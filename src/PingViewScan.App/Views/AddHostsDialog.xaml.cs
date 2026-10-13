using System.Windows;
using PingViewScan.Core;

namespace PingViewScan.App.Views;

public partial class AddHostsDialog : Window
{
    public AddHostsDialog()
    {
        InitializeComponent();
        Input.TextChanged += (_, _) => UpdateStatus();
        OnlyValid.Checked += (_, _) => UpdateStatus();
        OnlyValid.Unchecked += (_, _) => UpdateStatus();
        UpdateStatus();
    }

    public List<string> Result { get; private set; } = new();

    private bool FilterEnabled => OnlyValid.IsChecked == true;

    private void UpdateStatus()
    {
        var count = HostImport.Parse(Input.Text, FilterEnabled).Count;
        StatusText.Text = count == 0 ? "No hosts entered" : $"{count} host{(count == 1 ? "" : "s")} entered";
    }

    private void Paste_Click(object sender, RoutedEventArgs e)
    {
        if (Clipboard.ContainsText() && Clipboard.GetText() is { Length: > 0 } text)
        {
            if (Input.Text.Length == 0 || Input.Text.EndsWith('\n'))
            {
                Input.AppendText(text);
            }
            else
            {
                Input.AppendText("\n" + text);
            }
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        Result = HostImport.Parse(Input.Text, FilterEnabled);
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
