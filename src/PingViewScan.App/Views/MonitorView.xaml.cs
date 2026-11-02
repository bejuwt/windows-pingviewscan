using System.Windows.Controls;
using PingViewScan.App.ViewModels;
using PingViewScan.Core;

namespace PingViewScan.App.Views;

public partial class MonitorView : UserControl
{
    private readonly MonitorViewModel _viewModel;

    public MonitorView()
    {
        InitializeComponent();
        _viewModel = new MonitorViewModel();
        DataContext = _viewModel;
    }

    private void HostsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _viewModel.SelectedHost = HostsGrid.SelectedItem as Host;
        _viewModel.SetSelectedHosts(HostsGrid.SelectedItems.OfType<Host>());
    }
}
