using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace BuscaDeJogosLocais
{
    // Lista os jogos vinculados a uma pasta monitorada e, junto, o que está na pasta
    // mas ficou de fora da biblioteca — é a resposta para "essa pasta está toda puxada?".
    public partial class FolderGamesWindow : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void Notify(string name)
        {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name));
        }

        private readonly PastaResumo resumo;

        public ICollectionView ItensView { get; private set; }

        private bool somenteForaDaBiblioteca;
        public bool SomenteForaDaBiblioteca
        {
            get { return somenteForaDaBiblioteca; }
            set { somenteForaDaBiblioteca = value; Notify("SomenteForaDaBiblioteca"); ItensView.Refresh(); }
        }

        public string HeaderText
        {
            get { return resumo.Caminho; }
        }

        public string ResumoText
        {
            get
            {
                return string.Format(
                    "{0} na biblioteca · {1} não importado(s) · {2} com pasta ausente · {3} ignorado(s)",
                    resumo.NaBiblioteca, resumo.NaoImportados, resumo.ComProblema, resumo.Ignorados);
            }
        }

        public FolderGamesWindow(PastaResumo resumo)
        {
            InitializeComponent();
            this.resumo = resumo;
            this.DataContext = this;

            var itens = new ObservableCollection<PastaJogoItem>(
                resumo.Itens.OrderBy(i => i.Status).ThenBy(i => i.Nome));

            ItensView = CollectionViewSource.GetDefaultView(itens);
            ItensView.Filter = Filtrar;
        }

        private bool Filtrar(object item)
        {
            if (!SomenteForaDaBiblioteca) return true;
            var jogo = (PastaJogoItem)item;
            return jogo.Status != "Na biblioteca";
        }

        private void OnFecharClick(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this);
            if (window != null) window.Close();
        }
    }
}
