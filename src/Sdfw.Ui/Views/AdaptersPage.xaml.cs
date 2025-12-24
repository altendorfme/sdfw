using System.Windows;
using System.Windows.Controls;
using Sdfw.Ui.ViewModels;

namespace Sdfw.Ui.Views;

public partial class AdaptersPage : Page
{
    private readonly AdaptersViewModel _viewModel;

    public AdaptersPage(AdaptersViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadAsync();
    }
}
