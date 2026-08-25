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

    /// <summary>
    /// Quantas subpastas cada pasta monitorada tem no disco. Preenchido no cálculo do resumo e
    /// consultado pelo cabeçalho de grupo da lista de busca, para ele poder dizer "17/20" em vez
    /// de só "17 itens" — o denominador é o que revela o que ficou de fora.
    /// </summary>
    public static class PastaTotais
    {
        private static readonly Dictionary<string, int> totais =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public static void Registrar(string pasta, int totalSubpastas)
        {
            if (string.IsNullOrEmpty(pasta)) return;
            totais[pasta.TrimEnd('\\', '/')] = totalSubpastas;
        }

        public static void Limpar() { totais.Clear(); }

        /// <summary>Total conhecido, ou -1 quando a pasta nunca foi resumida.</summary>
        public static int Obter(string pasta)
        {
            if (string.IsNullOrEmpty(pasta)) return -1;
            int total;
            return totais.TryGetValue(pasta.TrimEnd('\\', '/'), out total) ? total : -1;
        }
    }

    /// <summary>Monta o rótulo "17/20 pastas" do cabeçalho de grupo (nome do grupo + itens do grupo).</summary>
    public class GroupCountConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2) return string.Empty;

            var pasta = values[0] as string;
            int itens = values[1] is int ? (int)values[1] : 0;

            int total = PastaTotais.Obter(pasta);
            if (total < 0 || total < itens) return string.Format(" ({0} itens)", itens);

            return string.Format(" ({0}/{1} pastas)", itens, total);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
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

        // ---- Pasta que mudou de lugar ----
        // Sem isto, uma pasta movida ou renomeada aparecia como "Pendente" (jogo novo para
        // importar) enquanto o registro antigo virava "pasta ausente" — dois problemas no lugar
        // de um, e o jogo duplicado na biblioteca se a pessoa importasse.
        private Guid movidoDeGameId;
        public Guid MovidoDeGameId { get { return movidoDeGameId; } set { SetValue(ref movidoDeGameId, value); OnPropertyChanged("Status"); OnPropertyChanged("EhMudancaDePasta"); } }

        public string MovidoDePasta { get; set; }
        public string MovidoDeNome { get; set; }

        private string motivoMudanca;
        public string MotivoMudanca { get { return motivoMudanca; } set { SetValue(ref motivoMudanca, value); } }

        private string confiancaMudanca;
        public string ConfiancaMudanca { get { return confiancaMudanca; } set { SetValue(ref confiancaMudanca, value); } }

        public bool EhMudancaDePasta { get { return MovidoDeGameId != Guid.Empty; } }

        public string Status
        {
            get
            {
                if (EhMudancaDePasta) return "Mudou de pasta";
                return JaExiste ? "Já na Biblioteca" : "Pendente";
            }
        }
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

    // Uma linha da prévia de "Completar Biblioteca": o console que o jogo de emulação vai passar
    // a mostrar como Fonte, e de onde essa informação foi lida.
    public class ConsoleLibraryItem : ObservableObject
    {
        public Guid GameId { get; set; }
        public string NomeJogo { get; set; }
        public string Emulador { get; set; }
        public string Perfil { get; set; }
        public string PlataformaDoJogo { get; set; }
        public string BibliotecaAtual { get; set; }
        /// <summary>De onde saiu a sugestão: "Emulador", "Plataforma do jogo" ou "—".</summary>
        public string Origem { get; set; }
        public string Situacao { get; set; }

        private string bibliotecaNova;
        public string BibliotecaNova { get { return bibliotecaNova; } set { SetValue(ref bibliotecaNova, value); } }

        private bool selecionado;
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

        // Pasta onde a extensão encontrou este jogo agora. Vazio = não achou em lugar nenhum.
        // A tela de integridade só oferecia remover ou desinstalar; sem esta busca, o jogo que
        // simplesmente mudou de pasta era tratado como jogo que sumiu.
        public string NovaPasta { get; set; }
        public string NovoExe { get; set; }
        public string Motivo { get; set; }
        public string Confianca { get; set; }

        public bool TemNovaCasa { get { return !string.IsNullOrEmpty(NovaPasta); } }

        public string StatusSummary {
            get {
                if (TemNovaCasa) return "Mudou de pasta";
                var s = new List<string>();
                if (FolderMissing) s.Add("Pasta Ausente");
                if (ExeMissing) s.Add("Executável Ausente");
                return string.Join(" | ", s);
            }
        }

        // O que a tela mostra na coluna de detalhe: para onde foi, ou por que não foi achado.
        public string Detalhe
        {
            get
            {
                if (TemNovaCasa) return NovaPasta + "  (" + Motivo + ")";
                return InstallDir;
            }
        }
    }

    // Um executável encontrado no disco, com o tamanho — que é o sinal capaz de distinguir
    // a MESMA cópia de um jogo homônimo.
    public class ExeEmDisco
    {
        public string Caminho { get; set; }
        public long Tamanho { get; set; }
    }

    // Uma pasta de jogo encontrada no disco durante a indexação das pastas monitoradas.
    public class PastaEmDisco
    {
        public string Pasta { get; set; }
        public string PastaMonitoradaPai { get; set; }
        public List<ExeEmDisco> Exes { get; set; }
    }

    public class RelinkGameItem : ObservableObject
    {
        public Guid GameId { get; set; }
        public string Nome { get; set; }
        public string CaminhoAntigoExe { get; set; }
        public string PastaAntiga { get; set; }
        public string NovoCaminhoExe { get; set; }
        public string NovoInstallDirectory { get; set; }

        // Por que a extensão acha que é este jogo. Sem isto a tela pede fé: a pessoa vê um
        // caminho novo e não tem como saber se o casamento foi por prova ou por palpite.
        private string motivo;
        public string Motivo { get { return motivo; } set { SetValue(ref motivo, value); } }

        // "Alta" | "Média" | vazio quando não há candidato.
        private string confianca;
        public string Confianca { get { return confianca; } set { SetValue(ref confianca, value); } }

        private string status;
        public string Status { get { return status; } set { SetValue(ref status, value); } }

        private bool selecionado;
        public bool Selecionado { get { return selecionado; } set { SetValue(ref selecionado, value); } }

        public bool TemCandidato { get { return !string.IsNullOrEmpty(NovoCaminhoExe); } }
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

        // Uma linha de total, agora que o detalhe por pasta vive na grade acima.
        private string pastasResumoTexto = "";
        public string PastasResumoTexto { get { return pastasResumoTexto; } set { SetValue(ref pastasResumoTexto, value); } }
        
        public RelayCommand<object> AddFolderCommand { get; private set; }
        public RelayCommand<object> ScanNowCommand { get; private set; }
        public RelayCommand<object> ImportSelectedCommand { get; private set; }
        public RelayCommand<object> ReapontarMovidosCommand { get; private set; }
        public RelayCommand<object> ClearHistoryCommand { get; private set; }
        public RelayCommand<object> UpdateExistingGamesDriveCommand { get; private set; }
        
        public RelayCommand<object> SearchLostGamesCommand { get; private set; }
        public RelayCommand<object> RelinkSelectedCommand { get; private set; }
        public RelayCommand<object> MarkNotFoundAsUninstalledCommand { get; private set; }
        public RelayCommand<object> ApplyLocalSourceToExistingCommand { get; private set; }
        public RelayCommand<object> LimparNomesExistentesCommand { get; private set; }
        public RelayCommand<object> CompletarBibliotecaCommand { get; private set; }

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
                AtualizarContagemDeMovidos();
            });

            // Reaponta os jogos que o scan reconheceu como "mudou de pasta". Sem este botão a
            // única saída para uma pasta movida era importar de novo (duplicando o jogo) ou
            // marcar como desinstalado (perdendo o jogo que está ali, no disco, funcionando).
            ReapontarMovidosCommand = new RelayCommand<object>((_) =>
            {
                var movidos = JogosEncontrados.Where(j => j.EhMudancaDePasta).ToList();
                if (movidos.Count == 0)
                {
                    plugin.PlayniteApi.Dialogs.ShowMessage(
                        "Nenhum jogo mudou de pasta nesta busca.",
                        "Reapontar");
                    return;
                }

                var linhas = movidos.Take(12).Select(m => string.Format("• {0}\n    de: {1}\n    para: {2}\n    ({3})", m.MovidoDeNome, m.MovidoDePasta, m.PastaRaiz, m.MotivoMudanca));
                string resumo = string.Join("\n\n", linhas.ToArray());
                if (movidos.Count > 12) resumo = resumo + string.Format("\n\n...e mais {0}.", movidos.Count - 12);

                if (plugin.PlayniteApi.Dialogs.ShowMessage(
                        string.Format("Reapontar {0} jogo(s) para a pasta onde estão hoje?\n\n{1}\n\nO registro do jogo é o mesmo: tempo de jogo, capa e tags ficam como estão.", movidos.Count, resumo),
                        "Reapontar jogos que mudaram de pasta",
                        System.Windows.MessageBoxButton.YesNo) != System.Windows.MessageBoxResult.Yes)
                {
                    return;
                }

                int ok = 0;
                using (plugin.PlayniteApi.Database.BufferedUpdate())
                {
                    foreach (var m in movidos)
                    {
                        if (!plugin.RelocarJogo(m.MovidoDeGameId, m.PastaRaiz, m.CaminhoExe)) continue;
                        m.MovidoDeGameId = Guid.Empty;
                        m.JaExiste = true;
                        m.Selecionado = false;
                        ok++;
                    }
                }

                JogosEncontradosView.Refresh();
                RecalcularEstatisticas();
                AtualizarContagemDeMovidos();
                plugin.PlayniteApi.Dialogs.ShowMessage(
                    string.Format("{0} jogo(s) reapontados para a pasta atual.", ok), "Sucesso");
            });

            ImportSelectedCommand = new RelayCommand<object>((_) =>
            {
                // Item que mudou de pasta fica de fora mesmo se marcado: importar criaria o
                // duplicado que a reconciliação existe para evitar. Ele sai pelo Reapontar.
                var selecionados = JogosEncontrados.Where(j => j.Selecionado && !j.JaExiste && !j.EhMudancaDePasta).ToList();
                if (selecionados.Count == 0)
                {
                    plugin.PlayniteApi.Dialogs.ShowMessage("Nenhum jogo novo selecionado para importar.", "Aviso");
                    return;
                }

                var idsImportados = plugin.ImportarLote(selecionados);

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
                
                using (plugin.PlayniteApi.Database.BufferedUpdate())
                {
                    foreach (var game in games)
                    {
                        if (plugin.ApplyDriveTag(game))
                        {
                            atualizados++;
                            plugin.PlayniteApi.Database.Games.Update(game);
                        }
                    }
                }


                plugin.PlayniteApi.Dialogs.ShowMessage(string.Format("{0} jogos atualizados com a característica do HD.", atualizados), "Sucesso");
            });

            ApplyLocalSourceToExistingCommand = new RelayCommand<object>((_) =>
            {
                int atualizados = 0;
                var games = plugin.PlayniteApi.Database.Games.Where(g => g.PluginId == plugin.Id).ToList();

                using (plugin.PlayniteApi.Database.BufferedUpdate())
                {
                    foreach (var game in games)
                    {
                        if (plugin.ApplyLocalSource(game))
                        {
                            atualizados++;
                            plugin.PlayniteApi.Database.Games.Update(game);
                        }
                    }
                }

                plugin.PlayniteApi.Dialogs.ShowMessage(string.Format("{0} jogo(s) marcado(s) com a Fonte \"Local\".", atualizados), "Sucesso");
            });

            CompletarBibliotecaCommand = new RelayCommand<object>((_) =>
            {
                plugin.CompletarBibliotecaDeEmulacao();
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
                // Jogo com candidato NUNCA entra aqui, mesmo selecionado: desinstalar o que a
                // extensão acabou de encontrar no disco é exatamente o erro que esta rodada corrige.
                var alvos = JogosParaRelinkar.Where(j => j.Selecionado && !j.TemCandidato).ToList();
                if (alvos.Count == 0)
                {
                    plugin.PlayniteApi.Dialogs.ShowMessage(
                        "Selecione um ou mais jogos sem pasta encontrada para marcar como desinstalados.\n\n" +
                        "Jogo que a busca localizou no disco não é marcado como desinstalado — use \"Relinkar\" nele.",
                        "Aviso");
                    return;
                }

                if (plugin.PlayniteApi.Dialogs.ShowMessage(
                        string.Format("Marcar {0} jogo(s) como desinstalados?\n\nSe a pasta onde eles estão hoje não é uma das Pastas Monitoradas, a busca não tinha como encontrá-los — cadastre a pasta e procure de novo antes.", alvos.Count),
                        "Confirmar", System.Windows.MessageBoxButton.YesNo) != System.Windows.MessageBoxResult.Yes)
                {
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

            // Procura, para cada jogo cuja instalação sumiu, a pasta que passou a ser a casa dele.
            //
            // A versão anterior desta busca tinha três furos, todos observados em biblioteca real:
            //   1. exigia que o jogo tivesse ação de arquivo — jogo com só InstallDirectory nunca
            //      era procurado;
            //   2. ao achar o executável, exigia que o nome da pasta nova fosse igual ao nome do
            //      jogo OU ao nome da pasta antiga; qualquer outro nome fazia o acerto ser
            //      DESCARTADO em silêncio, e o jogo voltava como "Não Encontrado" mesmo estando
            //      no disco. Mover e renomear a pasta é justamente o caso mais comum;
            //   3. o item "Não Encontrado" nascia MARCADO, então o falso negativo do item 2 virava
            //      desinstalação com um clique.
            // Agora a evidência é somada e mostrada (coluna "Por quê"), a varredura de disco
            // acontece uma vez só, e nada que tenha candidato nasce marcado para desinstalar.
            SearchLostGamesCommand = new RelayCommand<object>((_) =>
            {
                JogosParaRelinkar.Clear();

                plugin.PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
                {
                    progressArgs.Text = "Lendo as pastas monitoradas...";
                    var indice = plugin.IndexarPastasEmDisco(progressArgs.CancelToken);
                    if (progressArgs.CancelToken.IsCancellationRequested) return;

                    var games = plugin.PlayniteApi.Database.Games.ToList();

                    // Pasta que um jogo SAUDÁVEL da biblioteca já ocupa não pode ser oferecida como
                    // nova casa de outro: os dois passariam a apontar para a mesma instalação.
                    var ocupadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var g in games)
                    {
                        if (string.IsNullOrEmpty(g.InstallDirectory)) continue;
                        if (!Directory.Exists(g.InstallDirectory)) continue;
                        ocupadas.Add(plugin.NormalizePath(g.InstallDirectory));
                    }

                    progressArgs.Text = "Procurando os jogos que mudaram de pasta...";

                    foreach (var game in games)
                    {
                        if (progressArgs.CancelToken.IsCancellationRequested) break;

                        if (game.PluginId != Guid.Empty && game.PluginId != plugin.Id) continue;
                        // Jogo de emulação fica de fora: o "caminho" dele é a ROM, e várias ROMs
                        // dividem a mesma pasta — casar por lá apontaria o jogo errado.
                        if (game.GameActions != null &&
                            game.GameActions.Any(a => a.Type == Playnite.SDK.Models.GameActionType.Emulator)) continue;

                        var fileAction = game.GameActions == null
                            ? null
                            : game.GameActions.FirstOrDefault(a => a.Type == Playnite.SDK.Models.GameActionType.File);

                        // Sem ação de arquivo o jogo AINDA é procurável pela pasta — era o furo nº 1.
                        string exeAntigoCompleto = null;
                        if (fileAction != null && !string.IsNullOrEmpty(fileAction.Path))
                        {
                            exeAntigoCompleto = plugin.PlayniteApi.ExpandGameVariables(game, fileAction.Path);
                        }

                        bool pastaSumiu = !string.IsNullOrEmpty(game.InstallDirectory) &&
                                          !Directory.Exists(game.InstallDirectory);
                        bool exeSumiu = !string.IsNullOrEmpty(exeAntigoCompleto) && !File.Exists(exeAntigoCompleto);

                        // Nada sumiu: instalação saudável, não é assunto desta tela.
                        if (!pastaSumiu && !exeSumiu) continue;
                        // Sem pasta e sem executável não há por onde procurar.
                        if (string.IsNullOrEmpty(game.InstallDirectory) && string.IsNullOrEmpty(exeAntigoCompleto)) continue;

                        var relinkItem = new RelinkGameItem
                        {
                            GameId = game.Id,
                            Nome = game.Name,
                            CaminhoAntigoExe = string.IsNullOrEmpty(exeAntigoCompleto) ? game.InstallDirectory : exeAntigoCompleto,
                            PastaAntiga = game.InstallDirectory,
                            Status = "Procurando...",
                            Selecionado = false
                        };

                        plugin.PlayniteApi.MainView.UIDispatcher.Invoke(() => { JogosParaRelinkar.Add(relinkItem); });

                        var achado = plugin.ProcurarNovaCasa(game, exeAntigoCompleto, indice, ocupadas);

                        plugin.PlayniteApi.MainView.UIDispatcher.Invoke(() =>
                        {
                            if (achado != null)
                            {
                                relinkItem.NovoCaminhoExe = achado.ExeCandidato;
                                relinkItem.NovoInstallDirectory = achado.PastaCandidata;
                                relinkItem.Motivo = achado.Motivo;
                                relinkItem.Confianca = achado.Confianca;
                                relinkItem.Status = achado.Confiavel ? "Mudou de pasta" : "Provável (confira)";
                                // Só a evidência forte nasce marcada. Confiança "Média" é convite a
                                // olhar o motivo, não a apertar Relinkar no automático.
                                relinkItem.Selecionado = achado.Confiavel;
                            }
                            else
                            {
                                relinkItem.Status = "Não Encontrado";
                                relinkItem.Motivo = "nenhuma pasta monitorada tem evidência suficiente deste jogo";
                                relinkItem.Confianca = "";
                                // NUNCA marcado: marcar aqui é oferecer desinstalação em lote para
                                // o que pode ser só uma pasta monitorada que ficou de fora da lista.
                                relinkItem.Selecionado = false;
                            }
                        });
                    }
                }, new Playnite.SDK.GlobalProgressOptions("Procurando jogos que mudaram de pasta...", true));

                int mudaram = JogosParaRelinkar.Count(j => j.TemCandidato);
                int semNada = JogosParaRelinkar.Count(j => !j.TemCandidato);

                if (JogosParaRelinkar.Count == 0)
                {
                    plugin.PlayniteApi.Dialogs.ShowMessage(
                        "Nenhum jogo com pasta ou executável ausente. A biblioteca está inteira.",
                        "Reparar Desinstalados");
                    return;
                }

                plugin.PlayniteApi.Dialogs.ShowMessage(
                    string.Format(
                        "{0} jogo(s) mudaram de pasta e podem ser reapontados; {1} não foram encontrados em nenhuma pasta monitorada.\n\n" +
                        "Antes de marcar como desinstalado, confira se a pasta onde o jogo está hoje é uma das Pastas Monitoradas — se não for, ele não tem como ser encontrado.",
                        mudaram, semNada),
                    "Resultado da busca");
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
                using (plugin.PlayniteApi.Database.BufferedUpdate())
                {
                    foreach (var relink in selecionados)
                    {
                        // Um método só para reapontar, usado também pela aba de busca: o jogo que
                        // volta precisa sair do histórico de desinstalações nos dois caminhos, e
                        // duas cópias da mesma gravação divergiriam na primeira mudança.
                        if (!plugin.RelocarJogo(relink.GameId, relink.NovoInstallDirectory, relink.NovoCaminhoExe)) continue;

                        relink.Selecionado = false;
                        relink.Status = "Reapontado";
                        atualizados++;
                    }
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
            PastaTotais.Limpar();

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
                    PastaTotais.Registrar(pasta, subpastas.Length);

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

            PastasResumoTexto = string.Format(
                "Total: {0} jogo(s) na biblioteca · {1} pasta(s) fora · {2} com pasta ausente · {3} ignorado(s), em {4} pasta(s) monitorada(s).",
                PastasResumo.Sum(p => p.NaBiblioteca),
                PastasResumo.Sum(p => p.NaoImportados),
                PastasResumo.Sum(p => p.ComProblema),
                PastasResumo.Sum(p => p.Ignorados),
                Settings.Pastas.Count);

            AtualizarIndicadores();
        }

        // ---------------------------------------------------------------------
        // Indicadores da tela Início
        // ---------------------------------------------------------------------
        // A tela antiga só respondia "como eu configuro isso?". Para saber se havia algo errado
        // era preciso abrir aba por aba e apertar o botão de busca de cada uma. Estes números
        // saem do resumo que já era calculado — nenhuma varredura de disco a mais.

        private int totalNaBiblioteca;
        public int TotalNaBiblioteca { get { return totalNaBiblioteca; } set { SetValue(ref totalNaBiblioteca, value); } }

        private int totalForaDaBiblioteca;
        public int TotalForaDaBiblioteca { get { return totalForaDaBiblioteca; } set { SetValue(ref totalForaDaBiblioteca, value); } }

        private int totalComPastaAusente;
        public int TotalComPastaAusente { get { return totalComPastaAusente; } set { SetValue(ref totalComPastaAusente, value); } }

        private int totalPastasMonitoradas;
        public int TotalPastasMonitoradas { get { return totalPastasMonitoradas; } set { SetValue(ref totalPastasMonitoradas, value); } }

        private int totalPastasInacessiveis;
        public int TotalPastasInacessiveis { get { return totalPastasInacessiveis; } set { SetValue(ref totalPastasInacessiveis, value); } }

        private string diagnosticoTexto = "";
        public string DiagnosticoTexto { get { return diagnosticoTexto; } set { SetValue(ref diagnosticoTexto, value); } }

        // Quantos itens da última busca são pasta que mudou de lugar. É o que decide se o aviso
        // de "reapontar" aparece na aba de busca.
        private int totalMudaramDePasta;
        public int TotalMudaramDePasta
        {
            get { return totalMudaramDePasta; }
            set { SetValue(ref totalMudaramDePasta, value); OnPropertyChanged("MudaramDePastaTitulo"); }
        }

        public string MudaramDePastaTitulo
        {
            get
            {
                return TotalMudaramDePasta == 1
                    ? "1 jogo mudou de pasta"
                    : string.Format("{0} jogos mudaram de pasta", TotalMudaramDePasta);
            }
        }

        public void AtualizarContagemDeMovidos()
        {
            TotalMudaramDePasta = JogosEncontrados.Count(j => j.EhMudancaDePasta);
        }

        private void AtualizarIndicadores()
        {
            TotalNaBiblioteca = PastasResumo.Sum(p => p.NaBiblioteca);
            TotalForaDaBiblioteca = PastasResumo.Sum(p => p.NaoImportados);
            TotalComPastaAusente = PastasResumo.Sum(p => p.ComProblema);
            TotalPastasMonitoradas = Settings.Pastas == null ? 0 : Settings.Pastas.Count;
            TotalPastasInacessiveis = PastasResumo.Count(p => !p.ExisteEmDisco);

            // O texto diz o que fazer, não só o que existe. "3 jogos com pasta ausente" sem o
            // próximo passo é o que fazia a pessoa ir direto para "marcar como desinstalado".
            if (TotalPastasMonitoradas == 0)
            {
                DiagnosticoTexto = "Nenhuma pasta monitorada ainda. Cadastre em \"Pastas monitoradas\" a pasta onde seus jogos ficam — é dela que sai tudo o mais.";
            }
            else if (TotalPastasInacessiveis > 0)
            {
                DiagnosticoTexto = string.Format("{0} pasta(s) monitorada(s) não estão acessíveis agora. Se for um HD externo desligado, ligue antes de qualquer busca: sem ele, os jogos de lá parecem ter sumido.", TotalPastasInacessiveis);
            }
            else if (TotalComPastaAusente > 0)
            {
                DiagnosticoTexto = string.Format("{0} jogo(s) estão com a pasta ausente. Abra \"Jogos que sumiram\" e procure: pasta que só mudou de lugar é reapontada sem perder tempo de jogo nem capa.", TotalComPastaAusente);
            }
            else if (TotalForaDaBiblioteca > 0)
            {
                DiagnosticoTexto = string.Format("{0} pasta(s) do disco ainda não viraram jogo na biblioteca. Elas estão em \"Busca e importação\".", TotalForaDaBiblioteca);
            }
            else
            {
                DiagnosticoTexto = "Tudo o que está nas pastas monitoradas já está na biblioteca, e nenhuma instalação está faltando.";
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
