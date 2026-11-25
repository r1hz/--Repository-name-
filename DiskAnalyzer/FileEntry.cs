namespace ResetExplode.DiskAnalyzer;

/// <summary>Metadados de um arquivo encontrados durante a análise do disco.</summary>
public sealed class FileEntry
{
    public string Name { get; init; } = "";
    public string FullPath { get; init; } = "";
    public string FolderPath { get; init; } = "";
    public string Extension { get; init; } = "";
    public long SizeBytes { get; init; }
    public DateTime LastModifiedUtc { get; init; }
    public string SizeText => DiskDriveOption.FormatBytes(SizeBytes);
    public string LastModifiedText => LastModifiedUtc.ToLocalTime().ToString("g");
}
