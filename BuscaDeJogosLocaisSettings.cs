using Playnite.SDK;
using Playnite.SDK.Data;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace BuscaDeJogosLocais
{
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> execute;
        private readonly Predicate<T> canExecute;
        public RelayCommand(Action<T> execute, Predicate<T> canExecute = null)
        {
            if (execute == null) throw new ArgumentNullException("execute");
            this.execute = execute;
            this.canExecute = canExecute;
        }
        public bool CanExecute(object parameter) { return canExecute == null || canExecute((T)parameter); }
        public void Execute(object parameter) { execute((T)parameter); }
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }

    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) { return !(bool)value; }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) { return !(bool)value; }
    }

    public class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged([CallerMemberName] string name = null)
        {
            if (PropertyChanged != null) PropertyChanged.Invoke(this, new PropertyChangedEventArgs(name));
        }
        public void SetValue<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (!EqualityComparer<T>.Default.Equals(field, value))
            {
                field = value;
                OnPropertyChanged(name);
            }
        }
    }

    public class ImportLogEntry : ObservableObject
    {
        public string DataImportacao { get; set; }
        public string NomeJogo { get; set; }
        public string Origem { get; set; }
        public string PastaMonitoradaPai { get; set; } // Adicionado para filtro
    }

    public class ScannedGame : ObservableObject
    {
        private bool selecionado;
        public bool Selecionado { get { return selecionado; } set { SetValue(ref selecionado, value); } }
        
        public string Nome { get; set; }
        public string CaminhoExe { get; set; }
        public string PastaRaiz { get; set; }
        public string PastaMonitoradaPai { get; set; } // Adicionado para filtro/agrupamento
        
        private bool jaExiste;
        public bool JaExiste { get { return jaExiste; } set { SetValue(ref jaExiste, value); OnPropertyChanged("Status"); } }
        
        public string Status { get { return JaExiste ? "Já na Biblioteca" : "Pendente"; } }
    }

    public class IntegrityResult : ObservableObject
    {
        public Guid GameId { get; set; }
        public string Name { get; set; }
        public string InstallDir { get; set; }
        public bool FolderMissing { get; set; }
        public bool ExeMissing { get; set; }
        
        private bool selected;
        public bool Selected { get { return selected; } set { SetValue(ref selected, value); } }

        public string StatusSummary {
            get {
                var s = new List<string>();
                if (FolderMissing) s.Add("Pasta Ausente");
                if (ExeMissing) s.Add("Executável Ausente");
                return string.Join(" | ", s);
            }
        }
    }

    public class RelinkGameItem : ObservableObject
    {
        public Guid GameId { get; set; }
        public string Nome { get; set; }
        public string CaminhoAntigoExe { get; set; }
        public string NovoCaminhoExe { get; set; }
        public string NovoInstallDirectory { get; set; }
        
        private string status;
        public string Status { get { return status; } set { SetValue(ref status, value); } }
        
        private bool selecionado;
        public bool Selecionado { get { return selecionado; } set { SetValue(ref selecionado, value); } }
    }

    public class ExcludedEntry : ObservableObject
    {
        public string CaminhoExe { get; set; }
        public string NomeJogo { get; set; }
        public string PastaMonitoradaPai { get; set; }
        public string DataExclusao { get; set; }
    }

    public class BuscaDeJogosLocaisSettings : ObservableObject
    {
        private ObservableCollection<string> pastas = new ObservableCollection<string>();
        public ObservableCollection<string> Pastas { get { return pastas; } set { SetValue(ref pastas, value); } }

        private ObservableCollection<ImportLogEntry> historicoImportacoes = new ObservableCollection<ImportLogEntry>();
        public ObservableCollection<ImportLogEntry> HistoricoImportacoes { get { return historicoImportacoes; } set { SetValue(ref historicoImportacoes, value); } }

        private bool tagDriveAsFeature = false;
        public bool TagDriveAsFeature { get { return tagDriveAsFeature; } set { SetValue(ref tagDriveAsFeature, value); } }

        private bool escanearAutomaticamente = false;
        public bool EscanearAutomaticamente { get { return escanearAutomaticamente; } set { SetValue(ref escanearAutomaticamente, value); } }

        private DateTime? dataUltimoScanAutomatico = null;
        public DateTime? DataUltimoScanAutomatico { get { return dataUltimoScanAutomatico; } set { SetValue(ref dataUltimoScanAutomatico, value); } }

        private int intervaloScanDias = 7;
        public int IntervaloScanDias { get { return intervaloScanDias; } set { SetValue(ref intervaloScanDias, value); } }

        private ObservableCollection<ExcludedEntry> caminhosIgnorados = new ObservableCollection<ExcludedEntry>();
        public ObservableCollection<ExcludedEntry> CaminhosIgnorados { get { return caminhosIgnorados; } set { SetValue(ref caminhosIgnorados, value); } }
    }

    public class BuscaDeJogosLocaisSettingsViewModel : ObservableObject, ISettings
    {
        private readonly BuscaDeJogosLocais plugin;
        private BuscaDeJogosLocaisSettings editingClone { get; set; }
        private BuscaDeJogosLocaisSettings settings;
        public BuscaDeJogosLocaisSettings Settings { get { return settings; } set { SetValue(ref settings, value); } }

        // Listas e Views para Filtros
        public ObservableCollection<ScannedGame> JogosEncontrados { get; private set; }
        public ICollectionView JogosEncontradosView { get; private set; }

        public ICollectionView HistoricoView { get; private set; }

        public ObservableCollection<RelinkGameItem> JogosParaRelinkar { get; private set; }
        public ICollectionView JogosParaRelinkarView { get; private set; }
        
        // Filtros Ativos
        private bool filtrarApenasNovos = false;
        public bool FiltrarApenasNovos { get { return filtrarApenasNovos; } set { SetValue(ref filtrarApenasNovos, value); JogosEncontradosView.Refresh(); } }

        private string filtroPastaSelecionada = "Todas as Pastas";
        public string FiltroPastaSelecionada
        {
            get { return filtroPastaSelecionada; }
            set
            {
                SetValue(ref filtroPastaSelecionada, value);
                JogosEncontradosView.Refresh();
                HistoricoView.Refresh();
                OnPropertyChanged("ScanButtonLabel");
                OnPropertyChanged("ScanHintText");
            }
        }

        public string ScanButtonLabel
        {
            get
            {
                return FiltroPastaSelecionada == "Todas as Pastas"
                    ? "ESCANEAR TODAS"
                    : "ESCANEAR PASTA SELECIONADA";
            }
        }

        public string ScanHintText
        {
            get
            {
                return FiltroPastaSelecionada == "Todas as Pastas"
                    ? "Escaneia todas as pastas monitoradas."
                    : string.Format("Escaneia apenas: {0}", FiltroPastaSelecionada);
            }
        }

        public List<string> OpcoesPastas
        {
            get
            {
                var list = new List<string> { "Todas as Pastas" };
                if (Settings.Pastas != null) list.AddRange(Settings.Pastas);
                return list;
            }
        }

        private string pastasEstatisticas = "Escaneie para ver as estatísticas...";
        public string PastasEstatisticas { get { return pastasEstatisticas; } set { SetValue(ref pastasEstatisticas, value); } }
        
        public RelayCommand<object> AddFolderCommand { get; private set; }
        public RelayCommand<object> ScanNowCommand { get; private set; }
        public RelayCommand<object> ImportSelectedCommand { get; private set; }
        public RelayCommand<object> ClearHistoryCommand { get; private set; }
        public RelayCommand<object> UpdateExistingGamesDriveCommand { get; private set; }
        
        public RelayCommand<object> SearchLostGamesCommand { get; private set; }
        public RelayCommand<object> RelinkSelectedCommand { get; private set; }

        public ICollectionView ExcludedView { get; private set; }
        public RelayCommand<object> RemoveExcludedCommand { get; private set; }

        public string UltimoScanTexto
        {
            get
            {
                if (Settings.DataUltimoScanAutomatico.HasValue)
                    return "Último scan automático: " + Settings.DataUltimoScanAutomatico.Value.ToString("dd/MM/yyyy HH:mm");
                return "Nenhum scan automático realizado ainda.";
            }
        }

        public BuscaDeJogosLocaisSettingsViewModel(BuscaDeJogosLocais plugin)
        {
            JogosEncontrados = new ObservableCollection<ScannedGame>();
            JogosParaRelinkar = new ObservableCollection<RelinkGameItem>();
            this.plugin = plugin;
            var savedSettings = plugin.LoadPluginSettings<BuscaDeJogosLocaisSettings>();
            if (savedSettings != null)
            {
                Settings = savedSettings;
                if (Settings.HistoricoImportacoes == null) Settings.HistoricoImportacoes = new ObservableCollection<ImportLogEntry>();
                if (Settings.Pastas == null) Settings.Pastas = new ObservableCollection<string>();
            }
            else
            {
                Settings = new BuscaDeJogosLocaisSettings();
            }

            if (Settings.CaminhosIgnorados == null) Settings.CaminhosIgnorados = new ObservableCollection<ExcludedEntry>();

            // Inicializar Views de Coleção
            JogosEncontradosView = CollectionViewSource.GetDefaultView(JogosEncontrados);
            JogosEncontradosView.Filter = FilterJogosDescobertos;
            JogosEncontradosView.GroupDescriptions.Add(new PropertyGroupDescription("PastaMonitoradaPai"));

            HistoricoView = CollectionViewSource.GetDefaultView(Settings.HistoricoImportacoes);
            HistoricoView.Filter = FilterHistorico;
            HistoricoView.GroupDescriptions.Add(new PropertyGroupDescription("PastaMonitoradaPai"));

            JogosParaRelinkarView = CollectionViewSource.GetDefaultView(JogosParaRelinkar);

            ExcludedView = CollectionViewSource.GetDefaultView(Settings.CaminhosIgnorados);

            RemoveExcludedCommand = new RelayCommand<object>((param) =>
            {
                var entry = param as ExcludedEntry;
                if (entry != null) Settings.CaminhosIgnorados.Remove(entry);
            });

            AddFolderCommand = new RelayCommand<object>((_) =>
            {
                var path = plugin.PlayniteApi.Dialogs.SelectFolder();
                if (!string.IsNullOrEmpty(path))
                {
                    if (!Settings.Pastas.Contains(path))
                    {
                        Settings.Pastas.Add(path);
                        OnPropertyChanged("OpcoesPastas");
                        RecalcularEstatisticas();
                    }
                }
            });

            ScanNowCommand = new RelayCommand<object>((_) =>
            {
                JogosEncontrados.Clear();
                string folderToScan = FiltroPastaSelecionada != "Todas as Pastas" ? FiltroPastaSelecionada : null;
                plugin.ExecuteManualScan(JogosEncontrados, folderToScan);
                RecalcularEstatisticas();
            });

            ImportSelectedCommand = new RelayCommand<object>((_) =>
            {
                var selecionados = JogosEncontrados.Where(j => j.Selecionado && !j.JaExiste).ToList();
                if (selecionados.Count == 0)
                {
                    plugin.PlayniteApi.Dialogs.ShowMessage("Nenhum jogo novo selecionado para importar.", "Aviso");
                    return;
                }

                int importados = 0;
                foreach (var jogo in selecionados)
                {
                    if (plugin.ImportarJogoManual(jogo))
                    {
                        importados++;
                        jogo.JaExiste = true;
                        jogo.Selecionado = false;
                    }
                }

                if (importados > 0)
                {
                    plugin.PlayniteApi.Dialogs.ShowMessage(string.Format("{0} jogos importados com sucesso!", importados), "Sucesso");
                    JogosEncontradosView.Refresh();
                    RecalcularEstatisticas();
                }
            });

            ClearHistoryCommand = new RelayCommand<object>((_) =>
            {
                Settings.HistoricoImportacoes.Clear();
            });

            UpdateExistingGamesDriveCommand = new RelayCommand<object>((_) =>
            {
                int atualizados = 0;
                var games = plugin.PlayniteApi.Database.Games.Where(g => g.PluginId == plugin.Id).ToList();
                
                foreach (var game in games)
                {
                    if (plugin.ApplyDriveTag(game))
                    {
                        atualizados++;
                        plugin.PlayniteApi.Database.Games.Update(game);
                    }
                }
                
                plugin.PlayniteApi.Dialogs.ShowMessage(string.Format("{0} jogos atualizados com a característica do HD.", atualizados), "Sucesso");
            });

            SearchLostGamesCommand = new RelayCommand<object>((_) =>
            {
                JogosParaRelinkar.Clear();

                plugin.PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
                {
                    var games = plugin.PlayniteApi.Database.Games.ToList();
                    
                    foreach (var game in games)
                    {
                        if (progressArgs.CancelToken.IsCancellationRequested) break;

                        if (game.IsInstalled) continue;
                        if (game.PluginId != Guid.Empty && game.PluginId != plugin.Id) continue;
                        if (game.GameActions == null || game.GameActions.Count == 0) continue;
                        if (game.GameActions.Any(a => a.Type == Playnite.SDK.Models.GameActionType.Emulator)) continue;

                        var fileAction = game.GameActions.FirstOrDefault(a => a.Type == Playnite.SDK.Models.GameActionType.File);
                        if (fileAction == null || string.IsNullOrEmpty(fileAction.Path)) continue;

                        string exeName = Path.GetFileName(fileAction.Path);
                        if (string.IsNullOrEmpty(exeName)) continue;

                        var relinkItem = new RelinkGameItem
                        {
                            GameId = game.Id,
                            Nome = game.Name,
                            CaminhoAntigoExe = fileAction.Path,
                            Status = "Buscando...",
                            Selecionado = false
                        };

                        plugin.PlayniteApi.MainView.UIDispatcher.Invoke(() => { JogosParaRelinkar.Add(relinkItem); });

                        string foundExePath = null;
                        string foundInstallDir = null;
                        
                        if (Settings.Pastas != null)
                        {
                            foreach (var pasta in Settings.Pastas)
                            {
                                if (!Directory.Exists(pasta)) continue;

                                try
                                {
                                    var stack = new Stack<string>();
                                    stack.Push(pasta);

                                    while (stack.Count > 0 && foundExePath == null)
                                    {
                                        if (progressArgs.CancelToken.IsCancellationRequested) break;

                                        string currentDir = stack.Pop();
                                        
                                        try
                                        {
                                            var files = Directory.EnumerateFiles(currentDir, exeName);
                                            var match = files.FirstOrDefault(f => Path.GetFileName(f).Equals(exeName, StringComparison.OrdinalIgnoreCase));
                                            
                                            if (match != null)
                                            {
                                                foundExePath = match;
                                                foundInstallDir = currentDir;
                                                
                                                string parentName = new DirectoryInfo(currentDir).Name;
                                                if (!parentName.Equals(game.Name, StringComparison.OrdinalIgnoreCase))
                                                {
                                                    string oldParentName = !string.IsNullOrEmpty(game.InstallDirectory) ? new DirectoryInfo(game.InstallDirectory).Name : "";
                                                    if (!string.IsNullOrEmpty(oldParentName) && !parentName.Equals(oldParentName, StringComparison.OrdinalIgnoreCase))
                                                    {
                                                        // Se o nome da pasta não bater nem com o nome do jogo nem com o anterior, ignora e continua procurando.
                                                        foundExePath = null;
                                                        foundInstallDir = null;
                                                    }
                                                }
                                            }
                                        }
                                        catch (UnauthorizedAccessException) { }
                                        catch (Exception) { }

                                        if (foundExePath == null)
                                        {
                                            try
                                            {
                                                var subDirs = Directory.EnumerateDirectories(currentDir);
                                                foreach (var d in subDirs) stack.Push(d);
                                            }
                                            catch (Exception) { }
                                        }
                                    }
                                }
                                catch (Exception) { }
                                
                                if (foundExePath != null) break;
                            }
                        }

                        plugin.PlayniteApi.MainView.UIDispatcher.Invoke(() =>
                        {
                            if (foundExePath != null)
                            {
                                relinkItem.NovoCaminhoExe = foundExePath;
                                relinkItem.NovoInstallDirectory = foundInstallDir;
                                relinkItem.Status = "Pronto para Relink";
                                relinkItem.Selecionado = true;
                            }
                            else
                            {
                                relinkItem.Status = "Não Encontrado";
                            }
                        });
                    }
                }, new Playnite.SDK.GlobalProgressOptions("Buscando novos endereços para jogos perdidos...", true));
            });

            RelinkSelectedCommand = new RelayCommand<object>((_) =>
            {
                var selecionados = JogosParaRelinkar.Where(j => j.Selecionado && !string.IsNullOrEmpty(j.NovoCaminhoExe)).ToList();
                if (selecionados.Count == 0)
                {
                    plugin.PlayniteApi.Dialogs.ShowMessage("Nenhum jogo selecionado e pronto para relink.", "Aviso");
                    return;
                }

                int atualizados = 0;
                foreach (var relink in selecionados)
                {
                    var game = plugin.PlayniteApi.Database.Games.Get(relink.GameId);
                    if (game == null) continue;

                    game.InstallDirectory = relink.NovoInstallDirectory;
                    game.IsInstalled = true;
                    
                    if (game.GameActions != null)
                    {
                        var fileAction = game.GameActions.FirstOrDefault(a => a.Type == Playnite.SDK.Models.GameActionType.File);
                        if (fileAction != null)
                        {
                            fileAction.Path = relink.NovoCaminhoExe;
                            fileAction.WorkingDir = "{InstallDir}";
                        }
                    }

                    plugin.PlayniteApi.Database.Games.Update(game);
                    
                    relink.Selecionado = false;
                    relink.Status = "Relinkado";
                    atualizados++;
                }

                if (atualizados > 0)
                {
                    plugin.PlayniteApi.Dialogs.ShowMessage(string.Format("{0} jogos foram relinkados com sucesso!", atualizados), "Sucesso");
                }
            });
        }

        private bool FilterJogosDescobertos(object item)
        {
            var game = (ScannedGame)item;
            if (FiltrarApenasNovos && game.JaExiste) return false;
            
            if (FiltroPastaSelecionada != "Todas as Pastas")
            {
                return game.PastaMonitoradaPai.Equals(FiltroPastaSelecionada, StringComparison.OrdinalIgnoreCase);
            }
            
            return true;
        }

        private bool FilterHistorico(object item)
        {
            if (FiltroPastaSelecionada == "Todas as Pastas") return true;
            var entry = (ImportLogEntry)item;
            return entry.PastaMonitoradaPai != null ? entry.PastaMonitoradaPai.Equals(FiltroPastaSelecionada, StringComparison.OrdinalIgnoreCase) : true;
        }

        public void RecalcularEstatisticas()
        {
            if (Settings.Pastas == null || Settings.Pastas.Count == 0)
            {
                PastasEstatisticas = "Nenhuma pasta monitorada no momento.";
                return;
            }

            var builder = new System.Text.StringBuilder();
            builder.AppendLine("📊 Resumo das Pastas Monitoradas:");
            foreach (var pasta in Settings.Pastas)
            {
                if (Directory.Exists(pasta))
                {
                    try {
                        var dirs = Directory.GetDirectories(pasta);
                        int totalPastas = dirs.Length;
                        int pastasVinculadas = dirs.Count(d => plugin.PlayniteApi.Database.Games.Any(g => 
                            g.InstallDirectory != null && 
                            plugin.NormalizePath(g.InstallDirectory).Equals(plugin.NormalizePath(d), StringComparison.OrdinalIgnoreCase)
                        ));
                        builder.AppendLine(string.Format("- {0}: {1} pastas ({2} vinculadas).", pasta, totalPastas, pastasVinculadas));
                    } catch (Exception) { builder.AppendLine(string.Format("- {0}: Erro de leitura.", pasta)); }
                }
                else builder.AppendLine(string.Format("- {0}: (Inacessível)", pasta));
            }
            PastasEstatisticas = builder.ToString();
        }

        public void BeginEdit() { editingClone = Serialization.GetClone(Settings); RecalcularEstatisticas(); OnPropertyChanged("OpcoesPastas"); }
        public void CancelEdit() { Settings = editingClone; }
        public void EndEdit() { plugin.SavePluginSettings(Settings); plugin.UpdateWatchers(); }
        public bool VerifySettings(out List<string> errors) { errors = new List<string>(); return true; }
    }
}
