using System.ComponentModel;
using GeolabTITools.Application.Features.Windows.DTOs;

namespace ResetExplode;

/// <summary>Modelo de apresentação de um dispositivo USB/serial (conectado ou registro antigo).</summary>
public sealed class UsbDevice : INotifyPropertyChanged
{
    public static UsbDevice FromInfo(UsbDeviceInfo info) => new()
    {
        InstanceId = info.InstanceId, Name = info.Name, Class = info.DeviceClass, Port = info.Port,
        Present = info.Present, Maker = info.Maker, Vid = info.Vid, Pid = info.Pid, Problem = info.Problem,
        DriverKey = info.DriverKey, Selected = info.SelectedByDefault,
    };
    public string InstanceId { get; init; } = "";
    public string Name { get; init; } = "";
    public string Class { get; init; } = "";
    public string? Port { get; init; }
    public bool Present { get; init; }
    public string? Maker { get; init; }
    public string? Vid { get; init; }
    public string? Pid { get; init; }
    public uint? Problem { get; init; }
    public string? DriverKey { get; init; }
    public string Detail => string.Join(" · ", new[] { Port, Maker, Class }.Where(s => !string.IsNullOrEmpty(s)));
    private bool _selected;
    public bool Selected { get => _selected; set { _selected = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}
