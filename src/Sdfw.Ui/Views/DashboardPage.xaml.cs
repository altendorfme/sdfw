using System.Diagnostics;
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
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }

    private void OnDnsCheckClick(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://dnscheck.tools/",
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore errors opening URL
        }
    }
}
