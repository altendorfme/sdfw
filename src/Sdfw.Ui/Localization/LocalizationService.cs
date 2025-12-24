using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace Sdfw.Ui.Localization;

public sealed class LocalizationService : INotifyPropertyChanged
{
    private static readonly Lazy<LocalizationService> _instance = new(() => new LocalizationService());
    private readonly ResourceManager _resourceManager;
    private CultureInfo _currentCulture;

    public static LocalizationService Instance => _instance.Value;

    public event PropertyChangedEventHandler? PropertyChanged;

    public CultureInfo CurrentCulture
    {
        get => _currentCulture;
        private set
        {
            if (_currentCulture != value)
            {
                _currentCulture = value;
                OnPropertyChanged(nameof(CurrentCulture));
                OnPropertyChanged("Item[]");
            }
        }
    }

    public string this[string key] => GetString(key);

    private LocalizationService()
    {
        _resourceManager = new ResourceManager("Sdfw.Ui.Localization.Resources", typeof(LocalizationService).Assembly);
        _currentCulture = CultureInfo.GetCultureInfo("en-US");
    }

    public void SetLanguage(string languageCode)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(languageCode);
            CurrentCulture = culture;
            
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;
            
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (CultureNotFoundException)
        {
            var fallback = CultureInfo.GetCultureInfo("en-US");
            CurrentCulture = fallback;
            Thread.CurrentThread.CurrentCulture = fallback;
            Thread.CurrentThread.CurrentUICulture = fallback;
        }
    }

    public string GetString(string key)
    {
        try
        {
            var value = _resourceManager.GetString(key, CurrentCulture);
            return value ?? $"[{key}]";
        }
        catch
        {
            return $"[{key}]";
        }
    }

    public event EventHandler? LanguageChanged;

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
