using System.Windows.Controls;
using PingViewScan.App.ViewModels;

namespace PingViewScan.App.Views;

public partial class MonitorView : UserControl
{
    public MonitorView()
    {
        InitializeComponent();
        DataContext = new MonitorViewModel();
    }
}
