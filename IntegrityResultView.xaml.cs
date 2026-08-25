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
        private Action<List<IntegrityResult>> onRelocate;
        private Action onClose;

        public IntegrityResultView(ObservableCollection<IntegrityResult> results,
            Action<List<IntegrityResult>> onRemove,
            Action<List<IntegrityResult>> onMarkUninstalled,
            Action<List<IntegrityResult>> onRelocate,
            Action onClose)
        {
            InitializeComponent();
            this.Results = results;
            this.onRemove = onRemove;
            this.onMarkUninstalled = onMarkUninstalled;
            this.onRelocate = onRelocate;
            this.onClose = onClose;
            this.DataContext = this;
        }

        // Quantos itens desta lista apenas mudaram de pasta. É o número que decide se o texto do
        // topo fala de jogo perdido ou de jogo que só mudou de endereço.
        public int QuantosMudaramDePasta
        {
            get { return Results == null ? 0 : Results.Count(r => r.TemNovaCasa); }
        }

        public string ResumoDoDiagnostico
        {
            get
            {
                int movidos = QuantosMudaramDePasta;
                int total = Results == null ? 0 : Results.Count;
                int sumiram = total - movidos;

                if (movidos > 0 && sumiram == 0)
                    return string.Format("{0} jogo(s) apenas mudaram de pasta. Reaponte-os para a pasta atual — o registro, o tempo de jogo e as capas continuam os mesmos.", movidos);
                if (movidos > 0)
                    return string.Format("{0} jogo(s) mudaram de pasta e podem ser reapontados; {1} não foram encontrados em nenhuma pasta monitorada.", movidos, sumiram);
                return string.Format("{0} jogo(s) com pasta ou executável ausente, e nenhum foi encontrado nas pastas monitoradas. Confira se a pasta onde eles estão hoje está cadastrada antes de remover.", sumiram);
            }
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

        private void Relocate_Click(object sender, RoutedEventArgs e)
        {
            if (onRelocate != null) onRelocate.Invoke(Selecionados());
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (onClose != null) onClose.Invoke();
        }
    }
}
