namespace GeolabTITools.Application.Features.Windows.DTOs;

/// <summary>Dados técnicos de um dispositivo USB/serial sem estado de apresentação.</summary>
public sealed record UsbDeviceInfo(
    string InstanceId, string Name, string DeviceClass, string? Port, bool Present,
    string? Maker, string? Vid, string? Pid, uint? Problem, string? DriverKey,
    bool SelectedByDefault);

public sealed record UsbDeviceRemovalResult(int Removed, IReadOnlyList<string> Failed);
public sealed record ComPortStatus(IReadOnlyList<int> Reserved, IReadOnlyList<int> InUse);
