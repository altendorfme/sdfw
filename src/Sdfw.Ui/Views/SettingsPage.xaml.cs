using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Sdfw.Ui.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Appearance;

namespace Sdfw.Ui.Views;

public partial class SettingsPage : Page
{
    private readonly SettingsViewModel _viewModel;

    public SettingsPage(SettingsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        // Subscribe to appearance changes
        _viewModel.AppearanceChanged += OnAppearanceChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }

    private void OnAppearanceChanged(object? sender, AppearanceChangedEventArgs e)
    {
        // Apply theme immediately
        var theme = e.Theme switch
        {
            "Light" => ApplicationTheme.Light,
            "Dark" => ApplicationTheme.Dark,
            _ => ApplicationTheme.Unknown
        };

        if (theme != ApplicationTheme.Unknown)
        {
            ApplicationThemeManager.Apply(theme);
        }
        else
        {
            // System theme - use High Contrast detection or default to Dark
            var isHighContrast = SystemParameters.HighContrast;
            if (!isHighContrast)
            {
                // Try to detect Windows theme from registry
                try
                {
                    using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                    var appsUseLightTheme = key?.GetValue("AppsUseLightTheme");
                    var useLightTheme = appsUseLightTheme is int value && value == 1;
                    ApplicationThemeManager.Apply(useLightTheme ? ApplicationTheme.Light : ApplicationTheme.Dark);
                }
                catch
                {
                    // Fallback to dark theme
                    ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                }
            }
            else
            {
                ApplicationThemeManager.Apply(ApplicationTheme.HighContrast);
            }
        }
    }
}
