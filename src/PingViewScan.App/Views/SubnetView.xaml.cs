using System.Windows.Controls;
using PingViewScan.App.ViewModels;

namespace PingViewScan.App.Views;

public partial class SubnetView : UserControl
{
    public SubnetView()
    {
        InitializeComponent();
        DataContext = new SubnetViewModel();
    }
}
