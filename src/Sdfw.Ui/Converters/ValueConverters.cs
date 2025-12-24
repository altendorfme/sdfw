using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Sdfw.Core.Models;
using Sdfw.Ui.Localization;
using Wpf.Ui.Controls;

namespace Sdfw.Ui.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility.Visible;
    }
}

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is false ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility.Collapsed;
    }
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is not true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is not true;
    }
}

public class BoolToYesNoConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? Loc.Get("Common_Yes") : Loc.Get("Common_No");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value?.ToString() == Loc.Get("Common_Yes");
    }
}

public class ListToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is IEnumerable<string> list)
        {
            return string.Join(", ", list);
        }
        if (value is IEnumerable enumerable)
        {
            return string.Join(", ", enumerable.Cast<object>().Select(x => x?.ToString() ?? ""));
        }
        return value?.ToString() ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ProviderTypeToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            DnsProviderType.Standard => "Globe24",
            DnsProviderType.DoH => "ShieldLock24",
            _ => "Globe24"
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class BoolToAppearanceConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not true)
            return ControlAppearance.Secondary;

        var appearanceStr = parameter as string ?? "Primary";
        return appearanceStr switch
        {
            "Primary" => ControlAppearance.Primary,
            "Success" => ControlAppearance.Success,
            "Danger" => ControlAppearance.Danger,
            "Caution" => ControlAppearance.Caution,
            "Info" => ControlAppearance.Info,
            _ => ControlAppearance.Secondary
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class ActiveToConnectIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true
            ? new SymbolIcon { Symbol = SymbolRegular.Stop24 }
            : new SymbolIcon { Symbol = SymbolRegular.Play24 };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class TemporaryActiveToConnectIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true
            ? new SymbolIcon { Symbol = SymbolRegular.Pause24 }
            : new SymbolIcon { Symbol = SymbolRegular.Play24 };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class TemporaryActiveToSymbolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? SymbolRegular.Pause24 : SymbolRegular.Play24;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class LocalizedActiveToConnectTooltipConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? Loc.Get("Providers_Disconnect") : Loc.Get("Providers_Connect");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class LocalizedTemporaryActiveToConnectTooltipConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? Loc.Get("Providers_Disconnect") : Loc.Get("Providers_Connect");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public class DnsListToDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is IEnumerable<string> list && list.Any())
        {
            var dnsServers = string.Join(", ", list);
            return Loc.GetFormat("Adapters_Dns", dnsServers);
        }
        return Loc.Get("Adapters_DnsAutomatic");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

[Obsolete("Use LocalizedActiveToConnectTooltipConverter instead")]
public class ActiveToConnectTooltipConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? Loc.Get("Providers_Disconnect") : Loc.Get("Providers_Connect");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
