using Playnite.SDK;
using Playnite.SDK.Data;
using Playnite.SDK.Models;
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

    public class UninstallLogEntry : ObservableObject
    {
        public Guid GameId { get; set; }
        public string DataDesinstalacao { get; set; }
        public string NomeJogo { get; set; }
        public string Origem { get; set; } // Pasta de instalação no momento da desinstalação
    }

    public class ScannedGame : ObservableObject
    {
        private bool selecionado;
        public bool Selecionado { get { return selecionado; } set { SetValue(ref selecionado, value); } }
        
        private string nome;
        // Nome já limpo (editável na tela de resultados antes de importar).
        public string Nome { get { return nome; } set { SetValue(ref nome, value); } }

        // Nome cru da pasta, preservado para diagnóstico/tooltip.
        public string NomeOriginal { get; set; }

        private string versao;
        // Versão extraída do nome da pasta (ex.: "1.00.1", "20260729"); vazia quando não há.
        public string Versao { get { return versao; } set { SetValue(ref versao, value); } }

        public string CaminhoExe { get; set; }
        public string PastaRaiz { get; set; }
        public string PastaMonitoradaPai { get; set; } // Adicionado para filtro/agrupamento
        
        // Quando o scan viu esta pasta pela última vez ("—" enquanto nunca foi verificada).
        private string ultimaVerificacao = "—";
        public string UltimaVerificacao { get { return ultimaVerificacao; } set { SetValue(ref ultimaVerificacao, value); } }

        private bool jaExiste;
        public bool JaExiste { get { return jaExiste; } set { SetValue(ref jaExiste, value); OnPropertyChanged("Status"); } }

        public string Status { get { return JaExiste ? "Já na Biblioteca" : "Pendente"; } }
    }

    // Um jogo (ou candidato a jogo) visto de dentro de uma pasta monitorada.
    public class PastaJogoItem : ObservableObject
    {
        public Guid GameId { get; set; }        // Guid.Empty quando ainda não está na biblioteca
        public string Nome { get; set; }        // nome na biblioteca, ou o nome da pasta
        public string NomePasta { get; set; }
        public string Caminho { get; set; }
        public string Versao { get; set; }
        public string Status { get; set; }      // "Na biblioteca", "Não importado", "Pasta ausente", "Ignorado"
    }

    // Resumo de uma pasta monitorada: quanto dela virou biblioteca e quanto ficou de fora.
    public class PastaResumo : ObservableObject
    {
        public string Caminho { get; set; }
        public bool ExisteEmDisco { get; set; }
        public int TotalSubpastas { get; set; }
        public int NaBiblioteca { get; set; }
        public int NaoImportados { get; set; }
        public int ComProblema { get; set; }
        public int Ignorados { get; set; }

        public List<PastaJogoItem> Itens { get; set; }

        public string StatusTexto
        {
            get
            {
                if (!ExisteEmDisco) return "Pasta inacessível";
                if (TotalSubpastas == 0) return "Pasta vazia";
                if (NaBiblioteca == 0) return "Nada importado";
                if (NaoImportados == 0 && ComProblema == 0) return "Tudo importado";
                return "Parcialmente importado";
            }
        }
    }

    // Uma linha da prévia de limpeza de nomes de jogos já importados.
    public class RenameItem : ObservableObject
    {
        public Guid GameId { get; set; }
        public string NomeAtual { get; set; }

        private string nomeNovo;
        public string NomeNovo { get { return nomeNovo; } set { SetValue(ref nomeNovo, value); } }

        private string versao;
        public string Versao { get { return versao; } set { SetValue(ref versao, value); } }

        private bool selecionado = true;
        public bool Selecionado { get { return selecionado; } set { SetValue(ref selecionado, value); } }
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

    // Uma instalação individual pertencente a um grupo de jogos duplicados.
    public class DuplicateGameItem : ObservableObject
    {
        public Guid GameId { get; set; }
        public string Nome { get; set; }
        public string NomeNormalizado { get; set; } // usado para agrupar no DataGrid
        public string InstallDir { get; set; }
        public string CaminhoExe { get; set; }

        private bool temSaveNaRaiz;
        public bool TemSaveNaRaiz { get { return temSaveNaRaiz; } set { SetValue(ref temSaveNaRaiz, value); OnPropertyChanged("SaveStatus"); } }

        public string SaveStatus { get { return TemSaveNaRaiz ? "Save na pasta" : "Sem save (seguro)"; } }

        private bool selecionado;
        public bool Selecionado { get { return selecionado; } set { SetValue(ref selecionado, value); } }

        private string status = "";
        public string Status { get { return status; } set { SetValue(ref status, value); } }
    }

    // Registro de monitoramento de cada pasta de duplicata removida.
    public class DuplicateRemovalLogEntry : ObservableObject
    {
        public string DataRemocao { get; set; }
        public string NomeJogo { get; set; }
        public string PastaRemovida { get; set; }
        public string TinhaSave { get; set; }        // "Sim"/"Não"
        public string Destino { get; set; }          // "Lixeira" / "Falha"
        public bool RemovidoDaBiblioteca { get; set; }
    }

    // Carimbo da última vez que o scan confirmou que a pasta de um jogo estava no disco.
    public class VerificacaoEntry : ObservableObject
    {
        public string PastaRaiz { get; set; }
        public string Data { get; set; }   // dd/MM/yyyy HH:mm
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

        private ObservableCollection<UninstallLogEntry> historicoDesinstalacoes = new ObservableCollection<UninstallLogEntry>();
        public ObservableCollection<UninstallLogEntry> HistoricoDesinstalacoes { get { return historicoDesinstalacoes; } set { SetValue(ref historicoDesinstalacoes, value); } }

        private bool tagDriveAsFeature = false;
        public bool TagDriveAsFeature { get { return tagDriveAsFeature; } set { SetValue(ref tagDriveAsFeature, value); } }

        // Limpa o nome da pasta no padrão "scene release" ao importar (tira versão, grupo e ruído).
        private bool limparNomeDaPasta = true;
        public bool LimparNomeDaPasta { get { return limparNomeDaPasta; } set { SetValue(ref limparNomeDaPasta, value); } }

        // Grava a versão detectada no campo "Versão" do jogo no Playnite.
        private bool guardarVersaoDetectada = true;
        public bool GuardarVersaoDetectada { get { return guardarVersaoDetectada; } set { SetValue(ref guardarVersaoDetectada, value); } }

        private bool baixarMetadadosAposImportar = true;
        public bool BaixarMetadadosAposImportar { get { return baixarMetadadosAposImportar; } set { SetValue(ref baixarMetadadosAposImportar, value); } }

        private bool escanearAutomaticamente = false;
        public bool EscanearAutomaticamente { get { return escanearAutomaticamente; } set { SetValue(ref escanearAutomaticamente, value); } }

        private DateTime? dataUltimoScanAutomatico = null;
        public DateTime? DataUltimoScanAutomatico { get { return dataUltimoScanAutomatico; } set { SetValue(ref dataUltimoScanAutomatico, value); } }

        private int intervaloScanDias = 7;
        public int IntervaloScanDias { get { return intervaloScanDias; } set { SetValue(ref intervaloScanDias, value); } }

        private ObservableCollection<ExcludedEntry> caminhosIgnorados = new ObservableCollection<ExcludedEntry>();
        public ObservableCollection<ExcludedEntry> CaminhosIgnorados { get { return caminhosIgnorados; } set { SetValue(ref caminhosIgnorados, value); } }

        // Padrões que identificam um "save" dentro da pasta de um jogo (nomes de pasta/arquivo ou extensões como ".sav").
        private ObservableCollection<string> padroesSave = new ObservableCollection<string>();
        public ObservableCollection<string> PadroesSave { get { return padroesSave; } set { SetValue(ref padroesSave, value); } }

        // Última vez que cada pasta de jogo foi vista pelo scan.
        private ObservableCollection<VerificacaoEntry> verificacoes = new ObservableCollection<VerificacaoEntry>();
        public ObservableCollection<VerificacaoEntry> Verificacoes { get { return verificacoes; } set { SetValue(ref verificacoes, value); } }

        private ObservableCollection<DuplicateRemovalLogEntry> historicoDuplicatasRemovidas = new ObservableCollection<DuplicateRemovalLogEntry>();
        public ObservableCollection<DuplicateRemovalLogEntry> HistoricoDuplicatasRemovidas { get { return historicoDuplicatasRemovidas; } set { SetValue(ref historicoDuplicatasRemovidas, value); } }
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

        public ICollectionView HistoricoDesinstalacoesView { get; private set; }
        public RelayCommand<object> ReinstalarCommand { get; private set; }
        public RelayCommand<object> ClearUninstallHistoryCommand { get; private set; }

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
        public RelayCommand<object> MarkNotFoundAsUninstalledCommand { get; private set; }
        public RelayCommand<object> ApplyLocalSourceToExistingCommand { get; private set; }
        public RelayCommand<object> LimparNomesExistentesCommand { get; private set; }

        // Resumo por pasta monitorada: quantos jogos dela estão na biblioteca e quantos ficaram de fora.
        public ObservableCollection<PastaResumo> PastasResumo { get; private set; }
        public RelayCommand<object> VerJogosDaPastaCommand { get; private set; }
        public RelayCommand<object> AtualizarResumoPastasCommand { get; private set; }

        public ICollectionView ExcludedView { get; private set; }
        public RelayCommand<object> RemoveExcludedCommand { get; private set; }

        // Duplicados
        public ObservableCollection<DuplicateGameItem> Duplicatas { get; private set; }
        public ICollectionView DuplicatasView { get; private set; }
        public ICollectionView DuplicatasRemovidasView { get; private set; }
        public RelayCommand<object> SearchDuplicatesCommand { get; private set; }
        public RelayCommand<object> RemoveDuplicatesCommand { get; private set; }
        public RelayCommand<object> ClearDuplicateHistoryCommand { get; private set; }
        public RelayCommand<object> AddSavePatternCommand { get; private set; }
        public RelayCommand<object> RemoveSavePatternCommand { get; private set; }

        // Atualização da própria extensão
        private readonly UpdateChecker updateChecker;
        public RelayCommand<object> CheckUpdateCommand { get; private set; }

        public string VersaoInstaladaTexto
        {
            get { return string.Format("Versão instalada: {0}", updateChecker.CurrentVersion); }
        }

        public string RepositorioUrl
        {
            get { return updateChecker.ReleasesUrl; }
        }

        // Onde o auxiliar da atualização registra o que fez — é o que explica uma troca que não pegou.
        public RelayCommand<object> AbrirLogAtualizacaoCommand { get; private set; }

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
            PastasResumo = new ObservableCollection<PastaResumo>();
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
            if (Settings.HistoricoDesinstalacoes == null) Settings.HistoricoDesinstalacoes = new ObservableCollection<UninstallLogEntry>();
            if (Settings.HistoricoDuplicatasRemovidas == null) Settings.HistoricoDuplicatasRemovidas = new ObservableCollection<DuplicateRemovalLogEntry>();
            if (Settings.PadroesSave == null) Settings.PadroesSave = new ObservableCollection<string>();
            if (Settings.PadroesSave.Count == 0)
            {
                foreach (var p in LocalGameUtils.DefaultSavePatterns) Settings.PadroesSave.Add(p);
            }

            Duplicatas = new ObservableCollection<DuplicateGameItem>();

            updateChecker = new UpdateChecker(
                plugin.PlayniteApi,
                "mayccoisa/BuscaDeJogosLocais",
                "186d9374-4173-420d-b17a-e2ace45bb317",
                UpdateChecker.DefaultExtensionDir);

            CheckUpdateCommand = new RelayCommand<object>((_) => updateChecker.CheckInteractive());

            AbrirLogAtualizacaoCommand = new RelayCommand<object>((_) =>
            {
                var caminho = UpdateChecker.UpdateLogPath;
                if (!File.Exists(caminho))
                {
                    plugin.PlayniteApi.Dialogs.ShowMessage(
                        "Ainda não há registro de atualização nesta máquina.\n\nO log é criado quando você manda instalar uma versão nova.",
                        "Log da atualização");
                    return;
                }

                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(caminho) { UseShellExecute = true }); }
                catch (Exception) { plugin.PlayniteApi.Dialogs.ShowMessage(caminho, "Log da atualização"); }
            });

            // Inicializar Views de Coleção
            JogosEncontradosView = CollectionViewSource.GetDefaultView(JogosEncontrados);
            JogosEncontradosView.Filter = FilterJogosDescobertos;
            JogosEncontradosView.GroupDescriptions.Add(new PropertyGroupDescription("PastaMonitoradaPai"));

            HistoricoView = CollectionViewSource.GetDefaultView(Settings.HistoricoImportacoes);
            HistoricoView.Filter = FilterHistorico;
            HistoricoView.GroupDescriptions.Add(new PropertyGroupDescription("PastaMonitoradaPai"));

            JogosParaRelinkarView = CollectionViewSource.GetDefaultView(JogosParaRelinkar);

            ExcludedView = CollectionViewSource.GetDefaultView(Settings.CaminhosIgnorados);
            HistoricoDesinstalacoesView = CollectionViewSource.GetDefaultView(Settings.HistoricoDesinstalacoes);

            DuplicatasView = CollectionViewSource.GetDefaultView(Duplicatas);
            DuplicatasView.GroupDescriptions.Add(new PropertyGroupDescription("Nome"));
            DuplicatasRemovidasView = CollectionViewSource.GetDefaultView(Settings.HistoricoDuplicatasRemovidas);

            RemoveExcludedCommand = new RelayCommand<object>((param) =>
            {
                var entry = param as ExcludedEntry;
                if (entry != null) Settings.CaminhosIgnorados.Remove(entry);
            });

            AddSavePatternCommand = new RelayCommand<object>((_) =>
            {
                var texto = plugin.PlayniteApi.Dialogs.SelectString(
                    "Digite um nome de pasta/arquivo de save (ex: saves) ou uma extensão (ex: .sav):",
                    "Adicionar padrão de save", "");
                if (texto != null && texto.Result && !string.IsNullOrEmpty(texto.SelectedString))
                {
                    string valor = texto.SelectedString.Trim();
                    if (valor.Length > 0 && !Settings.PadroesSave.Any(p => p.Equals(valor, StringComparison.OrdinalIgnoreCase)))
                        Settings.PadroesSave.Add(valor);
                }
            });

            RemoveSavePatternCommand = new RelayCommand<object>((param) =>
            {
                var valor = param as string;
                if (valor != null) Settings.PadroesSave.Remove(valor);
            });

            ClearDuplicateHistoryCommand = new RelayCommand<object>((_) =>
            {
                Settings.HistoricoDuplicatasRemovidas.Clear();
                plugin.SavePluginSettings(Settings);
            });

            SearchDuplicatesCommand = new RelayCommand<object>((_) =>
            {
                Duplicatas.Clear();

                plugin.PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
                {
                    // Considera apenas jogos locais (do próprio plugin).
                    var locais = plugin.PlayniteApi.Database.Games
                        .Where(g => g.PluginId == plugin.Id && !string.IsNullOrEmpty(g.InstallDirectory))
                        .ToList();

                    var grupos = locais
                        .GroupBy(g => LocalGameUtils.NormalizeGameName(g.Name))
                        .Where(grp => grp.Count() > 1)
                        .ToList();

                    var novos = new List<DuplicateGameItem>();
                    foreach (var grupo in grupos)
                    {
                        if (progressArgs.CancelToken.IsCancellationRequested) break;

                        var itensGrupo = new List<DuplicateGameItem>();
                        foreach (var game in grupo)
                        {
                            if (progressArgs.CancelToken.IsCancellationRequested) break;

                            string exe = null;
                            if (game.GameActions != null)
                            {
                                var fa = game.GameActions.FirstOrDefault(a => a.Type == Playnite.SDK.Models.GameActionType.File);
                                if (fa != null) exe = fa.Path;
                            }

                            bool temSave = plugin.HasSaveInRoot(game.InstallDirectory);

                            itensGrupo.Add(new DuplicateGameItem
                            {
                                GameId = game.Id,
                                Nome = game.Name,
                                NomeNormalizado = grupo.Key,
                                InstallDir = game.InstallDirectory,
                                CaminhoExe = exe,
                                TemSaveNaRaiz = temSave,
                                Status = ""
                            });
                        }

                        // Pré-seleção: marca para remoção as cópias SEM save na raiz,
                        // mas garante que ao menos uma cópia do grupo permaneça.
                        var semSave = itensGrupo.Where(i => !i.TemSaveNaRaiz).ToList();
                        int marcaveis = semSave.Count;
                        if (marcaveis >= itensGrupo.Count && itensGrupo.Count > 0)
                        {
                            // Todas sem save: mantém a primeira sem marcar.
                            marcaveis = itensGrupo.Count - 1;
                        }
                        int marcadas = 0;
                        foreach (var item in itensGrupo)
                        {
                            if (!item.TemSaveNaRaiz && marcadas < marcaveis)
                            {
                                item.Selecionado = true;
                                marcadas++;
                            }
                        }

                        novos.AddRange(itensGrupo);
                    }

                    plugin.PlayniteApi.MainView.UIDispatcher.Invoke(() =>
                    {
                        foreach (var item in novos.OrderBy(i => i.NomeNormalizado)) Duplicatas.Add(item);
                    });
                }, new Playnite.SDK.GlobalProgressOptions("Procurando jogos duplicados...", true));

                if (Duplicatas.Count == 0)
                {
                    plugin.PlayniteApi.Dialogs.ShowMessage("Nenhum jogo local duplicado foi encontrado.", "Duplicados");
                }
            });

            RemoveDuplicatesCommand = new RelayCommand<object>((_) =>
            {
                var selecionados = Duplicatas.Where(d => d.Selecionado).ToList();
                if (selecionados.Count == 0)
                {
                    plugin.PlayniteApi.Dialogs.ShowMessage("Nenhuma cópia selecionada para remoção.", "Aviso");
                    return;
                }

                // Segurança: nunca remover TODAS as cópias de um mesmo jogo.
                var gruposSel = selecionados.GroupBy(d => d.NomeNormalizado);
                foreach (var grupo in gruposSel)
                {
                    int totalGrupo = Duplicatas.Count(d => d.NomeNormalizado == grupo.Key);
                    if (grupo.Count() >= totalGrupo)
                    {
                        plugin.PlayniteApi.Dialogs.ShowMessage(
                            string.Format("Você selecionou todas as cópias de \"{0}\". Deixe ao menos uma cópia sem marcar.", grupo.First().Nome),
                            "Operação bloqueada");
                        return;
                    }
                }

                int comSave = selecionados.Count(d => d.TemSaveNaRaiz);
                string aviso = string.Format("Serão enviadas para a Lixeira {0} pasta(s) de jogos duplicados e removidas da biblioteca.", selecionados.Count);
                if (comSave > 0)
                    aviso += string.Format("\n\nATENÇÃO: {0} dela(s) contêm um save na pasta e podem perder progresso!", comSave);
                aviso += "\n\nDeseja continuar?";

                if (plugin.PlayniteApi.Dialogs.ShowMessage(aviso, "Confirmar remoção de duplicados", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                    return;

                int removidos = 0;
                foreach (var item in selecionados)
                {
                    if (plugin.RemoverDuplicata(item))
                    {
                        removidos++;
                        Duplicatas.Remove(item);
                    }
                    else
                    {
                        item.Status = "Falha ao remover";
                    }
                }

                plugin.SavePluginSettings(Settings);
                plugin.PlayniteApi.Dialogs.ShowMessage(string.Format("{0} pasta(s) removida(s) e enviada(s) para a Lixeira.", removidos), "Concluído");
            });

            ReinstalarCommand = new RelayCommand<object>((param) =>
            {
                var entry = param as UninstallLogEntry;
                if (entry == null) return;

                var game = plugin.PlayniteApi.Database.Games.Get(entry.GameId);
                if (game == null)
                {
                    plugin.PlayniteApi.Dialogs.ShowMessage("O jogo não existe mais na biblioteca. A entrada será removida do histórico.", "Aviso");
                    Settings.HistoricoDesinstalacoes.Remove(entry);
                    plugin.SavePluginSettings(Settings);
                    return;
                }

                game.IsInstalled = true;
                plugin.PlayniteApi.Database.Games.Update(game);
                Settings.HistoricoDesinstalacoes.Remove(entry);
                plugin.SavePluginSettings(Settings);

                plugin.PlayniteApi.Dialogs.ShowMessage(string.Format("'{0}' marcado como instalado novamente. Se a pasta mudou, use a aba 'Reparar Desinstalados' para reapontar o caminho.", game.Name), "Sucesso");
            });

            ClearUninstallHistoryCommand = new RelayCommand<object>((_) =>
            {
                Settings.HistoricoDesinstalacoes.Clear();
                plugin.SavePluginSettings(Settings);
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

                var idsImportados = new List<Guid>();
                foreach (var jogo in selecionados)
                {
                    Guid novoId;
                    if (plugin.ImportarJogoManual(jogo, out novoId))
                    {
                        idsImportados.Add(novoId);
                        jogo.JaExiste = true;
                        jogo.Selecionado = false;
                    }
                }

                if (idsImportados.Count > 0)
                {
                    plugin.PlayniteApi.Dialogs.ShowMessage(string.Format("{0} jogos importados com sucesso!", idsImportados.Count), "Sucesso");
                    JogosEncontradosView.Refresh();
                    RecalcularEstatisticas();
                    plugin.BaixarMetadadosDosImportados(idsImportados);
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

            ApplyLocalSourceToExistingCommand = new RelayCommand<object>((_) =>
            {
                int atualizados = 0;
                var games = plugin.PlayniteApi.Database.Games.Where(g => g.PluginId == plugin.Id).ToList();

                foreach (var game in games)
                {
                    if (plugin.ApplyLocalSource(game))
                    {
                        atualizados++;
                        plugin.PlayniteApi.Database.Games.Update(game);
                    }
                }

                plugin.PlayniteApi.Dialogs.ShowMessage(string.Format("{0} jogo(s) marcado(s) com a Fonte \"Local\".", atualizados), "Sucesso");
            });

            LimparNomesExistentesCommand = new RelayCommand<object>((_) =>
            {
                plugin.LimparNomesDosJogosLocais();
                RecalcularEstatisticas();
            });

            AtualizarResumoPastasCommand = new RelayCommand<object>((_) => RecalcularEstatisticas());

            VerJogosDaPastaCommand = new RelayCommand<object>((param) =>
            {
                var resumo = param as PastaResumo;
                if (resumo == null) return;

                var window = plugin.PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                {
                    ShowMaximizeButton = true,
                    ShowMinimizeButton = false
                });
                window.Title = string.Format("Jogos em {0}", resumo.Caminho);
                window.Width = 950;
                window.Height = 600;
                window.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
                window.Content = new FolderGamesWindow(resumo);
                window.ShowDialog();
            });

            MarkNotFoundAsUninstalledCommand = new RelayCommand<object>((_) =>
            {
                var alvos = JogosParaRelinkar.Where(j => j.Selecionado && j.Status == "Não Encontrado").ToList();
                if (alvos.Count == 0)
                {
                    plugin.PlayniteApi.Dialogs.ShowMessage("Selecione um ou mais jogos com status \"Não Encontrado\" para marcar como desinstalados.", "Aviso");
                    return;
                }

                foreach (var item in alvos)
                {
                    plugin.MarcarComoDesinstalado(item.GameId);
                    item.Status = "Desinstalado";
                    item.Selecionado = false;
                }

                plugin.PlayniteApi.Dialogs.ShowMessage(string.Format("{0} jogo(s) marcado(s) como desinstalado(s).", alvos.Count), "Sucesso");
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

                        if (game.PluginId != Guid.Empty && game.PluginId != plugin.Id) continue;
                        if (game.GameActions == null || game.GameActions.Count == 0) continue;
                        if (game.GameActions.Any(a => a.Type == Playnite.SDK.Models.GameActionType.Emulator)) continue;

                        var fileAction = game.GameActions.FirstOrDefault(a => a.Type == Playnite.SDK.Models.GameActionType.File);
                        if (fileAction == null || string.IsNullOrEmpty(fileAction.Path)) continue;

                        // Jogos instalados cujo executável ainda existe estão saudáveis: não precisam de relink.
                        if (game.IsInstalled)
                        {
                            string caminhoAtual = plugin.PlayniteApi.ExpandGameVariables(game, fileAction.Path);
                            if (File.Exists(caminhoAtual)) continue;
                        }

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
                                // Já vem marcado para agilizar a marcação em lote como desinstalado.
                                relinkItem.Selecionado = true;
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

        /// <summary>
        /// Monta, para cada pasta monitorada, o que dela já virou jogo na biblioteca e o que ficou
        /// de fora. O "candidato" é a subpasta de primeiro nível — a mesma unidade que o scan usa
        /// como raiz de jogo —, então o que aparece como "não importado" é exatamente o que o
        /// scan enxergaria ali.
        /// </summary>
        public void RecalcularResumoPastas()
        {
            PastasResumo.Clear();

            if (Settings.Pastas == null) return;

            var jogosLocais = plugin.PlayniteApi.Database.Games
                .Where(g => g.PluginId == plugin.Id && !string.IsNullOrEmpty(g.InstallDirectory))
                .ToList();

            var ignorados = Settings.CaminhosIgnorados != null
                ? Settings.CaminhosIgnorados.Select(e => e.CaminhoExe).Where(c => !string.IsNullOrEmpty(c)).ToList()
                : new List<string>();

            foreach (var pasta in Settings.Pastas)
            {
                var resumo = new PastaResumo
                {
                    Caminho = pasta,
                    ExisteEmDisco = Directory.Exists(pasta),
                    Itens = new List<PastaJogoItem>()
                };

                // Jogos da biblioteca que apontam para dentro desta pasta monitorada.
                var jogosDaPasta = jogosLocais
                    .Where(g => LocalGameUtils.IsUnderFolder(g.InstallDirectory, pasta))
                    .ToList();

                var vinculados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var jogo in jogosDaPasta)
                {
                    vinculados.Add(plugin.NormalizePath(jogo.InstallDirectory));

                    bool pastaSumiu = !Directory.Exists(jogo.InstallDirectory);
                    if (pastaSumiu) resumo.ComProblema++; else resumo.NaBiblioteca++;

                    resumo.Itens.Add(new PastaJogoItem
                    {
                        GameId = jogo.Id,
                        Nome = jogo.Name,
                        NomePasta = SafeFolderName(jogo.InstallDirectory),
                        Caminho = jogo.InstallDirectory,
                        Versao = jogo.Version,
                        Status = pastaSumiu ? "Pasta ausente" : "Na biblioteca"
                    });
                }

                if (resumo.ExisteEmDisco)
                {
                    string[] subpastas;
                    try { subpastas = Directory.GetDirectories(pasta); }
                    catch (Exception) { subpastas = new string[0]; }

                    resumo.TotalSubpastas = subpastas.Length;

                    foreach (var sub in subpastas)
                    {
                        if (vinculados.Contains(plugin.NormalizePath(sub))) continue;

                        bool foiIgnorada = ignorados.Any(exe => LocalGameUtils.IsUnderFolder(exe, sub));
                        if (foiIgnorada) resumo.Ignorados++; else resumo.NaoImportados++;

                        resumo.Itens.Add(new PastaJogoItem
                        {
                            GameId = Guid.Empty,
                            Nome = LocalGameUtils.CleanGameNameOnly(SafeFolderName(sub)),
                            NomePasta = SafeFolderName(sub),
                            Caminho = sub,
                            Versao = LocalGameUtils.ExtractVersion(SafeFolderName(sub)),
                            Status = foiIgnorada ? "Ignorado" : "Não importado"
                        });
                    }
                }

                PastasResumo.Add(resumo);
            }

            // Jogos locais que não caem em nenhuma pasta monitorada: some da vista se não mostrar.
            var orfaos = jogosLocais
                .Where(g => !Settings.Pastas.Any(p => LocalGameUtils.IsUnderFolder(g.InstallDirectory, p)))
                .ToList();

            if (orfaos.Count > 0)
            {
                var resumo = new PastaResumo
                {
                    Caminho = "(fora das pastas monitoradas)",
                    ExisteEmDisco = true,
                    TotalSubpastas = orfaos.Count,
                    NaBiblioteca = orfaos.Count,
                    Itens = orfaos.Select(g => new PastaJogoItem
                    {
                        GameId = g.Id,
                        Nome = g.Name,
                        NomePasta = SafeFolderName(g.InstallDirectory),
                        Caminho = g.InstallDirectory,
                        Versao = g.Version,
                        Status = Directory.Exists(g.InstallDirectory) ? "Na biblioteca" : "Pasta ausente"
                    }).ToList()
                };
                resumo.ComProblema = resumo.Itens.Count(i => i.Status == "Pasta ausente");
                resumo.NaBiblioteca -= resumo.ComProblema;
                PastasResumo.Add(resumo);
            }
        }

        private static string SafeFolderName(string path)
        {
            try { return new DirectoryInfo(path).Name; }
            catch (Exception) { return path; }
        }

        /// <summary>
        /// Preenche a aba de busca com os jogos locais que JÁ estão na biblioteca, sem varrer o disco.
        /// Assim a tela abre mostrando o que existe (e desde quando foi visto pela última vez) em vez
        /// de uma lista vazia esperando um scan.
        /// </summary>
        public void CarregarJogosDaBiblioteca()
        {
            JogosEncontrados.Clear();

            var jogosLocais = plugin.PlayniteApi.Database.Games
                .Where(g => g.PluginId == plugin.Id && !string.IsNullOrEmpty(g.InstallDirectory))
                .OrderBy(g => g.Name)
                .ToList();

            foreach (var jogo in jogosLocais)
            {
                string pastaPai = Settings.Pastas != null
                    ? Settings.Pastas.FirstOrDefault(p => LocalGameUtils.IsUnderFolder(jogo.InstallDirectory, p))
                    : null;

                JogosEncontrados.Add(new ScannedGame
                {
                    Nome = jogo.Name,
                    NomeOriginal = jogo.Name,
                    Versao = jogo.Version == null ? string.Empty : jogo.Version,
                    CaminhoExe = ExePrincipal(jogo),
                    PastaRaiz = jogo.InstallDirectory,
                    PastaMonitoradaPai = pastaPai != null ? pastaPai : "(fora das pastas monitoradas)",
                    JaExiste = true,
                    Selecionado = false,
                    UltimaVerificacao = plugin.ObterUltimaVerificacao(jogo.InstallDirectory)
                });
            }
        }

        private static string ExePrincipal(Game jogo)
        {
            if (jogo.GameActions == null) return string.Empty;
            var acao = jogo.GameActions.FirstOrDefault(a => a.Type == GameActionType.File && !string.IsNullOrEmpty(a.Path));
            return acao != null ? acao.Path : string.Empty;
        }

        public void RecalcularEstatisticas()
        {
            RecalcularResumoPastas();

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

        public void BeginEdit() { editingClone = Serialization.GetClone(Settings); RecalcularEstatisticas(); CarregarJogosDaBiblioteca(); OnPropertyChanged("OpcoesPastas"); }
        public void CancelEdit() { Settings = editingClone; }
        public void EndEdit() { plugin.SavePluginSettings(Settings); plugin.UpdateWatchers(); }
        public bool VerifySettings(out List<string> errors) { errors = new List<string>(); return true; }
    }
}
