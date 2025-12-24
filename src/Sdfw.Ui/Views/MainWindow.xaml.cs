using System.ComponentModel;
using System.Windows;
using Sdfw.Ui.Services;
using Sdfw.Ui.ViewModels;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace Sdfw.Ui.Views;

/// <summary>
/// Main window with navigation.
/// </summary>
public partial class MainWindow : FluentWindow
{
    private readonly MainWindowViewModel _viewModel;
    private readonly ISnackbarService _snackbarService;
    private readonly SettingsViewModel _settingsViewModel;
    private bool _closeToTray = true;
    private bool _minimizeToTray = true;

    public MainWindow(
        MainWindowViewModel viewModel,
        ISnackbarService snackbarService,
        IContentDialogService contentDialogService,
        IIpcClientService ipcClient,
        SettingsViewModel settingsViewModel)
    {
        _viewModel = viewModel;
        _snackbarService = snackbarService;
        _settingsViewModel = settingsViewModel;

        DataContext = viewModel;

        InitializeComponent();

        // Set up services
        _snackbarService.SetSnackbarPresenter(SnackbarPresenter);

        // Set up navigation
        NavigationView.SetServiceProvider(App.Services);
        NavigationView.Loaded += OnNavigationViewLoaded;

        // Subscribe to IPC events
        ipcClient.ConnectionChanged += OnIpcConnectionChanged;

        // Subscribe to settings changes
        _settingsViewModel.TrayBehaviorChanged += OnTrayBehaviorChanged;

        // Load initial data
        _ = LoadInitialDataAsync();
    }

    private void OnTrayBehaviorChanged(object? sender, TrayBehaviorChangedEventArgs e)
    {
        _minimizeToTray = e.MinimizeToTray;
        _closeToTray = e.CloseToTray;
    }

    private async void OnNavigationViewLoaded(object sender, RoutedEventArgs e)
    {
        // Navigate to dashboard by default
        NavigationView.Navigate(typeof(DashboardPage));
    }

    private async Task LoadInitialDataAsync()
    {
        await _viewModel.InitializeAsync();
        
        // Load settings to get tray behavior
        await _settingsViewModel.LoadAsync();
    }

    private void OnIpcConnectionChanged(object? sender, bool isConnected)
    {
        Dispatcher.Invoke(() =>
        {
            if (!isConnected)
            {
                _snackbarService.Show(
                    "Conexão perdida",
                    "Não foi possível conectar ao serviço SDfW. Verifique se o serviço está em execução.",
                    ControlAppearance.Caution,
                    null,
                    TimeSpan.FromSeconds(5));
            }
        });
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeToTray)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            // Explicitly shutdown the application when CloseToTray is disabled
            Application.Current.Shutdown();
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (_minimizeToTray && WindowState == WindowState.Minimized)
        {
            Hide();
        }
    }

    /// <summary>
    /// Updates tray behavior settings.
    /// </summary>
    public void UpdateTrayBehavior(bool minimizeToTray, bool closeToTray)
    {
        _minimizeToTray = minimizeToTray;
        _closeToTray = closeToTray;
    }
}
