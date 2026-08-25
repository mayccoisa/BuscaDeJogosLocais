using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace BuscaDeJogosLocais.Ui
{
    /// <summary>Caixa marcada vira a palavra que aparece na coluna da direita dos cards.</summary>
    public class BoolToOnOffConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return IsTrue(value) ? "Ativado" : "Desativado";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        internal static bool IsTrue(object value)
        {
            return value is bool && (bool)value;
        }
    }

    /// <summary>
    /// A cor do valor: verde quando ligado, cinza quando desligado.
    ///
    /// As cores saem do próprio dicionário, e não de constantes aqui, para não existirem dois
    /// verdes que precisem ser mudados juntos.
    /// </summary>
    public class BoolToStatusBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return UiKit.Brush(BoolToOnOffConverter.IsTrue(value) ? "GreenBrush" : "TextMutedBrush");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>Texto vazio mostra o elemento. É o que faz o aviso de "não vinculado" aparecer.</summary>
    public class EmptyStringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var text = value as string;
            return string.IsNullOrWhiteSpace(text) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>O oposto: só mostra quando há texto. Serve para linha de erro.</summary>
    public class FilledStringToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var text = value as string;
            return string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Data e hora local, ou um travessão quando nunca aconteceu.
    ///
    /// Travessão e não "0" nem data vazia: ausência de registro não é o mesmo que registro zerado,
    /// e é a diferença entre "nunca enviei" e "enviei e não veio nada".
    /// </summary>
    public class NullableDateToTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is DateTime)
            {
                return ((DateTime)value).ToLocalTime().ToString("dd/MM/yyyy HH:mm");
            }
            return "—";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>Texto, ou um travessão quando vazio. Campo em branco na coluna da direita ficaria
    /// indistinguível de valor que não carregou.</summary>
    public class TextOrDashConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var text = value as string;
            return string.IsNullOrWhiteSpace(text) ? "—" : text;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>Visibilidade por booleano. Passe "inverse" no parâmetro para trocar o sentido.</summary>
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var show = BoolToOnOffConverter.IsTrue(value);
            if (string.Equals(parameter as string, "inverse", StringComparison.OrdinalIgnoreCase))
            {
                show = !show;
            }
            return show ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Cor do rótulo de situação de uma linha. A cor aqui carrega significado — verde é
    /// "resolvido sozinho", âmbar é "olhe antes de aplicar", vermelho é "some se você não agir" —,
    /// e por isso é a única exceção à regra de nunca fixar cor de texto.
    /// </summary>
    public class StatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string s = value == null ? string.Empty : value.ToString();

            if (s.IndexOf("Mudou de pasta", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("Executável mudou", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("Reapontado", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("Tudo importado", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("Sem save", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("Já na Biblioteca", StringComparison.OrdinalIgnoreCase) >= 0)
                return UiKit.Brush("GreenBrush");

            if (s.IndexOf("Provável", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("Parcialmente", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("Pendente", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("Tem save", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("Procurando", StringComparison.OrdinalIgnoreCase) >= 0)
                return UiKit.Brush("GoldBrush");

            if (s.IndexOf("Não Encontrado", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("Ausente", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("inacessível", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("Nada importado", StringComparison.OrdinalIgnoreCase) >= 0 ||
                s.IndexOf("Desinstalado", StringComparison.OrdinalIgnoreCase) >= 0)
                return UiKit.Brush("DangerBrush");

            return UiKit.Brush("TextSecondaryBrush");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>Número maior que zero deixa o bloco visível. Usado nos avisos da tela Início.</summary>
    public class CountToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int n = 0;
            if (value != null) int.TryParse(value.ToString(), out n);
            bool mostrar = n > 0;
            if (string.Equals(parameter as string, "inverse", StringComparison.OrdinalIgnoreCase)) mostrar = !mostrar;
            return mostrar ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>
    /// Contagem de problemas: zero fica na cor normal, qualquer número acima disso fica vermelho.
    /// Pintar de vermelho o tempo todo faria o indicador virar decoração e parar de avisar.
    /// </summary>
    public class CountToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int n = 0;
            if (value != null) int.TryParse(value.ToString(), out n);
            return n > 0 ? UiKit.Brush("DangerBrush") : UiKit.Brush("TextPrimaryBrush");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    /// <summary>Booleano invertido, para desabilitar um controle quando a condição é verdadeira.</summary>
    public class NegateBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !BoolToOnOffConverter.IsTrue(value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return !BoolToOnOffConverter.IsTrue(value);
        }
    }
}
