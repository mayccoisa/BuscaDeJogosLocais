using System;
using System.Windows;
using System.Windows.Media;

namespace BuscaDeJogosLocais.Ui
{
    /// <summary>
    /// Acesso ao dicionário de estilos a partir de código.
    ///
    /// As telas em XAML mergeiam o Theme.xaml sozinhas, mas os conversores que devolvem cor de
    /// status precisam ler os mesmos pincéis de lá. Sem este atalho eles repetiriam os
    /// hexadecimais à mão — que é exatamente como as cores fixas espalhadas apareceram na
    /// versão anterior desta tela, e o motivo de mudar o visual ter virado caçada por literal.
    /// </summary>
    public static class UiKit
    {
        /// <summary>
        /// URI absoluta de propósito. O Playnite carrega o plugin como assembly solto, sem
        /// aplicação WPF dona do recurso, e aí um caminho relativo não resolve.
        /// </summary>
        public const string ThemeUri = "pack://application:,,,/BuscaDeJogosLocais;component/Ui/Theme.xaml";

        private static ResourceDictionary cached;

        public static ResourceDictionary Theme
        {
            get
            {
                if (cached == null)
                {
                    cached = new ResourceDictionary { Source = new Uri(ThemeUri, UriKind.Absolute) };
                }
                return cached;
            }
        }

        /// <summary>Mergeia o dicionário num elemento montado em código.</summary>
        public static T Themed<T>(T element) where T : FrameworkElement
        {
            if (!element.Resources.MergedDictionaries.Contains(Theme))
            {
                element.Resources.MergedDictionaries.Add(Theme);
            }
            return element;
        }

        public static Brush Brush(string key)
        {
            var found = Theme[key] as Brush;
            return found ?? Brushes.Transparent;
        }

        public static Color Color(string key)
        {
            var found = Theme[key];
            if (found is Color) return (Color)found;
            var brush = found as SolidColorBrush;
            return brush != null ? brush.Color : Colors.Transparent;
        }

        public static Style Style(string key)
        {
            return Theme[key] as Style;
        }
    }
}
