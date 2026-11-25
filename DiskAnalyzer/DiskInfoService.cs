using System.IO;
using System.Management;
using System.Text.RegularExpressions;

namespace ResetExplode.DiskAnalyzer;

/// <summary>Identifica volumes legíveis e, quando o Windows fornece os dados, o disco físico associado.</summary>
public sealed class DiskInfoService
{
    public Task<DiskInfoReadResult> GetAvailableDrivesAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => ReadAvailableDrives(cancellationToken), cancellationToken);

    private static DiskInfoReadResult ReadAvailableDrives(CancellationToken cancellationToken)
    {
        var drives = new List<DiskDriveOption>();
        int unavailable = 0;
        var physicalByVolume = ReadPhysicalDiskByVolume();

        foreach (var drive in DriveInfo.GetDrives())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!drive.IsReady)
                {
                    unavailable++;
                    continue;
                }

                long total = drive.TotalSize;
                long free = drive.TotalFreeSpace;
                string root = drive.RootDirectory.FullName;
                physicalByVolume.TryGetValue(root.TrimEnd('\\'), out var physicalDisks);
                bool usb = physicalDisks?.Any(d => d.IsUsb) == true;
                int[] mediaTypes = physicalDisks?.Where(d => d.MediaType.HasValue).Select(d => d.MediaType!.Value).Distinct().ToArray()
                                   ?? Array.Empty<int>();
                string kind = GetDriveKind(drive.DriveType, usb, mediaTypes);

                drives.Add(new DiskDriveOption
                {
                    RootPath = root,
                    DriveLetter = root.TrimEnd('\\'),
                    Label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Sem rótulo" : drive.VolumeLabel,
                    DriveKind = kind,
                    FileSystem = TryGetFileSystem(drive),
                    TotalBytes = total,
                    FreeBytes = free,
                    UsedBytes = Math.Max(0, total - free),
                });
            }
            catch (Exception)
            {
                // Unidades que desconectam ou perdem acesso durante a consulta não interrompem a lista.
                unavailable++;
            }
        }

        return new DiskInfoReadResult(drives.OrderBy(d => d.DriveLetter, StringComparer.OrdinalIgnoreCase).ToArray(), unavailable);
    }

    private static string TryGetFileSystem(DriveInfo drive)
    {
        try { return drive.DriveFormat; }
        catch (Exception) { return "Indisponível"; }
    }

    private static string GetDriveKind(DriveType driveType, bool usb, IReadOnlyCollection<int> mediaTypes)
    {
        if (driveType == DriveType.Network) return "Unidade de rede";
        if (driveType == DriveType.CDRom) return "Unidade óptica";
        bool hdd = mediaTypes.Contains(3); // MSFT_PhysicalDisk.MediaType: 3 = HDD
        bool ssd = mediaTypes.Contains(4); // MSFT_PhysicalDisk.MediaType: 4 = SSD
        if (hdd && ssd) return "HD + SSD";
        if (driveType == DriveType.Removable) return ssd ? "SSD removível" : hdd ? "HD removível" : "Removível";
        if (driveType != DriveType.Fixed) return "Unidade";
        if (usb) return ssd ? "SSD externo (USB)" : hdd ? "HD externo (USB)" : "Externo (USB)";
        if (ssd) return "SSD";
        if (hdd) return "HD";
        return "Unidade fixa";
    }

    /// <summary>Associa letra lógica a disco físico pelas classes de associação padrão do WMI.</summary>
    private static Dictionary<string, List<PhysicalDiskInfo>> ReadPhysicalDiskByVolume()
    {
        var result = new Dictionary<string, List<PhysicalDiskInfo>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var disks = new Dictionary<string, PhysicalDiskInfo>(StringComparer.OrdinalIgnoreCase);
            using (var searcher = new ManagementObjectSearcher(
                       "SELECT DeviceID, InterfaceType, PNPDeviceID FROM Win32_DiskDrive"))
            using (var collection = searcher.Get())
            {
                foreach (ManagementObject disk in collection)
                {
                    string deviceId = disk["DeviceID"]?.ToString() ?? "";
                    string pnp = disk["PNPDeviceID"]?.ToString() ?? "";
                    disks[deviceId] = new PhysicalDiskInfo(
                        pnp.StartsWith("USBSTOR", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(disk["InterfaceType"]?.ToString(), "USB", StringComparison.OrdinalIgnoreCase), null);
                }
            }

            // O número de DeviceId da MSFT_PhysicalDisk corresponde ao índice do disco no Windows.
            try
            {
                var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
                scope.Connect();
                using var searcher = new ManagementObjectSearcher(scope,
                    new ObjectQuery("SELECT DeviceId, MediaType FROM MSFT_PhysicalDisk"));
                using var collection = searcher.Get();
                foreach (ManagementObject disk in collection)
                {
                    if (int.TryParse(disk["DeviceId"]?.ToString(), out int index)
                        && disks.TryGetValue($"\\\\.\\PHYSICALDRIVE{index}", out var info)
                        && int.TryParse(disk["MediaType"]?.ToString(), out int mediaType))
                        disks[$"\\\\.\\PHYSICALDRIVE{index}"] = info with { MediaType = mediaType };
                }
            }
            catch (ManagementException) { /* Em algumas versões do Windows a classe Storage não está disponível. */ }
            catch (UnauthorizedAccessException) { }

            var diskByPartition = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (var searcher = new ManagementObjectSearcher(
                       "SELECT Antecedent, Dependent FROM Win32_DiskDriveToDiskPartition"))
            using (var collection = searcher.Get())
            {
                foreach (ManagementObject association in collection)
                {
                    string? diskId = ReferenceDeviceId(association["Antecedent"]);
                    string? partitionId = ReferenceDeviceId(association["Dependent"]);
                    if (diskId is not null && partitionId is not null) diskByPartition[partitionId] = diskId;
                }
            }

            using (var searcher = new ManagementObjectSearcher(
                       "SELECT Antecedent, Dependent FROM Win32_LogicalDiskToPartition"))
            using (var collection = searcher.Get())
            {
                foreach (ManagementObject association in collection)
                {
                    string? partitionId = ReferenceDeviceId(association["Antecedent"]);
                    string? volumeId = ReferenceDeviceId(association["Dependent"]);
                    if (partitionId is null || volumeId is null || !diskByPartition.TryGetValue(partitionId, out var diskId)
                        || !disks.TryGetValue(diskId, out var info)) continue;

                    string volume = volumeId.EndsWith(':') ? volumeId + "\\" : volumeId;
                    if (!result.TryGetValue(volume.TrimEnd('\\'), out var list)) result[volume.TrimEnd('\\')] = list = new();
                    list.Add(info);
                }
            }
        }
        catch (ManagementException) { /* Unidades continuam disponíveis, com tipo físico genérico. */ }
        catch (UnauthorizedAccessException) { }
        catch (System.Runtime.InteropServices.COMException) { }
        catch (Exception) { /* A identificação física é opcional; mantém os volumes do .NET disponíveis. */ }

        return result;
    }

    private static string? ReferenceDeviceId(object? reference)
    {
        var match = Regex.Match(reference?.ToString() ?? "", "DeviceID=\"(.*)\"");
        return match.Success ? match.Groups[1].Value.Replace("\\\\", "\\", StringComparison.Ordinal) : null;
    }

    private sealed record PhysicalDiskInfo(bool IsUsb, int? MediaType);
}

public sealed record DiskInfoReadResult(IReadOnlyList<DiskDriveOption> Drives, int UnavailableCount);
