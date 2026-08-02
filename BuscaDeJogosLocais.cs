using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using Playnite.SDK.Events;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;

namespace BuscaDeJogosLocais
{
    public class BuscaDeJogosLocais : LibraryPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private List<FileSystemWatcher> _vigias = new List<FileSystemWatcher>();
        public override Guid Id { get { return Guid.Parse("186d9374-4173-420d-b17a-e2ace45bb317"); } }
        public override string Name { get { return "Local"; } }
        public override LibraryClient Client { get { return null; } }

        private BuscaDeJogosLocaisSettingsViewModel settings;

        public BuscaDeJogosLocais(IPlayniteAPI api) : base(api)
        {
            Properties = new LibraryPluginProperties { HasSettings = true };
            settings = new BuscaDeJogosLocaisSettingsViewModel(this);
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            UpdateWatchers();
            VerificarScanAutomatico();
        }

        public void UpdateWatchers()
        {
            foreach (var watcher in _vigias) watcher.Dispose();
            _vigias.Clear();

            if (settings.Settings.Pastas != null)
            {
                foreach (var pasta in settings.Settings.Pastas)
                {
                    if (Directory.Exists(pasta))
                    {
                        var watcher = new FileSystemWatcher(pasta) { IncludeSubdirectories = true, EnableRaisingEvents = true };
                        watcher.Created += (s, e) => {
                            logger.Info(string.Format("Novo arquivo detectado: {0}", e.FullPath));
                        };
                        _vigias.Add(watcher);
                    }
                }
            }
        }

        public void ExecuteManualScan(ObservableCollection<ScannedGame> listToPopulate, string singleFolder = null)
        {
            PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
            {
                if (settings.Settings.Pastas == null || settings.Settings.Pastas.Count == 0) return;

                string agora = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
                var gamesFoundMap = new Dictionary<string, ScannedGame>(StringComparer.OrdinalIgnoreCase);
                var monitoredPaths = settings.Settings.Pastas.Select(p => NormalizePath(p)).ToList();

                var pastasParaEscanear = string.IsNullOrEmpty(singleFolder)
                    ? settings.Settings.Pastas.ToList()
                    : new List<string> { singleFolder };

                foreach (var pasta in pastasParaEscanear)
                {
                    if (Directory.Exists(pasta))
                    {
                        try 
                        {
                            var files = SafeEnumerateFiles(pasta, "*.exe", progressArgs.CancelToken);
                            foreach (var file in files)
                            {
                                if (progressArgs.CancelToken.IsCancellationRequested) break;
                                
                                if (IsExplosiveFile(file)) continue;
                                if (IsExcluded(file)) continue;

                                string monitoredPai;
                                string gameRoot = GetGameRootInternal(file, monitoredPaths, out monitoredPai);
                                if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot)) continue;

                                if (!gamesFoundMap.ContainsKey(gameRoot))
                                {
                                    bool jaExiste = PlayniteApi.Database.Games.Any(g => 
                                        g.InstallDirectory != null && 
                                        NormalizePath(g.InstallDirectory).Equals(NormalizePath(gameRoot), StringComparison.OrdinalIgnoreCase));

                                    string nomePasta = new DirectoryInfo(gameRoot).Name;
                                    string versaoDetectada;
                                    string nomeLimpo = LocalGameUtils.CleanGameName(nomePasta, out versaoDetectada);
                                    if (!settings.Settings.LimparNomeDaPasta) nomeLimpo = nomePasta;

                                    gamesFoundMap[gameRoot] = new ScannedGame
                                    {
                                        Nome = nomeLimpo,
                                        NomeOriginal = nomePasta,
                                        Versao = versaoDetectada == null ? string.Empty : versaoDetectada,
                                        CaminhoExe = file,
                                        PastaRaiz = gameRoot,
                                        PastaMonitoradaPai = monitoredPai,
                                        JaExiste = jaExiste,
                                        Selecionado = !jaExiste,
                                        UltimaVerificacao = agora
                                    };

                                    RegistrarVerificacao(gameRoot, agora);
                                }
                            }
                        }
                        catch (Exception ex) { logger.Error(ex, string.Format("Erro crítico ao escanear {0}", pasta)); }
                    }
                }
                
                var finalResults = gamesFoundMap.Values.OrderBy(g => g.Nome).ToList();
                PlayniteApi.MainView.UIDispatcher.Invoke(() => {
                    foreach (var g in finalResults) listToPopulate.Add(g);
                });

                try { SavePluginSettings(settings.Settings); } catch (Exception) { }
            }, new GlobalProgressOptions(
                string.IsNullOrEmpty(singleFolder)
                    ? "Buscando jogos locais em todas as pastas..."
                    : string.Format("Buscando jogos locais em {0}...", singleFolder),
                true));
        }

        private bool IsExplosiveFile(string path)
        {
            return LocalGameUtils.IsExplosiveFile(path);
        }

        // Carimba a pasta como "vista agora". É o que permite responder, meses depois,
        // "quando foi a última vez que este jogo estava mesmo no disco?".
        private void RegistrarVerificacao(string pastaRaiz, string quando)
        {
            if (settings.Settings.Verificacoes == null)
                settings.Settings.Verificacoes = new ObservableCollection<VerificacaoEntry>();

            var existente = settings.Settings.Verificacoes
                .FirstOrDefault(v => v.PastaRaiz != null && v.PastaRaiz.Equals(pastaRaiz, StringComparison.OrdinalIgnoreCase));

            if (existente != null) existente.Data = quando;
            else settings.Settings.Verificacoes.Add(new VerificacaoEntry { PastaRaiz = pastaRaiz, Data = quando });
        }

        /// <summary>Data do último scan que confirmou esta pasta, ou "—" se nunca foi verificada.</summary>
        public string ObterUltimaVerificacao(string pastaRaiz)
        {
            if (string.IsNullOrEmpty(pastaRaiz) || settings.Settings.Verificacoes == null) return "—";

            var entrada = settings.Settings.Verificacoes
                .FirstOrDefault(v => v.PastaRaiz != null && v.PastaRaiz.Equals(pastaRaiz, StringComparison.OrdinalIgnoreCase));

            return entrada != null ? entrada.Data : "—";
        }

        private bool IsExcluded(string caminhoExe)
        {
            if (settings == null || settings.Settings == null) return false;
            var ignorados = settings.Settings.CaminhosIgnorados;
            if (ignorados == null) return false;
            return LocalGameUtils.IsPathExcluded(caminhoExe, ignorados.Select(e => e.CaminhoExe));
        }

        public void AdicionarExcluido(ScannedGame jogo)
        {
            if (settings.Settings.CaminhosIgnorados.Any(e => e.CaminhoExe.Equals(jogo.CaminhoExe, StringComparison.OrdinalIgnoreCase)))
                return;

            settings.Settings.CaminhosIgnorados.Add(new ExcludedEntry
            {
                CaminhoExe = jogo.CaminhoExe,
                NomeJogo = jogo.Nome,
                PastaMonitoradaPai = jogo.PastaMonitoradaPai,
                DataExclusao = DateTime.Now.ToString("dd/MM/yyyy HH:mm")
            });
            SavePluginSettings(settings.Settings);
        }

        private void AbrirScanRapido()
        {
            if (settings.Settings.Pastas == null || settings.Settings.Pastas.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage("Nenhuma pasta monitorada configurada. Acesse as configurações da extensão para adicionar pastas.", "Busca de Jogos");
                return;
            }

            var resultados = new ObservableCollection<ScannedGame>();
            ExecuteManualScan(resultados);

            if (resultados.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage("Nenhum jogo encontrado nas pastas configuradas.", "Busca de Jogos");
                return;
            }

            var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMaximizeButton = true,
                ShowMinimizeButton = false
            });
            window.Title = string.Format("Jogos Encontrados ({0})", resultados.Count);
            window.Width = 900;
            window.Height = 600;
            window.Content = new ScanResultWindow(
                resultados,
                importar: (selecionados) =>
                {
                    var importados = new List<Guid>();
                    foreach (var j in selecionados)
                    {
                        Guid novoId;
                        if (ImportarJogoManual(j, out novoId))
                        {
                            j.JaExiste = true;
                            importados.Add(novoId);
                        }
                    }
                    BaixarMetadadosDosImportados(importados);
                },
                ignorar: (jogo) => AdicionarExcluido(jogo)
            );
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.ShowDialog();
        }

        private void VerificarScanAutomatico()
        {
            if (!settings.Settings.EscanearAutomaticamente) return;

            var ultimo = settings.Settings.DataUltimoScanAutomatico;
            int intervalo = settings.Settings.IntervaloScanDias;
            if (ultimo != null && (DateTime.Now - ultimo.Value).TotalDays < intervalo) return;

            if (settings.Settings.Pastas == null || settings.Settings.Pastas.Count == 0) return;

            var resultados = new ObservableCollection<ScannedGame>();
            ExecuteManualScan(resultados);

            var novos = resultados.Where(g => !g.JaExiste).ToList();

            settings.Settings.DataUltimoScanAutomatico = DateTime.Now;
            SavePluginSettings(settings.Settings);

            if (novos.Count == 0) return;

            PlayniteApi.MainView.UIDispatcher.Invoke(() =>
            {
                var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                {
                    ShowMaximizeButton = true,
                    ShowMinimizeButton = false
                });
                window.Title = string.Format("Scan Automático: {0} novo(s) jogo(s) encontrado(s)", novos.Count);
                window.Width = 900;
                window.Height = 600;
                window.Content = new ScanResultWindow(
                    new ObservableCollection<ScannedGame>(novos),
                    importar: (selecionados) =>
                    {
                        var importados = new List<Guid>();
                        foreach (var j in selecionados)
                        {
                            Guid novoId;
                            if (ImportarJogoManual(j, out novoId))
                            {
                                j.JaExiste = true;
                                importados.Add(novoId);
                            }
                        }
                        BaixarMetadadosDosImportados(importados);
                    },
                    ignorar: (jogo) => AdicionarExcluido(jogo)
                );
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                window.ShowDialog();
            });
        }

        private IEnumerable<string> SafeEnumerateFiles(string path, string searchPattern, System.Threading.CancellationToken cancelToken)
        {
            var stack = new Stack<string>();
            stack.Push(path);

            while (stack.Count > 0)
            {
                if (cancelToken.IsCancellationRequested) yield break;

                string currentDir = stack.Pop();
                
                // Tenta enumerar arquivos no diretório atual
                IEnumerable<string> files = null;
                try
                {
                    files = Directory.EnumerateFiles(currentDir, searchPattern);
                }
                catch (UnauthorizedAccessException)
                {
                    continue; 
                }
                catch (Exception ex)
                {
                    logger.Debug(string.Format("Erro ao enumerar arquivos em {0}: {1}", currentDir, ex.Message));
                    continue;
                }

                if (files != null)
                {
                    foreach (var file in files) yield return file;
                }

                // Tenta enumerar subdiretórios
                IEnumerable<string> subDirs = null;
                try
                {
                    subDirs = Directory.EnumerateDirectories(currentDir);
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                catch (Exception ex)
                {
                    logger.Debug(string.Format("Erro ao enumerar diretórios em {0}: {1}", currentDir, ex.Message));
                    continue;
                }

                if (subDirs != null)
                {
                    foreach (var dir in subDirs) stack.Push(dir);
                }
            }
        }

        public string NormalizePath(string path)
        {
            return LocalGameUtils.NormalizePath(path);
        }

        private string GetGameRootInternal(string filePath, List<string> monitoredPaths, out string monitoredPai)
        {
            return LocalGameUtils.GetGameRoot(filePath, monitoredPaths, out monitoredPai);
        }

        public bool ImportarJogoManual(ScannedGame scanned)
        {
            Guid ignorado;
            return ImportarJogoManual(scanned, out ignorado);
        }

        public bool ImportarJogoManual(ScannedGame scanned, out Guid gameId)
        {
            gameId = Guid.Empty;
            string normRoot = NormalizePath(scanned.PastaRaiz);
            if (PlayniteApi.Database.Games.Any(g => g.InstallDirectory != null && NormalizePath(g.InstallDirectory).Equals(normRoot, StringComparison.OrdinalIgnoreCase)))
                return false;

            var game = new Game(scanned.Nome)
            {
                PluginId = Id,
                InstallDirectory = scanned.PastaRaiz,
                IsInstalled = true,
                GameActions = new ObservableCollection<GameAction> { 
                    new GameAction { Type = GameActionType.File, Path = scanned.CaminhoExe, Name = "Jogar", WorkingDir = "{InstallDir}" } 
                }
            };

            if (settings.Settings.GuardarVersaoDetectada && !string.IsNullOrEmpty(scanned.Versao))
            {
                game.Version = scanned.Versao;
            }

            var tag = PlayniteApi.Database.Tags.Add("Importado Local");
            game.TagIds = new List<Guid> { tag.Id };

            if (settings.Settings.TagDriveAsFeature)
            {
                ApplyDriveTag(game);
            }

            ApplyLocalSource(game);

            PlayniteApi.Database.Games.Add(game);
            gameId = game.Id;

            try {
                settings.Settings.HistoricoImportacoes.Add(new ImportLogEntry { 
                    DataImportacao = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"), 
                    NomeJogo = scanned.Nome, 
                    Origem = scanned.PastaRaiz,
                    PastaMonitoradaPai = scanned.PastaMonitoradaPai
                });
                SavePluginSettings(settings.Settings);
            } catch (Exception) { }
            return true;
        }

        /// <summary>
        /// Roda o download de metadados (capa, ícone, fundo, descrição, gêneros...) nos jogos recém-importados,
        /// usando as fontes de metadados instaladas no Playnite. Respeita a opção da tela de configurações
        /// e nunca sobrescreve campo que o jogo já tenha preenchido.
        /// </summary>
        public void BaixarMetadadosDosImportados(List<Guid> gameIds, bool forcar = false)
        {
            if (!forcar && !settings.Settings.BaixarMetadadosAposImportar) return;
            if (gameIds == null || gameIds.Count == 0) return;

            var downloader = new MetadataDownloader(PlayniteApi);
            if (!downloader.HasProviders)
            {
                logger.Warn("Nenhuma fonte de metadados instalada no Playnite; download pós-importação ignorado.");
                PlayniteApi.Dialogs.ShowMessage(
                    "Os jogos foram importados, mas nenhuma fonte de metadados está instalada no Playnite (ex: IGDB).\n\n" +
                    "Instale uma fonte em Add-ons para que a capa e os metadados sejam baixados automaticamente.",
                    "Metadados");
                return;
            }

            int atualizados = RodarDownloadMetadados(downloader, gameIds, false);
            logger.Info(string.Format("Metadados preenchidos em {0} de {1} jogo(s).", atualizados, gameIds.Count));

            // Passada 1 é auto-match silencioso: o provedor devolve vazio quando não tem certeza do
            // título. Quem sobrou sem capa só resolve no modo manual, o mesmo da janela de edição —
            // e esse pode abrir uma janela por jogo, então é escolha do usuário, nunca automático.
            var semCapa = gameIds
                .Select(id => PlayniteApi.Database.Games.Get(id))
                .Where(g => g != null && string.IsNullOrEmpty(g.CoverImage))
                .Select(g => g.Id)
                .ToList();

            if (semCapa.Count == 0) return;

            var pergunta = string.Format(
                "{0} jogo(s) continuaram sem capa: as fontes não encontraram correspondência automática para o título.\n\n" +
                "Quer tentar de novo no modo manual? É o mesmo modo do \"Download metadata\" do Playnite, em que a fonte pode abrir uma janela para você escolher o jogo certo.",
                semCapa.Count);

            if (PlayniteApi.Dialogs.ShowMessage(pergunta, "Metadados", System.Windows.MessageBoxButton.YesNo)
                != System.Windows.MessageBoxResult.Yes) return;

            int manuais = RodarDownloadMetadados(downloader, semCapa, true);
            logger.Info(string.Format("Metadados manuais preenchidos em {0} de {1} jogo(s).", manuais, semCapa.Count));
        }

        private int RodarDownloadMetadados(MetadataDownloader downloader, List<Guid> gameIds, bool interativo)
        {
            int atualizados = 0;

            PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
            {
                progressArgs.ProgressMaxValue = gameIds.Count;

                foreach (var id in gameIds)
                {
                    if (progressArgs.CancelToken.IsCancellationRequested) break;

                    var jogo = PlayniteApi.Database.Games.Get(id);
                    progressArgs.Text = jogo != null
                        ? string.Format("Baixando metadados: {0}", jogo.Name)
                        : "Baixando metadados...";

                    try
                    {
                        if (downloader.Download(id, progressArgs.CancelToken, interativo)) atualizados++;
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, string.Format("Falha ao baixar metadados do jogo {0}.", id));
                    }

                    progressArgs.CurrentProgressValue++;
                }
            }, new GlobalProgressOptions(
                interativo ? "Buscando metadados (modo manual)..." : "Baixando metadados dos jogos...", true)
            { IsIndeterminate = false });

            return atualizados;
        }

        public override ISettings GetSettings(bool firstRun) { return settings; }
        public override UserControl GetSettingsView(bool firstRun) { return new BuscaDeJogosLocaisSettingsView(); }
        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args) { return new List<GameMetadata>(); }

        public override IEnumerable<PlayController> GetPlayActions(GetPlayActionsArgs args)
        {
            if (args.Game.PluginId != Id) yield break;

            var game = args.Game;
            if (game.GameActions != null && game.GameActions.Count > 0)
            {
                var action = game.GameActions[0];
                if (action.Type == GameActionType.File)
                {
                    yield return new AutomaticPlayController(game)
                    {
                        Path = action.Path,
                        Arguments = action.Arguments,
                        WorkingDir = action.WorkingDir,
                        Name = action.Name,
                        TrackingMode = TrackingMode.Default
                    };
                }
            }
        }

        public const string NomeFonteLocal = "Local";

        // Marca o jogo com a Fonte (Source) "Local" para exibição e filtros nativos do Playnite.
        public bool ApplyLocalSource(Game game)
        {
            try
            {
                var fonte = PlayniteApi.Database.Sources.Add(NomeFonteLocal);
                if (fonte != null && game.SourceId != fonte.Id)
                {
                    game.SourceId = fonte.Id;
                    return true;
                }
            }
            catch (Exception) { }
            return false;
        }

        // Marca um jogo como desinstalado (mantém na biblioteca, igual ao comportamento da Steam).
        public bool MarcarComoDesinstalado(Guid gameId)
        {
            var game = PlayniteApi.Database.Games.Get(gameId);
            if (game == null) return false;
            if (!game.IsInstalled) return false;
            game.IsInstalled = false;
            PlayniteApi.Database.Games.Update(game);

            try
            {
                if (settings.Settings.HistoricoDesinstalacoes == null)
                    settings.Settings.HistoricoDesinstalacoes = new ObservableCollection<UninstallLogEntry>();

                settings.Settings.HistoricoDesinstalacoes.Add(new UninstallLogEntry
                {
                    GameId = game.Id,
                    DataDesinstalacao = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"),
                    NomeJogo = game.Name,
                    Origem = game.InstallDirectory
                });
                SavePluginSettings(settings.Settings);
            }
            catch (Exception) { }

            return true;
        }

        public List<string> GetSavePatterns()
        {
            if (settings != null && settings.Settings != null && settings.Settings.PadroesSave != null && settings.Settings.PadroesSave.Count > 0)
                return settings.Settings.PadroesSave.ToList();
            return LocalGameUtils.DefaultSavePatterns.ToList();
        }

        // Percorre a pasta do jogo (recursivamente) procurando por um "save" segundo os padrões configurados.
        // Se houver save em qualquer nível dentro da pasta, NÃO é seguro apagar a pasta.
        public bool HasSaveInRoot(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return false;

            var patterns = GetSavePatterns();
            var dirNames = new List<string>();
            var fileNames = new List<string>();

            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                string cur = stack.Pop();

                try
                {
                    foreach (var d in Directory.EnumerateDirectories(cur))
                    {
                        dirNames.Add(new DirectoryInfo(d).Name);
                        stack.Push(d);
                    }
                }
                catch (Exception) { }

                try
                {
                    foreach (var f in Directory.EnumerateFiles(cur))
                        fileNames.Add(Path.GetFileName(f));
                }
                catch (Exception) { }

                if (LocalGameUtils.MatchesSavePattern(dirNames, fileNames, patterns))
                    return true;
            }

            return false;
        }

        // Envia a pasta do jogo duplicado para a Lixeira, remove o jogo da biblioteca e registra no histórico.
        public bool RemoverDuplicata(DuplicateGameItem item)
        {
            if (item == null) return false;

            string destino = "Lixeira";
            bool pastaOk = true;

            try
            {
                if (!string.IsNullOrEmpty(item.InstallDir) && Directory.Exists(item.InstallDir))
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                        item.InstallDir,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, string.Format("Falha ao enviar pasta para a Lixeira: {0}", item.InstallDir));
                destino = "Falha";
                pastaOk = false;
            }

            if (!pastaOk) return false;

            bool removidoDb = false;
            try
            {
                var game = PlayniteApi.Database.Games.Get(item.GameId);
                if (game != null)
                {
                    PlayniteApi.Database.Games.Remove(item.GameId);
                    removidoDb = true;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, string.Format("Falha ao remover jogo duplicado da biblioteca: {0}", item.Nome));
            }

            try
            {
                if (settings.Settings.HistoricoDuplicatasRemovidas == null)
                    settings.Settings.HistoricoDuplicatasRemovidas = new ObservableCollection<DuplicateRemovalLogEntry>();

                settings.Settings.HistoricoDuplicatasRemovidas.Add(new DuplicateRemovalLogEntry
                {
                    DataRemocao = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"),
                    NomeJogo = item.Nome,
                    PastaRemovida = item.InstallDir,
                    TinhaSave = item.TemSaveNaRaiz ? "Sim" : "Não",
                    Destino = destino,
                    RemovidoDaBiblioteca = removidoDb
                });
            }
            catch (Exception) { }

            return true;
        }

        public bool ApplyDriveTag(Game game)
        {
            if (string.IsNullOrEmpty(game.InstallDirectory)) return false;
            try
            {
                string drive = Path.GetPathRoot(game.InstallDirectory);
                if (!string.IsNullOrEmpty(drive))
                {
                    string featureName = string.Format("HD {0}", drive.TrimEnd('\\'));
                    var feature = PlayniteApi.Database.Features.Add(featureName);
                    if (game.FeatureIds == null) game.FeatureIds = new List<Guid>();
                    if (!game.FeatureIds.Contains(feature.Id))
                    {
                        game.FeatureIds.Add(feature.Id);
                        return true;
                    }
                }
            } catch (Exception) { }
            return false;
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            yield return new MainMenuItem
            {
                Description = "Buscar Novos Jogos",
                MenuSection = "@Local",
                Action = (mainMenuItemArgs) => AbrirScanRapido()
            };

            yield return new MainMenuItem
            {
                Description = "Limpar Nomes dos Jogos Locais",
                MenuSection = "@Local",
                Action = (mainMenuItemArgs) => LimparNomesDosJogosLocais()
            };

            yield return new MainMenuItem
            {
                Description = "Verificar Integridade da Biblioteca",
                MenuSection = "@Local",
                Action = (mainMenuItemArgs) => CheckLibraryIntegrity()
            };
        }

        /// <summary>
        /// Reaplica a limpeza de nome (e a versão detectada) nos jogos que já foram importados
        /// por esta extensão. Só mexe em jogo cujo nome realmente muda; mostra prévia antes de gravar.
        /// </summary>
        public void LimparNomesDosJogosLocais()
        {
            var jogos = PlayniteApi.Database.Games.Where(g => g.PluginId == Id).ToList();
            var itens = new ObservableCollection<RenameItem>();

            foreach (var jogo in jogos)
            {
                // A versão sai do nome da pasta quando existe; o nome do jogo é o fallback.
                string baseNome = jogo.Name;
                string origem = !string.IsNullOrEmpty(jogo.InstallDirectory) && Directory.Exists(jogo.InstallDirectory)
                    ? new DirectoryInfo(jogo.InstallDirectory).Name
                    : jogo.Name;

                string versaoPasta;
                LocalGameUtils.CleanGameName(origem, out versaoPasta);

                string versaoNome;
                string limpo = LocalGameUtils.CleanGameName(baseNome, out versaoNome);
                string versao = versaoNome != null ? versaoNome : versaoPasta;

                bool mudaNome = !string.IsNullOrEmpty(limpo) && limpo != jogo.Name;
                bool ganhaVersao = !string.IsNullOrEmpty(versao) && string.IsNullOrEmpty(jogo.Version);

                if (!mudaNome && !ganhaVersao) continue;

                itens.Add(new RenameItem
                {
                    GameId = jogo.Id,
                    NomeAtual = jogo.Name,
                    NomeNovo = string.IsNullOrEmpty(limpo) ? jogo.Name : limpo,
                    Versao = string.IsNullOrEmpty(versao) ? (jogo.Version == null ? string.Empty : jogo.Version) : versao,
                    Selecionado = true
                });
            }

            if (itens.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage("Nenhum jogo local precisa de ajuste de nome.", "Limpar Nomes");
                return;
            }

            var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMaximizeButton = true,
                ShowMinimizeButton = false
            });
            window.Title = string.Format("Limpar Nomes ({0})", itens.Count);
            window.Width = 950;
            window.Height = 600;
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            // A renomeação grava na hora; a busca de metadados só roda DEPOIS que a modal fecha,
            // senão a barra de progresso e os diálogos da fonte nascem atrás da janela.
            var renomeados = new List<Guid>();
            window.Content = new RenamePreviewWindow(itens, (selecionados) => renomeados.AddRange(AplicarRenomeacoes(selecionados)));
            window.ShowDialog();

            if (renomeados.Count == 0) return;

            var resposta = PlayniteApi.Dialogs.ShowMessage(
                string.Format("{0} jogo(s) atualizados.\n\nQuer buscar capa e metadados agora para esses jogos, com o nome já corrigido?", renomeados.Count),
                "Limpar Nomes", System.Windows.MessageBoxButton.YesNo);

            if (resposta == System.Windows.MessageBoxResult.Yes)
            {
                BaixarMetadadosDosImportados(renomeados, true);
            }
        }

        // Grava as renomeações confirmadas na prévia. Só toca no que o usuário deixou marcado.
        // Devolve os ids efetivamente alterados, para a busca de metadados rodar depois.
        private List<Guid> AplicarRenomeacoes(List<RenameItem> selecionados)
        {
            var alterados = new List<Guid>();

            foreach (var item in selecionados)
            {
                var jogo = PlayniteApi.Database.Games.Get(item.GameId);
                if (jogo == null) continue;

                try
                {
                    bool mudou = false;

                    if (!string.IsNullOrEmpty(item.NomeNovo) && item.NomeNovo != jogo.Name)
                    {
                        jogo.Name = item.NomeNovo.Trim();
                        mudou = true;
                    }

                    if (!string.IsNullOrEmpty(item.Versao) && item.Versao != jogo.Version)
                    {
                        jogo.Version = item.Versao.Trim();
                        mudou = true;
                    }

                    if (mudou)
                    {
                        PlayniteApi.Database.Games.Update(jogo);
                        alterados.Add(jogo.Id);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, string.Format("Falha ao renomear o jogo {0}", item.NomeAtual));
                }
            }

            return alterados;
        }

        public void CheckLibraryIntegrity()
        {
            var results = new ObservableCollection<IntegrityResult>();
            PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
            {
                var games = PlayniteApi.Database.Games.ToList();
                foreach (var game in games)
                {
                    if (progressArgs.CancelToken.IsCancellationRequested) break;

                    bool folderMissing = false;
                    bool exeMissing = false;

                    if (!string.IsNullOrEmpty(game.InstallDirectory))
                    {
                        if (!Directory.Exists(game.InstallDirectory))
                        {
                            folderMissing = true;
                        }
                    }

                    if (game.GameActions != null && game.GameActions.Any(a => a.Type == GameActionType.File))
                    {
                        var primaryAction = game.GameActions.FirstOrDefault(a => a.Type == GameActionType.File);
                        if (primaryAction != null)
                        {
                            string fullPath = PlayniteApi.ExpandGameVariables(game, primaryAction.Path);
                            if (!File.Exists(fullPath))
                            {
                                exeMissing = true;
                            }
                        }
                    }

                    if (folderMissing || exeMissing)
                    {
                        PlayniteApi.MainView.UIDispatcher.Invoke(() =>
                        {
                            results.Add(new IntegrityResult
                            {
                                GameId = game.Id,
                                Name = game.Name,
                                InstallDir = game.InstallDirectory,
                                FolderMissing = folderMissing,
                                ExeMissing = exeMissing,
                                Selected = true
                            });
                        });
                    }
                }
            }, new GlobalProgressOptions("Verificando integridade da biblioteca...", true));

            if (results.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage("Nenhum problema de integridade encontrado.", "Scanner de Integridade");
                return;
            }

            var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMaximizeButton = false,
                ShowMinimizeButton = false
            });

            window.Title = "Integridade da Biblioteca - Itens Ausentes";
            window.Content = new IntegrityResultView(
                results,
                onRemove: (selectedItems) =>
                {
                    if (selectedItems.Count == 0)
                    {
                        PlayniteApi.Dialogs.ShowMessage("Nenhum item selecionado.", "Aviso");
                        return;
                    }
                    if (PlayniteApi.Dialogs.ShowMessage(string.Format("Tem certeza que deseja remover {0} jogo(s) da biblioteca?", selectedItems.Count), "Confirmar Remoção", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    {
                        foreach (var item in selectedItems)
                        {
                            PlayniteApi.Database.Games.Remove(item.GameId);
                        }
                        PlayniteApi.Dialogs.ShowMessage(string.Format("{0} jogo(s) removido(s).", selectedItems.Count), "Sucesso");
                        window.Close();
                    }
                },
                onMarkUninstalled: (selectedItems) =>
                {
                    if (selectedItems.Count == 0)
                    {
                        PlayniteApi.Dialogs.ShowMessage("Nenhum item selecionado.", "Aviso");
                        return;
                    }
                    int marcados = 0;
                    foreach (var item in selectedItems)
                    {
                        if (MarcarComoDesinstalado(item.GameId)) marcados++;
                    }
                    PlayniteApi.Dialogs.ShowMessage(string.Format("{0} jogo(s) marcado(s) como desinstalado(s).", marcados), "Sucesso");
                    window.Close();
                },
                onClose: () => window.Close());

            window.SizeToContent = SizeToContent.WidthAndHeight;
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.ShowDialog();
        }
    }
}
