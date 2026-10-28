using System.Globalization;
using System.Windows.Data;

namespace JobTracker.Presentation;

public sealed class NavigationSelectionConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is string page && values[1] is string destination &&
        (page == destination || page == "Detalhes" && destination == "Vagas");
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        targetTypes.Select(_ => Binding.DoNothing).ToArray();
}
