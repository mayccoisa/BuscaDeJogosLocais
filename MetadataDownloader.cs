using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace BuscaDeJogosLocais
{
    /// <summary>
    /// Busca capa, ícone, fundo e demais metadados de um jogo usando as fontes de metadados
    /// que o usuário já tem instaladas no Playnite (IGDB, Xbox Metadata, etc.).
    ///
    /// O SDK do Playnite não expõe o download nativo nem a prioridade por campo configurada
    /// em Configurações › Metadados. O que dá para fazer é consultar os provedores instalados
    /// na ordem em que o Playnite os carrega e usar, para cada campo, a primeira resposta válida.
    /// Campos que o jogo já tem preenchidos nunca são sobrescritos.
    /// </summary>
    public class MetadataDownloader
    {
        private static readonly ILogger logger = LogManager.GetLogger();

        private readonly IPlayniteAPI api;
        private readonly List<MetadataPlugin> providers;

        public MetadataDownloader(IPlayniteAPI api)
        {
            this.api = api;
            providers = new List<MetadataPlugin>();

            try
            {
                foreach (var plugin in api.Addons.Plugins)
                {
                    var metadataPlugin = plugin as MetadataPlugin;
                    if (metadataPlugin != null)
                    {
                        providers.Add(metadataPlugin);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Não foi possível listar as fontes de metadados instaladas.");
            }
        }

        public bool HasProviders
        {
            get { return providers.Count > 0; }
        }

        public string ProvidersDescription
        {
            get { return string.Join(", ", providers.Select(p => p.Name).ToArray()); }
        }

        /// <summary>Preenche os metadados de um jogo. Devolve true se algo foi alterado.</summary>
        public bool Download(Guid gameId, CancellationToken cancelToken)
        {
            var game = api.Database.Games.Get(gameId);
            if (game == null)
            {
                return false;
            }

            var fieldArgs = new GetMetadataFieldArgs();
            bool changed = false;

            foreach (var provider in providers)
            {
                if (cancelToken.IsCancellationRequested)
                {
                    break;
                }

                if (IsComplete(game))
                {
                    break;
                }

                OnDemandMetadataProvider onDemand = null;
                try
                {
                    onDemand = provider.GetMetadataProvider(new MetadataRequestOptions(game, true));
                    if (onDemand == null)
                    {
                        continue;
                    }

                    changed |= FillFrom(game, onDemand, fieldArgs, cancelToken);
                }
                catch (Exception ex)
                {
                    logger.Error(ex, string.Format("Falha ao consultar metadados de '{0}' em {1}.", game.Name, provider.Name));
                }
                finally
                {
                    if (onDemand != null)
                    {
                        try { onDemand.Dispose(); } catch (Exception) { }
                    }
                }
            }

            if (changed)
            {
                api.Database.Games.Update(game);
            }

            return changed;
        }

        private bool FillFrom(Game game, OnDemandMetadataProvider provider, GetMetadataFieldArgs args, CancellationToken cancelToken)
        {
            var available = provider.AvailableFields;
            if (available == null || available.Count == 0)
            {
                return false;
            }

            bool changed = false;

            if (string.IsNullOrEmpty(game.CoverImage) && available.Contains(MetadataField.CoverImage))
            {
                var saved = SaveImage(provider.GetCoverImage(args), game.Id);
                if (saved != null) { game.CoverImage = saved; changed = true; }
            }

            if (cancelToken.IsCancellationRequested) return changed;

            if (string.IsNullOrEmpty(game.Icon) && available.Contains(MetadataField.Icon))
            {
                var saved = SaveImage(provider.GetIcon(args), game.Id);
                if (saved != null) { game.Icon = saved; changed = true; }
            }

            if (cancelToken.IsCancellationRequested) return changed;

            if (string.IsNullOrEmpty(game.BackgroundImage) && available.Contains(MetadataField.BackgroundImage))
            {
                var saved = SaveImage(provider.GetBackgroundImage(args), game.Id);
                if (saved != null) { game.BackgroundImage = saved; changed = true; }
            }

            if (cancelToken.IsCancellationRequested) return changed;

            if (string.IsNullOrEmpty(game.Description) && available.Contains(MetadataField.Description))
            {
                var description = provider.GetDescription(args);
                if (!string.IsNullOrEmpty(description)) { game.Description = description; changed = true; }
            }

            if (game.ReleaseDate == null && available.Contains(MetadataField.ReleaseDate))
            {
                var releaseDate = provider.GetReleaseDate(args);
                if (releaseDate != null) { game.ReleaseDate = releaseDate; changed = true; }
            }

            if (game.CommunityScore == null && available.Contains(MetadataField.CommunityScore))
            {
                var score = provider.GetCommunityScore(args);
                if (score != null) { game.CommunityScore = score; changed = true; }
            }

            if (game.CriticScore == null && available.Contains(MetadataField.CriticScore))
            {
                var score = provider.GetCriticScore(args);
                if (score != null) { game.CriticScore = score; changed = true; }
            }

            if (cancelToken.IsCancellationRequested) return changed;

            if (IsEmpty(game.GenreIds) && available.Contains(MetadataField.Genres))
            {
                var ids = ResolveIds(api.Database.Genres, provider.GetGenres(args));
                if (ids != null) { game.GenreIds = ids; changed = true; }
            }

            if (IsEmpty(game.DeveloperIds) && available.Contains(MetadataField.Developers))
            {
                var ids = ResolveIds(api.Database.Companies, provider.GetDevelopers(args));
                if (ids != null) { game.DeveloperIds = ids; changed = true; }
            }

            if (IsEmpty(game.PublisherIds) && available.Contains(MetadataField.Publishers))
            {
                var ids = ResolveIds(api.Database.Companies, provider.GetPublishers(args));
                if (ids != null) { game.PublisherIds = ids; changed = true; }
            }

            if (IsEmpty(game.FeatureIds) && available.Contains(MetadataField.Features))
            {
                var ids = ResolveIds(api.Database.Features, provider.GetFeatures(args));
                if (ids != null) { game.FeatureIds = ids; changed = true; }
            }

            if (cancelToken.IsCancellationRequested) return changed;

            if ((game.Links == null || game.Links.Count == 0) && available.Contains(MetadataField.Links))
            {
                var links = provider.GetLinks(args);
                if (links != null)
                {
                    var list = links.Where(l => l != null).ToList();
                    if (list.Count > 0)
                    {
                        game.Links = new System.Collections.ObjectModel.ObservableCollection<Link>(list);
                        changed = true;
                    }
                }
            }

            return changed;
        }

        /// <summary>Já tem tudo o que costuma importar numa importação local? Evita consultar provedor à toa.</summary>
        private static bool IsComplete(Game game)
        {
            return !string.IsNullOrEmpty(game.CoverImage)
                && !string.IsNullOrEmpty(game.Icon)
                && !string.IsNullOrEmpty(game.BackgroundImage)
                && !string.IsNullOrEmpty(game.Description)
                && game.ReleaseDate != null
                && !IsEmpty(game.GenreIds)
                && !IsEmpty(game.DeveloperIds)
                && !IsEmpty(game.PublisherIds);
        }

        private static bool IsEmpty(List<Guid> ids)
        {
            return ids == null || ids.Count == 0;
        }

        private List<Guid> ResolveIds<TItem>(IItemCollection<TItem> collection, IEnumerable<MetadataProperty> properties)
            where TItem : DatabaseObject
        {
            if (properties == null)
            {
                return null;
            }

            var list = properties.Where(p => p != null).ToList();
            if (list.Count == 0)
            {
                return null;
            }

            try
            {
                // O próprio Playnite resolve nome existente x novo item a partir das MetadataProperty.
                var items = collection.Add(list);
                if (items == null)
                {
                    return null;
                }

                var ids = items.Where(i => i != null).Select(i => i.Id).ToList();
                return ids.Count > 0 ? ids : null;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Falha ao registrar propriedades de metadados no banco.");
                return null;
            }
        }

        private string SaveImage(MetadataFile file, Guid gameId)
        {
            if (file == null)
            {
                return null;
            }

            try
            {
                if (file.HasContent)
                {
                    var fileName = string.IsNullOrEmpty(file.FileName) ? Guid.NewGuid().ToString() + ".jpg" : file.FileName;
                    var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), fileName);
                    System.IO.File.WriteAllBytes(temp, file.Content);

                    try
                    {
                        return api.Database.AddFile(temp, gameId);
                    }
                    finally
                    {
                        try { System.IO.File.Delete(temp); } catch (Exception) { }
                    }
                }

                if (!string.IsNullOrEmpty(file.Path))
                {
                    return api.Database.AddFile(file.Path, gameId);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Falha ao salvar imagem de metadados.");
            }

            return null;
        }
    }
}
