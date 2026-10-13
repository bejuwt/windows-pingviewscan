using System.Windows.Controls;
using PingViewScan.App.ViewModels;

namespace PingViewScan.App.Views;

public partial class ScanView : UserControl
{
    public ScanView()
    {
        InitializeComponent();
        DataContext = new ScanViewModel();
    }
}
