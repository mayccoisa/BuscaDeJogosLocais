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
        private Action<List<IntegrityResult>> onRemove;
        private Action<List<IntegrityResult>> onMarkUninstalled;
        private Action onClose;

        public IntegrityResultView(ObservableCollection<IntegrityResult> results,
            Action<List<IntegrityResult>> onRemove,
            Action<List<IntegrityResult>> onMarkUninstalled,
            Action onClose)
        {
            InitializeComponent();
            this.Results = results;
            this.onRemove = onRemove;
            this.onMarkUninstalled = onMarkUninstalled;
            this.onClose = onClose;
            this.DataContext = this;
        }

        private List<IntegrityResult> Selecionados()
        {
            return Results.Where(r => r.Selected).ToList();
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (onRemove != null) onRemove.Invoke(Selecionados());
        }

        private void MarkUninstalled_Click(object sender, RoutedEventArgs e)
        {
            if (onMarkUninstalled != null) onMarkUninstalled.Invoke(Selecionados());
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (onClose != null) onClose.Invoke();
        }
    }
}
