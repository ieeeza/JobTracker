using System.Globalization;
using JobTracker.Presentation.ViewModels;
using System.Windows.Data;

namespace JobTracker.Presentation.Converters;

public sealed class NavigationSelectionConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is PageDestination page && values[1] is PageDestination destination &&
        (page == destination || page == PageDestination.Details && destination == PageDestination.Jobs);
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        targetTypes.Select(_ => Binding.DoNothing).ToArray();
}
