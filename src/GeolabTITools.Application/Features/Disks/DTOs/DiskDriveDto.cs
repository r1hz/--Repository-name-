namespace GeolabTITools.Application.Features.Disks.DTOs;

public sealed record DiskDriveDto(
    string RootPath,
    string DriveLetter,
    string Label,
    string DriveKind,
    string FileSystem,
    long TotalBytes,
    long UsedBytes,
    long FreeBytes);

public sealed record DiskInfoReadResult(IReadOnlyList<DiskDriveDto> Drives, int UnavailableCount);
