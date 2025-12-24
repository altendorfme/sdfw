using System.Windows;
using System.Windows.Controls;
using Sdfw.Ui.ViewModels;

namespace Sdfw.Ui.Views;

/// <summary>
/// Dashboard page showing status and quick actions.
/// </summary>
public partial class DashboardPage : Page
{
    private readonly DashboardViewModel _viewModel;

    public DashboardPage(DashboardViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;

        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
        _viewModel.StartRealTimeUpdates();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _viewModel.StopRealTimeUpdates();
    }
}
