using System.Management;
using GeolabTITools.Application.Features.Windows.Contracts;
using GeolabTITools.Application.Features.Windows.DTOs;

namespace GeolabTITools.Infrastructure.Windows.Wmi;

/// <summary>Windows Management Instrumentation adapter for the queries previously hosted by DriverManager.</summary>
public sealed class WmiHardwareInventoryService : IHardwareInventoryService
{
    public Task<IReadOnlyList<InstalledDriverInfo>> GetInstalledDriversAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<InstalledDriverInfo>>(ReadInstalledDrivers, cancellationToken);

    public Task<IReadOnlyList<VideoControllerInfo>> GetVideoControllersAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<VideoControllerInfo>>(ReadVideoControllers, cancellationToken);

    public Task<BoardSystemInfo> GetBoardSystemInfoAsync(CancellationToken cancellationToken = default) =>
        Task.Run(ReadBoardSystemInfo, cancellationToken);

    private static IReadOnlyList<InstalledDriverInfo> ReadInstalledDrivers()
    {
        var rows = new List<InstalledDriverInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT DeviceName, DeviceClass, Manufacturer, DriverVersion, DriverDate, IsSigned FROM Win32_PnPSignedDriver");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                    string deviceClass = ReadString(item, "DeviceClass") ?? "";
                    string manufacturer = ReadString(item, "Manufacturer") ?? "";
                    rows.Add(new InstalledDriverInfo(ReadString(item, "DeviceName") ?? "", deviceClass, manufacturer,
                        ReadString(item, "DriverVersion") ?? "—", WmiDate(ReadString(item, "DriverDate")),
                        item["IsSigned"] is bool signed && signed));
                }
            }
        }
        catch { }
        return rows;
    }

    private static IReadOnlyList<VideoControllerInfo> ReadVideoControllers()
    {
        var rows = new List<VideoControllerInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion, AdapterCompatibility FROM Win32_VideoController");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                    rows.Add(new VideoControllerInfo(ReadString(item, "Name") ?? "", ReadString(item, "DriverVersion") ?? "",
                        ReadString(item, "AdapterCompatibility") ?? ""));
            }
        }
        catch { }
        return rows;
    }

    private static BoardSystemInfo ReadBoardSystemInfo()
    {
        string boardManufacturer = "", boardProduct = "", bios = "", systemManufacturer = "", systemModel = "";
        try
        {
            using (var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
            using (var results = searcher.Get())
                foreach (ManagementObject item in results)
                {
                    using (item) { boardManufacturer = ReadString(item, "Manufacturer") ?? ""; boardProduct = ReadString(item, "Product") ?? ""; }
                    break;
                }
            using (var searcher = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS"))
            using (var results = searcher.Get())
                foreach (ManagementObject item in results)
                {
                    using (item) bios = $"{ReadString(item, "SMBIOSBIOSVersion")} ({WmiDate(ReadString(item, "ReleaseDate"))})";
                    break;
                }
            using (var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Model FROM Win32_ComputerSystem"))
            using (var results = searcher.Get())
                foreach (ManagementObject item in results)
                {
                    using (item) { systemManufacturer = ReadString(item, "Manufacturer") ?? ""; systemModel = ReadString(item, "Model") ?? ""; }
                    break;
                }
        }
        catch { }
        return new BoardSystemInfo(boardManufacturer, boardProduct, bios, systemManufacturer, systemModel);
    }

    private static string? ReadString(ManagementBaseObject item, string property) => item[property]?.ToString()?.Trim();

    private static string WmiDate(string? value)
    {
        try { if (!string.IsNullOrEmpty(value)) return ManagementDateTimeConverter.ToDateTime(value).ToString("dd/MM/yyyy"); }
        catch { }
        return "—";
    }
}
