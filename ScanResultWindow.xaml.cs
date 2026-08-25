using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace BuscaDeJogosLocais
{
    public partial class ScanResultWindow : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void Notify(string name)
        {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name));
        }

        private ObservableCollection<ScannedGame> resultados;
        private Action<List<ScannedGame>> onImportar;
        private Action<ScannedGame> onIgnorar;
        private Action<List<ScannedGame>> onReapontar;

        public ICollectionView ResultadosView { get; private set; }

        // Quantas pastas desta lista são, na verdade, jogos que a biblioteca já tem e que
        // mudaram de lugar. Enquanto isto for zero, o aviso e o botão de reapontar não existem.
        public int QuantosMudaramDePasta
        {
            get { return resultados.Count(g => g.EhMudancaDePasta); }
        }

        public string TituloMudaramDePasta
        {
            get
            {
                return QuantosMudaramDePasta == 1
                    ? "1 jogo mudou de pasta"
                    : string.Format("{0} jogos mudaram de pasta", QuantosMudaramDePasta);
            }
        }

        private bool ocultarJaExistentes = true;
        public bool OcultarJaExistentes
        {
            get { return ocultarJaExistentes; }
            set { ocultarJaExistentes = value; Notify("OcultarJaExistentes"); ResultadosView.Refresh(); Notify("ContagemTexto"); }
        }

        public string HeaderText
        {
            get { return string.Format("{0} pasta(s) encontrada(s)", resultados.Count); }
        }

        public string ContagemTexto
        {
            get
            {
                // Pasta que mudou de lugar não é "novo": contá-la como novidade é o que fazia a
                // pessoa importar e acabar com o mesmo jogo duas vezes na biblioteca.
                int movidos = QuantosMudaramDePasta;
                int novos = resultados.Count(g => !g.JaExiste && !g.EhMudancaDePasta);
                int existentes = resultados.Count(g => g.JaExiste);
                return string.Format("{0} novo(s) · {1} mudaram de pasta · {2} já na biblioteca", novos, movidos, existentes);
            }
        }

        public ScanResultWindow(ObservableCollection<ScannedGame> resultados,
            Action<List<ScannedGame>> importar,
            Action<ScannedGame> ignorar,
            Action<List<ScannedGame>> reapontar)
        {
            InitializeComponent();
            this.resultados = resultados;
            this.onImportar = importar;
            this.onIgnorar = ignorar;
            this.onReapontar = reapontar;
            this.DataContext = this;

            ResultadosView = CollectionViewSource.GetDefaultView(resultados);
            ResultadosView.Filter = FiltrarResultados;
            ResultadosView.GroupDescriptions.Add(new PropertyGroupDescription("PastaMonitoradaPai"));
            ResultadosView.SortDescriptions.Add(new SortDescription("PastaMonitoradaPai", ListSortDirection.Ascending));
            ResultadosView.SortDescriptions.Add(new SortDescription("Nome", ListSortDirection.Ascending));
        }

        private bool FiltrarResultados(object item)
        {
            var jogo = (ScannedGame)item;
            if (OcultarJaExistentes && jogo.JaExiste) return false;
            return true;
        }

        private void OnIgnorarClick(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            var jogo = btn != null ? btn.Tag as ScannedGame : null;
            if (jogo == null) return;

            if (onIgnorar != null) onIgnorar(jogo);
            resultados.Remove(jogo);
            Notify("HeaderText");
            Notify("ContagemTexto");
        }

        private void OnReapontarClick(object sender, RoutedEventArgs e)
        {
            var movidos = resultados.Where(j => j.EhMudancaDePasta).ToList();
            if (movidos.Count == 0 || onReapontar == null) return;

            onReapontar(movidos);

            ResultadosView.Refresh();
            Notify("QuantosMudaramDePasta");
            Notify("TituloMudaramDePasta");
            Notify("ContagemTexto");
        }

        private void OnImportarClick(object sender, RoutedEventArgs e)
        {
            // Pasta que mudou de lugar fica de fora mesmo se marcada: ela sai pelo Reapontar.
            var selecionados = resultados.Where(j => j.Selecionado && !j.JaExiste && !j.EhMudancaDePasta).ToList();
            if (selecionados.Count == 0)
            {
                MessageBox.Show("Nenhum jogo novo selecionado para importar.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (onImportar != null) onImportar(selecionados);

            ResultadosView.Refresh();
            Notify("ContagemTexto");

            MessageBox.Show(string.Format("{0} jogo(s) importado(s) com sucesso!", selecionados.Count), "Sucesso", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnFecharClick(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this);
            if (window != null) window.Close();
        }
    }
}
