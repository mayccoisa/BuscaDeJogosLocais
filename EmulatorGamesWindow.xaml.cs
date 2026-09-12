using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace BuscaDeJogosLocais
{
    // Lista o que há nas pastas de varredura de um emulador e separa o que já virou jogo no
    // Playnite do que ficou de fora — a mesma pergunta que a FolderGamesWindow responde para as
    // pastas monitoradas de PC.
    public partial class EmulatorGamesWindow : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void Notify(string name)
        {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name));
        }

        private readonly EmuladorResumo resumo;
        private readonly BuscaDeJogosLocais plugin;

        public ICollectionView ItensView { get; private set; }

        public object Icone { get { return resumo.Icone; } }
        public bool TemIcone { get { return resumo.Icone != null; } }

        public bool PodeAbrirPasta
        {
            get { return plugin != null && !string.IsNullOrWhiteSpace(resumo.PrimeiraPasta); }
        }

        private bool somenteForaDaBiblioteca;
        public bool SomenteForaDaBiblioteca
        {
            get { return somenteForaDaBiblioteca; }
            set { somenteForaDaBiblioteca = value; Notify("SomenteForaDaBiblioteca"); ItensView.Refresh(); }
        }

        public string HeaderText
        {
            get { return resumo.Nome; }
        }

        public string ResumoText
        {
            get
            {
                return string.Format(
                    "Versão {0} · {1} na biblioteca · {2} fora dela · {3}",
                    resumo.Versao, resumo.NaBiblioteca, resumo.ForaDaBiblioteca, resumo.Plataformas);
            }
        }

        public string PastasText
        {
            get
            {
                if (resumo.TotalPastas == 0)
                {
                    // Sem pasta de varredura a tabela vem vazia, e vazia sem explicação parece
                    // defeito. O lugar de configurar é do Playnite, não desta extensão.
                    return "Este emulador não tem pasta de varredura configurada no Playnite " +
                           "(Biblioteca › Configurar emuladores › Pastas de varredura automática), " +
                           "então não há como saber o que dele está ou não na biblioteca.";
                }

                var texto = "Pastas varridas: " + resumo.Pastas;
                if (!resumo.ExecutavelExiste)
                    texto += "\nExecutável do emulador não encontrado" +
                             (string.IsNullOrEmpty(resumo.InstallDir) ? " (pasta de instalação vazia no Playnite)." : " em " + resumo.InstallDir + ".") +
                             " O playnite.log tem uma linha [Emulador] com o padrão tentado — botão \"Abrir pasta dos logs\" na aba Emuladores.";
                if (!string.IsNullOrEmpty(resumo.Observacao)) texto += "\n" + resumo.Observacao;
                return texto;
            }
        }

        public EmulatorGamesWindow(EmuladorResumo resumo, BuscaDeJogosLocais plugin)
        {
            InitializeComponent();
            this.resumo = resumo;
            this.plugin = plugin;
            this.DataContext = this;

            var itens = new ObservableCollection<EmuladorJogoItem>(
                resumo.Itens.OrderBy(i => i.Status).ThenBy(i => i.Nome));

            ItensView = CollectionViewSource.GetDefaultView(itens);
            ItensView.Filter = Filtrar;
        }

        private bool Filtrar(object item)
        {
            if (!SomenteForaDaBiblioteca) return true;
            var jogo = (EmuladorJogoItem)item;
            return jogo.Status != "Na biblioteca";
        }

        private void OnAbrirPastaClick(object sender, RoutedEventArgs e)
        {
            if (plugin != null) plugin.AbrirPastaNoExplorador(resumo.PrimeiraPasta);
        }

        private void OnFecharClick(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this);
            if (window != null) window.Close();
        }
    }
}
