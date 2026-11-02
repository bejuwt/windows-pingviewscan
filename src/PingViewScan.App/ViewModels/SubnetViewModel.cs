using System.Windows;
using PingViewScan.Core;

namespace PingViewScan.App.ViewModels;

public sealed record SubnetRow(string Label, string Value);

/// <summary>Backs the Subnet Calculator tab.</summary>
public sealed class SubnetViewModel : ObservableObject
{
    private string _input = "192.168.1.10/24";
    private string _address = "192.168.1.10";
    private int _prefix = 24;
    private SubnetInfo? _info;
    private IReadOnlyList<SubnetRow> _rows = Array.Empty<SubnetRow>();
    private string _statusText = "Enter an IPv4 address to calculate its subnet.";

    public SubnetViewModel()
    {
        Prefixes = Enumerable.Range(0, 33).ToList();
        CopyCommand = new RelayCommand(_ => Copy(), _ => Info is not null);
        Recompute();
    }

    public IReadOnlyList<int> Prefixes { get; }

    public RelayCommand CopyCommand { get; }

    public string Input
    {
        get => _input;
        set
        {
            if (Set(ref _input, value))
            {
                ParseInput();
            }
        }
    }

    public int Prefix
    {
        get => _prefix;
        set
        {
            var clamped = Math.Clamp(value, 0, 32);
            if (!Set(ref _prefix, clamped))
            {
                return;
            }

            if (_address.Length > 0)
            {
                var text = $"{_address}/{clamped}";
                if (!string.Equals(_input, text, StringComparison.Ordinal))
                {
                    _input = text;
                    OnPropertyChanged(nameof(Input));
                }
            }
            Recompute();
        }
    }

    public SubnetInfo? Info
    {
        get => _info;
        private set
        {
            if (Set(ref _info, value))
            {
                OnPropertyChanged(nameof(HasInfo));
                CopyCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasInfo => Info is not null;

    public IReadOnlyList<SubnetRow> Rows
    {
        get => _rows;
        private set => Set(ref _rows, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    private void ParseInput()
    {
        var (address, prefix) = SubnetCalculator.ParseInput(_input, _prefix);
        _address = address;
        if (prefix != _prefix)
        {
            _prefix = prefix;
            OnPropertyChanged(nameof(Prefix));
        }
        Recompute();
    }

    private void Recompute()
    {
        Info = _address.Length == 0 ? null : SubnetCalculator.Compute(_address, _prefix);

        if (Info is not { } info)
        {
            Rows = Array.Empty<SubnetRow>();
            StatusText = "Enter a valid IPv4 address to calculate its subnet.";
            return;
        }

        Rows = new[]
        {
            new SubnetRow("Address", info.Address),
            new SubnetRow("Network (CIDR)", info.Cidr),
            new SubnetRow("Subnet mask", info.Mask),
            new SubnetRow("Wildcard mask", info.Wildcard),
            new SubnetRow("Network address", info.Network),
            new SubnetRow("Broadcast address", info.Broadcast),
            new SubnetRow("Usable range", info.UsableRange),
            new SubnetRow("Usable hosts", info.UsableHosts.ToString("N0")),
            new SubnetRow("Total addresses", info.TotalAddresses.ToString("N0")),
            new SubnetRow("IP class", info.IpClass),
            new SubnetRow("Scope", info.Scope),
            new SubnetRow("Address (binary)", info.AddressBinary),
            new SubnetRow("Mask (binary)", info.MaskBinary)
        };
        StatusText = "Results update as you type.";
    }

    private void Copy()
    {
        if (Info is not { } info)
        {
            return;
        }

        try
        {
            Clipboard.SetText(info.Summary);
            StatusText = "Copied subnet summary to the clipboard.";
        }
        catch (Exception ex)
        {
            StatusText = $"Copy failed: {ex.Message}";
        }
    }
}
