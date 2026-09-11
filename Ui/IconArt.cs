using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BuscaDeJogosLocais.Ui
{
    /// <summary>
    /// O desenho da extensao: uma pasta com um controle dentro.
    ///
    /// Ele mora aqui, e nao solto em cada tela, porque o MESMO desenho e usado em dois lugares
    /// que nao se falam: o icone vetorial da barra lateral (pintado com a cor do tema em uso) e o
    /// PNG do icone da biblioteca (arquivo estatico, gerado por fora). Duas copias do caminho
    /// divergiriam na primeira vez que alguem ajustasse uma delas, e a diferenca so apareceria
    /// com os dois na mesma tela.
    ///
    /// A pasta e CONTORNO e o controle e PREENCHIDO de proposito. Desenhar os dois preenchidos,
    /// um por cima do outro, faz uma mancha unica no tamanho da barra lateral (20 px); com o
    /// contorno vazado, o controle continua se lendo como um objeto separado.
    /// </summary>
    public static class IconArt
    {
        /// <summary>A pasta, em contorno. Caixa de 24x24, igual a do controle.</summary>
        public const string FolderPath =
            "M2.6,18.4 L2.6,6.3 " +
            "C2.6,5.4 3.3,4.7 4.2,4.7 L9.1,4.7 L11.1,7.1 L19.8,7.1 " +
            "C20.7,7.1 21.4,7.8 21.4,8.7 L21.4,18.4 " +
            "C21.4,19.3 20.7,20.0 19.8,20.0 L4.2,20.0 " +
            "C3.3,20.0 2.6,19.3 2.6,18.4 Z";

        /// <summary>
        /// O controle, preenchido, com o direcional e os dois botoes VAZADOS pelo EvenOdd (o "F0").
        /// Desenhar os furos por cima exigiria saber a cor do fundo, que muda com o tema — e na
        /// barra lateral o fundo nem e o mesmo da tela de configuracoes.
        /// </summary>
        public const string PadPath =
            "F0 " +
            "M7.6,10.6 C5.8,10.6 4.7,12.2 4.7,14.3 C4.7,16.4 5.6,17.7 7.0,17.7 " +
            "C8.0,17.7 8.6,17.0 9.2,16.2 L14.8,16.2 " +
            "C15.4,17.0 16.0,17.7 17.0,17.7 C18.4,17.7 19.3,16.4 19.3,14.3 " +
            "C19.3,12.2 18.2,10.6 16.4,10.6 Z " +
            // direcional
            "M7.7,12.1 L8.7,12.1 L8.7,13.1 L9.7,13.1 L9.7,14.1 L8.7,14.1 L8.7,15.1 " +
            "L7.7,15.1 L7.7,14.1 L6.7,14.1 L6.7,13.1 L7.7,13.1 Z " +
            // botoes
            "M14.0,13.2 A0.9,0.9 0 1 1 15.8,13.2 A0.9,0.9 0 1 1 14.0,13.2 Z " +
            "M15.6,15.0 A0.9,0.9 0 1 1 17.4,15.0 A0.9,0.9 0 1 1 15.6,15.0 Z";

        /// <summary>
        /// Monta o desenho pronto para entrar numa barra lateral ou num cabecalho.
        ///
        /// As duas cores chegam de fora porque quem chama sabe o contexto: a barra lateral pede a
        /// cor de texto do tema em uso, e o gerador do PNG pede cores fixas, ja que arquivo
        /// estatico nao tem como acompanhar tema.
        /// </summary>
        public static FrameworkElement Build(Brush folderBrush, Brush padBrush, double size)
        {
            var pasta = new Path();
            pasta.Data = Geometry.Parse(FolderPath);
            pasta.Stroke = folderBrush;
            pasta.StrokeThickness = 1.5;
            pasta.StrokeLineJoin = PenLineJoin.Round;
            pasta.Fill = Brushes.Transparent;

            var controle = new Path();
            controle.Data = Geometry.Parse(PadPath);
            controle.Fill = padBrush;

            // Os dois caminhos usam a MESMA caixa de 24x24, entao precisam de um Canvas (que nao
            // reposiciona nada) e de um Viewbox unico por fora. Um Viewbox para cada esticaria
            // cada desenho ate a propria caixa e o controle sairia do tamanho da pasta.
            var tela = new Canvas();
            tela.Width = 24;
            tela.Height = 24;
            tela.Children.Add(pasta);
            tela.Children.Add(controle);

            var caixa = new Viewbox();
            caixa.Child = tela;
            caixa.Width = size;
            caixa.Height = size;
            caixa.Stretch = Stretch.Uniform;
            caixa.HorizontalAlignment = HorizontalAlignment.Center;
            caixa.VerticalAlignment = VerticalAlignment.Center;
            return caixa;
        }
    }
}
