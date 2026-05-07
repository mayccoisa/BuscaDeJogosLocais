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
        public override string Name { get { return "Busca de Jogos Locais"; } }
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

                                    gamesFoundMap[gameRoot] = new ScannedGame
                                    {
                                        Nome = new DirectoryInfo(gameRoot).Name,
                                        CaminhoExe = file,
                                        PastaRaiz = gameRoot,
                                        PastaMonitoradaPai = monitoredPai,
                                        JaExiste = jaExiste,
                                        Selecionado = !jaExiste
                                    };
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
            }, new GlobalProgressOptions(
                string.IsNullOrEmpty(singleFolder)
                    ? "Buscando jogos locais em todas as pastas..."
                    : string.Format("Buscando jogos locais em {0}...", singleFolder),
                true));
        }

        private bool IsExplosiveFile(string path)
        {
            string p = path.ToLowerInvariant();
            if (!p.EndsWith(".exe")) return true;
            string[] blacklist = { "unins", "setup", "crash", "helper", "update", "redist", "bugreport", "sendreport", "steamerrorreporter", "dxwebsetup", "dotnet", "vcredist", "tool", "media", "storybook" };
            return blacklist.Any(b => p.Contains(b));
        }

        private bool IsExcluded(string caminhoExe)
        {
            if (settings == null || settings.Settings == null) return false;
            var ignorados = settings.Settings.CaminhosIgnorados;
            if (ignorados == null) return false;
            return ignorados.Any(e => e.CaminhoExe.Equals(caminhoExe, StringComparison.OrdinalIgnoreCase));
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
                    foreach (var j in selecionados)
                    {
                        if (ImportarJogoManual(j))
                            j.JaExiste = true;
                    }
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
                        foreach (var j in selecionados)
                        {
                            if (ImportarJogoManual(j))
                                j.JaExiste = true;
                        }
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
            if (string.IsNullOrEmpty(path)) return string.Empty;
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
        }

        private string GetGameRootInternal(string filePath, List<string> monitoredPaths, out string monitoredPai)
        {
            monitoredPai = "Desconhecida";
            DirectoryInfo current = new DirectoryInfo(Path.GetDirectoryName(filePath));
            while (current != null && current.Parent != null)
            {
                string currentParentPath = NormalizePath(current.Parent.FullName);
                if (monitoredPaths.Contains(currentParentPath))
                {
                    monitoredPai = current.Parent.FullName;
                    return current.FullName;
                }
                current = current.Parent;
            }
            return null;
        }

        public bool ImportarJogoManual(ScannedGame scanned)
        {
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
            
            var tag = PlayniteApi.Database.Tags.Add("Importado Local");
            game.TagIds = new List<Guid> { tag.Id };

            if (settings.Settings.TagDriveAsFeature)
            {
                ApplyDriveTag(game);
            }

            PlayniteApi.Database.Games.Add(game);
            
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
                MenuSection = "@Busca de Jogos Locais",
                Action = (mainMenuItemArgs) => AbrirScanRapido()
            };

            yield return new MainMenuItem
            {
                Description = "Verificar Integridade da Biblioteca",
                MenuSection = "@Busca de Jogos Locais",
                Action = (mainMenuItemArgs) => CheckLibraryIntegrity()
            };
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
            window.Content = new IntegrityResultView(results, (selectedItems) =>
            {
                if (selectedItems.Count > 0)
                {
                    if (PlayniteApi.Dialogs.ShowMessage(string.Format("Tem certeza que deseja remover {0} jogos da biblioteca?", selectedItems.Count), "Confirmar Remoção", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    {
                        foreach (var item in selectedItems)
                        {
                            PlayniteApi.Database.Games.Remove(item.GameId);
                        }
                        PlayniteApi.Dialogs.ShowMessage(string.Format("{0} jogos removidos.", selectedItems.Count), "Sucesso");
                        window.Close();
                    }
                }
                else
                {
                    window.Close();
                }
            });

            window.SizeToContent = SizeToContent.WidthAndHeight;
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.ShowDialog();
        }
    }
}
