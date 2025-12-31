using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Hardcodet.Wpf.TaskbarNotification;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sdfw.Service;
using Sdfw.Ui.Localization;
using Sdfw.Ui.Services;
using Sdfw.Ui.ViewModels;
using Sdfw.Ui.Views;
using Serilog;
using Wpf.Ui;

namespace Sdfw.Ui;

/// <summary>
/// WPF Application entry point with DI and hosting.
/// </summary>
public partial class App : Application
{
    private IHost? _host;
    private TaskbarIcon? _trayIcon;
    private Mutex? _singleInstanceMutex;

    private const string MutexName = "Global\\SDfW_SingleInstance_Mutex";

    public static IServiceProvider Services { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance check
        _singleInstanceMutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            // Another instance is already running
            MessageBox.Show(
                Loc.Get("Dialog_AlreadyRunning"),
                "SDfW",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        // Configure Serilog first so we can log any startup errors
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Sdfw", "Logs", "ui-.log");

        try
        {
            // Ensure log directory exists
            var logDir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(logDir) && !Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext()
                .WriteTo.File(
                    logPath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Log.Information("SDfW UI starting...");
            Log.Information("Log file: {LogPath}", logPath);
        }
        catch (Exception ex)
        {
            // Fallback: show error if logging fails to initialize
            MessageBox.Show(
                $"Failed to initialize logging:\n\n{ex.Message}\n\nPath: {logPath}",
                "SDfW Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        try
        {
            Log.Debug("Building host and configuring services...");

            // Build host
            _host = Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureServices((context, services) =>
                {
                    Log.Debug("Registering WPF UI services...");
                    // WPF UI services
                    services.AddSingleton<IThemeService, ThemeService>();
                    services.AddSingleton<ISnackbarService, SnackbarService>();
                    services.AddSingleton<IContentDialogService, ContentDialogService>();

                    Log.Debug("Registering application services...");
                    // Application services
                    services.AddSingleton<IIpcClientService, IpcClientService>();
                    services.AddSingleton<ITrayIconService, TrayIconService>();
                    
                    // Update checker service
                    services.AddHttpClient<IUpdateCheckerService, UpdateCheckerService>(client =>
                    {
                        client.Timeout = TimeSpan.FromSeconds(10);
                        client.DefaultRequestHeaders.Add("User-Agent", "SDfW-UpdateChecker");
                    });

                    Log.Debug("Registering SDfW in-process services...");
                    services.AddSdfwService();

                    Log.Debug("Registering ViewModels...");
                    // ViewModels
                    services.AddSingleton<MainWindowViewModel>();
                    services.AddSingleton<DashboardViewModel>();
                    services.AddSingleton<ProvidersViewModel>();
                    services.AddSingleton<AdaptersViewModel>();
                    services.AddSingleton<SettingsViewModel>();

                    Log.Debug("Registering Views...");
                    // Views
                    services.AddSingleton<MainWindow>();
                    services.AddTransient<DashboardPage>();
                    services.AddTransient<ProvidersPage>();
                    services.AddTransient<AdaptersPage>();
                    services.AddTransient<SettingsPage>();
                })
                .Build();

            Services = _host.Services;
            Log.Debug("Host built successfully");

            Log.Debug("Starting host...");
            await _host.StartAsync();
            Log.Debug("Host started");

            // Initialize IPC client
            Log.Debug("Initializing IPC client...");
            try
            {
                var ipcClient = Services.GetRequiredService<IIpcClientService>();
                var connected = await ConnectIpcWithRetryAsync(ipcClient);
                if (connected)
                {
                    Log.Information("IPC client connected to service");

                    // Load language settings from config
                    await InitializeLanguageFromConfigAsync(ipcClient);

                    // Auto-register first DNS provider as default if none is set
                    await InitializeDefaultProviderAsync(ipcClient);
                }
                else
                {
                    Log.Warning("IPC client failed to connect to in-process server");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize IPC client");
            }

            // Initialize tray icon
            Log.Debug("Initializing tray icon...");
            try
            {
                var trayService = Services.GetRequiredService<ITrayIconService>();
                _trayIcon = trayService.Initialize();
                Log.Debug("Tray icon initialized");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to initialize tray icon");
            }

            // Show main window
            Log.Debug("Creating main window...");
            var mainWindow = Services.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;

            // Check if should start minimized
            var startMinimized = e.Args.Contains("--minimized");
            if (startMinimized)
            {
                Log.Information("Starting minimized");
                mainWindow.WindowState = WindowState.Minimized;
                mainWindow.Hide();
            }
            else
            {
                Log.Debug("Showing main window...");
                    mainWindow.Show();
                }
    
                // Check for updates if enabled
                await CheckForUpdatesAsync();
    
                Log.Information("SDfW UI startup complete");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Fatal error during startup");
            MessageBox.Show(
                $"Failed to start application:\n\n{ex.Message}\n\nDetails: {ex}",
                "SDfW Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static async Task InitializeLanguageFromConfigAsync(IIpcClientService ipcClient)
    {
        try
        {
            var configResponse = await ipcClient.GetConfigAsync();
            if (configResponse?.Settings?.UiSettings is not null)
            {
                var language = configResponse.Settings.UiSettings.Language;
                if (!string.IsNullOrEmpty(language))
                {
                    LocalizationService.Instance.SetLanguage(language);
                    Log.Information("Language initialized from config: {Language}", language);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load language from config, using default (English)");
        }
    }

    private static async Task InitializeDefaultProviderAsync(IIpcClientService ipcClient)
    {
        try
        {
            var configResponse = await ipcClient.GetConfigAsync();
            if (configResponse?.Settings is null)
            {
                Log.Warning("Cannot initialize default provider - config is null");
                return;
            }

            // Check if a default provider is already set AND valid.
            // (DefaultProfile.ProviderId can be set but refer to a deleted provider.)
            var defaultProviderId = configResponse.Settings.DefaultProfile?.ProviderId;
            var hasValidDefault = defaultProviderId != Guid.Empty &&
                                  defaultProviderId != default &&
                                  configResponse.Settings.Providers.Any(p => p.Id == defaultProviderId);

            if (hasValidDefault)
            {
                Log.Debug("Default provider already set and valid");
                return;
            }

            // If no default is set (or it's invalid) and there are providers, set the first one as default.
            if (configResponse.Settings.Providers.Count > 0)
            {
                var firstProvider = configResponse.Settings.Providers[0];

                // Persist default WITHOUT toggling Enabled.
                configResponse.Settings.DefaultProfile ??= new Sdfw.Core.Models.DnsProfile();
                configResponse.Settings.DefaultProfile.ProviderId = firstProvider.Id;

                var response = await ipcClient.SaveConfigAsync(configResponse.Settings);
                if (response?.Success == true)
                {
                    Log.Information("Set {ProviderName} as default DNS provider on startup", firstProvider.Name);
                }
                else
                {
                    Log.Warning("Failed to set default provider: {Error}", response?.ErrorMessage);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to initialize default provider");
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        Log.Information("SDfW UI shutting down...");

        // Perform unified DNS shutdown: revert, flush, and restore original DNS
        try
        {
            var ipcClient = Services?.GetService<IIpcClientService>();
            if (ipcClient is not null)
            {
                Log.Information("Performing unified DNS shutdown...");
                await ipcClient.ShutdownDnsAsync();
                Log.Information("DNS shutdown completed successfully");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error during DNS shutdown");
        }

        _trayIcon?.Dispose();

        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }

        // Release single instance mutex
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();

        await Log.CloseAndFlushAsync();

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Unhandled exception");
        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}",
            "SDfW Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }

    private static async Task<bool> ConnectIpcWithRetryAsync(IIpcClientService ipcClient, CancellationToken cancellationToken = default)
    {
        // The IPC server starts as an IHostedService, so there can be a small race during startup.
        const int attempts = 10;
        for (var i = 1; i <= attempts; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await ipcClient.ConnectAsync(cancellationToken))
            {
                return true;
            }

            await Task.Delay(200, cancellationToken);
        }

        return false;
    }

    private static async Task CheckForUpdatesAsync()
    {
        try
        {
            // Check if update checking is enabled in settings
            var ipcClient = Services.GetService<IIpcClientService>();
            if (ipcClient is not null)
            {
                var configResponse = await ipcClient.GetConfigAsync();
                if (configResponse?.Settings?.UiSettings?.CheckForUpdates == false)
                {
                    Log.Debug("Update checking is disabled in settings");
                    return;
                }
            }

            var updateChecker = Services.GetService<IUpdateCheckerService>();
            if (updateChecker is null)
            {
                Log.Warning("Update checker service not available");
                return;
            }

            Log.Debug("Checking for updates...");
            var updateInfo = await updateChecker.CheckForUpdatesAsync();

            if (updateInfo?.IsUpdateAvailable == true)
            {
                Log.Information("Update available: {Version}", updateInfo.Version);
                
                // Show update notification dialog on UI thread
                await Current.Dispatcher.InvokeAsync(() =>
                {
                    var message = string.Format(Loc.Get("Update_NewVersionAvailable"), updateInfo.Version);
                    var currentVersionText = string.Format(Loc.Get("Update_CurrentVersion"), updateChecker.CurrentVersion);
                    
                    var result = MessageBox.Show(
                        $"{message}\n\n{currentVersionText}",
                        Loc.Get("Update_Available"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        // Open the releases page in the default browser
                        try
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = "https://github.com/altendorfme/sdfw/releases",
                                UseShellExecute = true
                            });
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "Failed to open releases page");
                        }
                    }
                });
            }
            else
            {
                Log.Debug("No updates available");
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to check for updates");
        }
    }
}
