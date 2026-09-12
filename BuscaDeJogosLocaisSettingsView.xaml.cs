using System.Windows.Controls;

namespace BuscaDeJogosLocais
{
    public partial class BuscaDeJogosLocaisSettingsView : UserControl
    {
        // Aberta pela JANELA de configurações do Playnite. Ali quem atribui o DataContext é o
        // próprio app, a partir do que GetSettings devolveu — por isso este construtor não mexe
        // nele.
        public BuscaDeJogosLocaisSettingsView()
        {
            InitializeComponent();
        }

        // Aberta pela BARRA LATERAL, onde não passa ninguém para atribuir o DataContext. Tela sem
        // DataContext não dá erro: ela abre com todo campo vazio e todo botão morto, que é o pior
        // jeito de falhar — parece que a extensão perdeu os dados.
        public BuscaDeJogosLocaisSettingsView(BuscaDeJogosLocaisSettingsViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            // A janela de configurações chama BeginEdit ao abrir e EndEdit no OK; pela barra
            // lateral ninguém chama nada. Sem isto a tela abria sem a tabela de pastas e sem os
            // jogos da biblioteca, e o que se mudava nela nunca era gravado.
            viewModel.AbrirPelaBarraLateral();
        }
    }
}