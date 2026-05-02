using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BuscaDeJogosLocais
{
    public partial class IntegrityResultView : UserControl
    {
        public ObservableCollection<IntegrityResult> Results { get; set; }
        private Action<List<IntegrityResult>> onConfirm;

        public IntegrityResultView(ObservableCollection<IntegrityResult> results, Action<List<IntegrityResult>> onConfirm)
        {
            InitializeComponent();
            this.Results = results;
            this.onConfirm = onConfirm;
            this.DataContext = this;
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            var selected = Results.Where(r => r.Selected).ToList();
            if (onConfirm != null) onConfirm.Invoke(selected);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (onConfirm != null) onConfirm.Invoke(new List<IntegrityResult>());
        }
    }
}
