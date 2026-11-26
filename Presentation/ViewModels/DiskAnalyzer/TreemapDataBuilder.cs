using System.Collections.Generic;

using GeolabTITools.Application.Features.Disks.DTOs;

namespace ResetExplode.DiskAnalyzer;

/// <summary>Prepara um conjunto limitado de itens visuais, sem criar controles WPF para cada arquivo do disco.</summary>
public static class TreemapDataBuilder
{
    private const int MaximumFileTiles = 1200;
    private const int MaximumFolderTiles = 250;

    public static IReadOnlyList<TreemapTile> Build(FolderEntry folder, CancellationToken cancellationToken = default)
    {
        var tiles = new List<TreemapTile>();
        var files = new PriorityQueue<FileEntry, long>();
        long otherFileBytes = 0;
        int otherFileCount = 0;

        foreach (var file in folder.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (file.SizeBytes <= 0) continue;
            if (files.Count < MaximumFileTiles)
            {
                files.Enqueue(file, file.SizeBytes);
                continue;
            }

            if (files.TryPeek(out _, out long smallest) && file.SizeBytes > smallest)
            {
                var evicted = files.Dequeue();
                otherFileBytes = AddSaturated(otherFileBytes, evicted.SizeBytes);
                otherFileCount++;
                files.Enqueue(file, file.SizeBytes);
            }
            else
            {
                otherFileBytes = AddSaturated(otherFileBytes, file.SizeBytes);
                otherFileCount++;
            }
        }

        foreach (var entry in files.UnorderedItems.OrderByDescending(item => item.Priority))
        {
            var file = entry.Element;
            string category = string.IsNullOrWhiteSpace(file.Extension) ? "(sem extensão)" : file.Extension;
            tiles.Add(new TreemapTile(file.Name, file.SizeBytes, category, File: file));
        }
        if (otherFileBytes > 0)
            tiles.Add(new TreemapTile($"Outros arquivos ({otherFileCount:N0})", otherFileBytes, "Outros arquivos", GroupedItemsCount: otherFileCount));

        var folders = new PriorityQueue<FolderEntry, long>();
        long otherFolderBytes = 0;
        int otherFolderCount = 0;
        foreach (var child in folder.Folders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (child.TotalSizeBytes <= 0) continue;
            if (folders.Count < MaximumFolderTiles)
            {
                folders.Enqueue(child, child.TotalSizeBytes);
                continue;
            }

            if (folders.TryPeek(out _, out long smallest) && child.TotalSizeBytes > smallest)
            {
                var evicted = folders.Dequeue();
                otherFolderBytes = AddSaturated(otherFolderBytes, evicted.TotalSizeBytes);
                otherFolderCount++;
                folders.Enqueue(child, child.TotalSizeBytes);
            }
            else
            {
                otherFolderBytes = AddSaturated(otherFolderBytes, child.TotalSizeBytes);
                otherFolderCount++;
            }
        }

        foreach (var entry in folders.UnorderedItems.OrderByDescending(item => item.Priority))
        {
            var child = entry.Element;
            tiles.Add(new TreemapTile(child.Name, child.TotalSizeBytes, "Pastas", Folder: child));
        }
        if (otherFolderBytes > 0)
            tiles.Add(new TreemapTile($"Outras pastas ({otherFolderCount:N0})", otherFolderBytes, "Pastas", GroupedItemsCount: otherFolderCount));

        return tiles.OrderByDescending(tile => tile.SizeBytes).ToArray();
    }

    private static long AddSaturated(long current, long value)
        => value > 0 && current > long.MaxValue - value ? long.MaxValue : current + value;
}
