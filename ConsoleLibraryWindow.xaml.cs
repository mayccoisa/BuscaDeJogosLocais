using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BuscaDeJogosLocais
{
    // Prévia de "Completar Biblioteca": mostra, jogo a jogo de emulação, qual console vai virar a
    // Fonte, de onde ele foi lido e o que muda. Permite editar e desmarcar linha a linha;
    // só grava no clique em Aplicar.
    public partial class ConsoleLibraryWindow : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void Notify(string name)
        {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name));
        }

        private Action<List<ConsoleLibraryItem>> onAplicar;

        public ObservableCollection<ConsoleLibraryItem> Itens { get; private set; }

        public string HeaderText
        {
            get
            {
                int semConsole = Itens.Count(i => string.IsNullOrEmpty(i.BibliotecaNova));
                if (semConsole == 0)
                    return string.Format("{0} jogo(s) de emulação, todos com console identificado.", Itens.Count);

                return string.Format("{0} jogo(s) de emulação · {1} sem console identificado (preencha à mão se quiser incluir).",
                    Itens.Count, semConsole);
            }
        }

        // Quantos jogos ficam em cada console depois de aplicar — é a visão que a tela de perfil
        // do tema vai mostrar.
        public string ResumoTexto
        {
            get
            {
                var grupos = Itens
                    .Where(i => !string.IsNullOrEmpty(i.BibliotecaNova))
                    .GroupBy(i => i.BibliotecaNova.Trim())
                    .OrderByDescending(g => g.Count())
                    .Select(g => string.Format("{0}: {1}", g.Key, g.Count()))
                    .ToList();

                if (grupos.Count == 0) return "Nenhum console identificado.";
                return "Por console  ·  " + string.Join("   ·   ", grupos.ToArray());
            }
        }

        public string ContagemTexto
        {
            get { return string.Format("{0} de {1} selecionado(s)", Itens.Count(i => i.Selecionado), Itens.Count); }
        }

        public ConsoleLibraryWindow(ObservableCollection<ConsoleLibraryItem> itens, Action<List<ConsoleLibraryItem>> aplicar)
        {
            InitializeComponent();
            this.Itens = itens;
            this.onAplicar = aplicar;
            this.DataContext = this;
        }

        private void OnMarcarTodosClick(object sender, RoutedEventArgs e)
        {
            foreach (var i in Itens)
            {
                if (string.IsNullOrEmpty(i.BibliotecaNova)) continue;
                i.Selecionado = true;
            }
            Notify("ContagemTexto");
        }

        private void OnDesmarcarTodosClick(object sender, RoutedEventArgs e)
        {
            foreach (var i in Itens) i.Selecionado = false;
            Notify("ContagemTexto");
        }

        private void OnAplicarClick(object sender, RoutedEventArgs e)
        {
            GridBibliotecas.CommitEdit(DataGridEditingUnit.Row, true);

            var selecionados = Itens.Where(i => i.Selecionado && !string.IsNullOrEmpty(i.BibliotecaNova)).ToList();
            if (selecionados.Count == 0)
            {
                MessageBox.Show("Nenhum jogo selecionado com console preenchido.", "Aviso",
                    MessageBoxButton.OK, MessageBoxImage.Information);
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
