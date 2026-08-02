using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BuscaDeJogosLocais
{
    // Prévia da limpeza de nomes dos jogos JÁ importados: mostra "de -> para",
    // permite editar e desmarcar item a item, e só grava no clique em Aplicar.
    public partial class RenamePreviewWindow : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void Notify(string name)
        {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name));
        }

        private Action<List<RenameItem>> onAplicar;

        public ObservableCollection<RenameItem> Itens { get; private set; }

        public string HeaderText
        {
            get { return string.Format("{0} jogo(s) local(is) com nome a ajustar.", Itens.Count); }
        }

        public string ContagemTexto
        {
            get { return string.Format("{0} de {1} selecionado(s)", Itens.Count(i => i.Selecionado), Itens.Count); }
        }

        public RenamePreviewWindow(ObservableCollection<RenameItem> itens, Action<List<RenameItem>> aplicar)
        {
            InitializeComponent();
            this.Itens = itens;
            this.onAplicar = aplicar;
            this.DataContext = this;
        }

        private void OnMarcarTodosClick(object sender, RoutedEventArgs e)
        {
            foreach (var i in Itens) i.Selecionado = true;
            Notify("ContagemTexto");
        }

        private void OnDesmarcarTodosClick(object sender, RoutedEventArgs e)
        {
            foreach (var i in Itens) i.Selecionado = false;
            Notify("ContagemTexto");
        }

        private void OnAplicarClick(object sender, RoutedEventArgs e)
        {
            GridRenomeacoes.CommitEdit(DataGridEditingUnit.Row, true);

            var selecionados = Itens.Where(i => i.Selecionado).ToList();
            if (selecionados.Count == 0)
            {
                MessageBox.Show("Nenhum jogo selecionado.", "Aviso", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (onAplicar != null) onAplicar(selecionados);

            var window = Window.GetWindow(this);
            if (window != null) window.Close();
        }

        private void OnFecharClick(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this);
            if (window != null) window.Close();
        }
    }
}
