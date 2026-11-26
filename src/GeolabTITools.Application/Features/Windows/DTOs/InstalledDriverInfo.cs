namespace GeolabTITools.Application.Features.Windows.DTOs;

public sealed record InstalledDriverInfo(string DeviceName, string DeviceClass, string Manufacturer, string DriverVersion, string DriverDate, bool IsSigned);
