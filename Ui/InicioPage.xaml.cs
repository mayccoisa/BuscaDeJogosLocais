using System.Windows.Controls;

namespace BuscaDeJogosLocais.Ui
{
    // Página sem lógica: o DataContext é o mesmo view model da tela de configurações, herdado
    // do shell. Manter o code-behind vazio é proposital — regra de tela que vive aqui não é
    // testável e não aparece para quem lê o view model.
    public partial class InicioPage : UserControl
    {
        public InicioPage()
        {
            InitializeComponent();
        }
    }
}
