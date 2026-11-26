namespace GeolabTITools.Application.Features.Disks.DTOs;

/// <summary>Fotografia leve do progresso da varredura para atualizar a interface em intervalos controlados.</summary>
public sealed record DiskScanProgress(
    string RootPath,
    string CurrentDirectory,
    int FilesScanned,
    int FoldersScanned,
    long ScannedBytes,
    TimeSpan Elapsed,
    int ErrorsEncountered,
    int ReparsePointsSkipped);
