using GeolabTITools.Application.Features.Windows.DTOs;

namespace GeolabTITools.Application.Features.Windows.Contracts;

/// <summary>USB device discovery and serial-port reservation operations.</summary>
public interface IUsbDeviceService
{
    IReadOnlyList<UsbDeviceInfo> ListDevices();
    UsbDeviceRemovalResult RemoveAbsentDevices(IEnumerable<string> instanceIds);
}

public interface IComPortReservationService
{
    ComPortStatus ReadComPortStatus();
    byte[] RebuildComPortDatabase();
    void RestoreComPortDatabase(byte[] backup);
}
