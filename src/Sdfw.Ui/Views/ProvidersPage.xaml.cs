using System.Windows;
using System.Windows.Controls;
using Sdfw.Ui.ViewModels;

namespace Sdfw.Ui.Views;

public partial class ProvidersPage : Page
{
    private readonly ProvidersViewModel _viewModel;
    private bool _subscribed;

    public ProvidersPage(ProvidersViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        
        InitializeComponent();

        Loaded += (_, _) => SubscribeEvents();
        Unloaded += (_, _) => UnsubscribeEvents();
    }

    private void SubscribeEvents()
    {
        if (_subscribed) return;
        _viewModel.ShowProviderDialog += OnShowProviderDialog;
        _subscribed = true;
    }

    private void UnsubscribeEvents()
    {
        if (!_subscribed) return;
        _viewModel.ShowProviderDialog -= OnShowProviderDialog;
        _subscribed = false;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }

    private async void OnShowProviderDialog(object? sender, ProviderDialogEventArgs e)
    {
        var dialog = new ProviderDialog();

        if (e.IsNew)
        {
            dialog.ConfigureForAdd();
        }
        else if (e.Provider != null)
        {
            dialog.ConfigureForEdit(e.Provider);
        }
        else
        {
            return;
        }

        // Set the owner to the main window so it appears centered
        dialog.Owner = Application.Current.MainWindow;

        // Show the dialog as a modal window
        dialog.ShowDialog();

        // Check if the user saved
        if (dialog.Confirmed && dialog.ResultProvider is not null)
        {
            await _viewModel.SaveProviderAsync(dialog.ResultProvider, dialog.IsNewProvider);
        }
    }

}
