using GeolabTITools.Application.Features.Disks.Services;

namespace GeolabTITools.Application.Features.Disks.DTOs;

/// <summary>Nó de pasta com os arquivos e subpastas que serão preenchidos pela varredura.</summary>
public sealed class FolderEntry
{
    public string Name { get; init; } = "";
    public string FullPath { get; init; } = "";
    public DateTime LastModifiedUtc { get; init; }
    public long TotalSizeBytes { get; set; }
    public string TotalSizeText => DiskFormatting.FormatBytes(TotalSizeBytes);
    public string LastModifiedText => LastModifiedUtc == default ? "" : LastModifiedUtc.ToLocalTime().ToString("g");
    public int FileCount => Files.Count;
    public int SubfolderCount => Folders.Count;
    public string SummaryText => $"{TotalSizeText}  ·  {FileCount:N0} arquivo(s)  ·  {SubfolderCount:N0} subpasta(s)";
    public List<FileEntry> Files { get; } = new();
    public List<FolderEntry> Folders { get; } = new();
    public FolderEntry? Parent { get; init; }
}
