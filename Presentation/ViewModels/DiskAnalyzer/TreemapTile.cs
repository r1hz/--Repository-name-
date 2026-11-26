using GeolabTITools.Application.Features.Disks.DTOs;

namespace ResetExplode.DiskAnalyzer;

/// <summary>Retângulo de arquivo, pasta ou grupo agregado usado pelo Treemap.</summary>
public sealed record TreemapTile(
    string Label,
    long SizeBytes,
    string CategoryKey,
    FileEntry? File = null,
    FolderEntry? Folder = null,
    int GroupedItemsCount = 0)
{
    public string SizeText => DiskDriveOption.FormatBytes(SizeBytes);
    public bool IsAggregate => File is null && Folder is null;
}

public sealed class TreemapFolderEventArgs(FolderEntry folder) : EventArgs
{
    public FolderEntry Folder { get; } = folder;
}

public sealed record TreemapLayoutItem(TreemapTile Tile, System.Windows.Rect Bounds);
