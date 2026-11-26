namespace GeolabTITools.Application.Features.Disks.DTOs;

/// <summary>Resultado agregado da varredura de uma unidade.</summary>
public sealed class DiskAnalysisResult
{
    public string RootPath { get; init; } = "";
    public DateTime StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public bool IsComplete { get; init; }
    public long TotalBytes { get; init; }
    public long ScannedBytes { get; init; }
    public int FilesScanned { get; init; }
    public int FoldersScanned { get; init; }
    public IReadOnlyList<FileEntry> Files { get; init; } = Array.Empty<FileEntry>();
    public FolderEntry? RootFolder { get; init; }
    public IReadOnlyList<FileEntry> LargestFiles { get; init; } = Array.Empty<FileEntry>();
    public IReadOnlyList<FolderEntry> LargestFolders { get; init; } = Array.Empty<FolderEntry>();
    public IReadOnlyDictionary<string, ExtensionSummary> FilesByExtension { get; init; }
        = new Dictionary<string, ExtensionSummary>();
    public int ErrorsEncountered { get; init; }
    public int ReparsePointsSkipped { get; init; }
    public string? Message { get; init; }
}
