using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Sdfw.Ui.Localization;
using Sdfw.Ui.Services;

namespace Sdfw.Ui.ViewModels;

public class LanguageOption
{
    public string Code { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    public override string ToString() => DisplayName;

    public override bool Equals(object? obj)
    {
        if (obj is LanguageOption other)
            return Code == other.Code;
        return false;
    }

    public override int GetHashCode() => Code.GetHashCode();
}

public partial class SettingsViewModel : ObservableObject
{
    private readonly IIpcClientService _ipcClient;
    private readonly ILogger<SettingsViewModel> _logger;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private bool _closeToTray = true;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _startMinimized;

    [ObservableProperty]
    private bool _checkForUpdates = true;

    [ObservableProperty]
    private string _selectedTheme = "System";

    [ObservableProperty]
    private LanguageOption _selectedLanguage;

    public string[] AvailableThemes { get; } = ["Light", "Dark", "System"];

    public LanguageOption[] AvailableLanguages { get; } =
    [
        new LanguageOption { Code = "en-US", DisplayName = "English" },
        new LanguageOption { Code = "pt-BR", DisplayName = "Português (Brasil)" }
    ];

    public event EventHandler<TrayBehaviorChangedEventArgs>? TrayBehaviorChanged;
    public event EventHandler<AppearanceChangedEventArgs>? AppearanceChanged;

    public SettingsViewModel(IIpcClientService ipcClient, ILogger<SettingsViewModel> logger)
    {
        _ipcClient = ipcClient;
        _logger = logger;
        _selectedLanguage = AvailableLanguages[0];
    }

    public async Task LoadAsync()
    {
        IsLoading = true;

        try
        {
            var response = await _ipcClient.GetConfigAsync();
            if (response?.Settings is not null)
            {
                var ui = response.Settings.UiSettings;
                MinimizeToTray = ui.MinimizeToTray;
                CloseToTray = ui.CloseToTray;
                StartWithWindows = ui.StartWithWindows;
                StartMinimized = ui.StartMinimized;
                CheckForUpdates = ui.CheckForUpdates;
                SelectedTheme = ui.Theme;

                var languageCode = ui.Language;
                SelectedLanguage = AvailableLanguages.FirstOrDefault(l => l.Code == languageCode)
                    ?? AvailableLanguages[0];

                TrayBehaviorChanged?.Invoke(this, new TrayBehaviorChangedEventArgs(MinimizeToTray, CloseToTray));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading settings");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        IsLoading = true;

        try
        {
            var response = await _ipcClient.GetConfigAsync();
            if (response?.Settings is null)
            {
                return;
            }

            response.Settings.UiSettings.MinimizeToTray = MinimizeToTray;
            response.Settings.UiSettings.CloseToTray = CloseToTray;
            response.Settings.UiSettings.StartWithWindows = StartWithWindows;
            response.Settings.UiSettings.StartMinimized = StartMinimized;
            response.Settings.UiSettings.CheckForUpdates = CheckForUpdates;
            response.Settings.UiSettings.Theme = SelectedTheme;
            response.Settings.UiSettings.Language = SelectedLanguage.Code;

            var saveResponse = await _ipcClient.SaveConfigAsync(response.Settings);
            if (saveResponse?.Success == true)
            {
                LocalizationService.Instance.SetLanguage(SelectedLanguage.Code);
                TrayBehaviorChanged?.Invoke(this, new TrayBehaviorChangedEventArgs(MinimizeToTray, CloseToTray));
                AppearanceChanged?.Invoke(this, new AppearanceChangedEventArgs(SelectedTheme, SelectedLanguage.Code));
            }

            UpdateWindowsStartup(StartWithWindows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving settings");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task ResetToDefaultsAsync()
    {
        MinimizeToTray = true;
        CloseToTray = true;
        StartWithWindows = false;
        StartMinimized = false;
        CheckForUpdates = true;
        SelectedTheme = "System";
        SelectedLanguage = AvailableLanguages[0];

        await SaveAsync();
    }

    private void UpdateWindowsStartup(bool enable)
    {
        const string taskName = "SDfW";

        try
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;

            if (enable && exePath is not null)
            {
                var args = $"/create /tn \"{taskName}\" /tr \"\\\"{exePath}\\\" --minimized\" /sc onlogon /rl highest /f";

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                process?.WaitForExit(5000);
            }
            else
            {
                var args = $"/delete /tn \"{taskName}\" /f";

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                process?.WaitForExit(5000);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating Windows startup via Task Scheduler");
        }
    }
}

public class TrayBehaviorChangedEventArgs : EventArgs
{
    public bool MinimizeToTray { get; }
    public bool CloseToTray { get; }

    public TrayBehaviorChangedEventArgs(bool minimizeToTray, bool closeToTray)
    {
        MinimizeToTray = minimizeToTray;
        CloseToTray = closeToTray;
    }
}

public class AppearanceChangedEventArgs : EventArgs
{
    public string Theme { get; }
    public string Language { get; }

    public AppearanceChangedEventArgs(string theme, string language)
    {
        Theme = theme;
        Language = language;
    }
}
