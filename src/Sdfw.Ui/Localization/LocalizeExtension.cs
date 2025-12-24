using System.Windows.Data;
using System.Windows.Markup;

namespace Sdfw.Ui.Localization;

[MarkupExtensionReturnType(typeof(string))]
public class LocalizeExtension : MarkupExtension
{
    public LocalizeExtension()
    {
    }

    public LocalizeExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string? Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key))
            return "[No Key]";

        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationService.Instance,
            Mode = BindingMode.OneWay
        };

        return binding.ProvideValue(serviceProvider);
    }
}

public static class Loc
{
    public static string Get(string key) => LocalizationService.Instance.GetString(key);

    public static string GetFormat(string key, params object[] args)
    {
        var format = LocalizationService.Instance.GetString(key);
        return string.Format(format, args);
    }
}
