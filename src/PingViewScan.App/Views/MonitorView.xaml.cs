using System.Windows.Controls;
using PingViewScan.App.ViewModels;
using PingViewScan.Core;

namespace PingViewScan.App.Views;

public partial class MonitorView : UserControl
{
    private readonly MonitorViewModel _viewModel;

    public MonitorView()
    {
        // Assign before InitializeComponent: the DataGrid's SelectionChanged
        // handler can run while the XAML is being loaded.
        _viewModel = new MonitorViewModel();
        InitializeComponent();
        DataContext = _viewModel;
    }

    private void HostsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.SelectedHost = HostsGrid.SelectedItem as Host;
        _viewModel.SetSelectedHosts(HostsGrid.SelectedItems.OfType<Host>());
    }
}
