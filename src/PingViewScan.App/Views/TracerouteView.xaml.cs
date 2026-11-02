using System.Windows.Controls;
using PingViewScan.App.ViewModels;

namespace PingViewScan.App.Views;

public partial class TracerouteView : UserControl
{
    public TracerouteView()
    {
        InitializeComponent();
        DataContext = new TracerouteViewModel();
    }
}
