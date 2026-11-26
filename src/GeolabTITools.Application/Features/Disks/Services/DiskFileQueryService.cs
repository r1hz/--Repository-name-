using GeolabTITools.Application.Features.Disks.Contracts;
using GeolabTITools.Application.Features.Disks.DTOs;

namespace GeolabTITools.Application.Features.Disks.Services;

public sealed class DiskFileQueryService : IDiskFileQueryService
{
    public IReadOnlyList<FileEntry> FilterAndSortFiles(
        IReadOnlyList<FileEntry> files,
        string searchText,
        string extension,
        string? folderPath,
        string sortKey,
        CancellationToken cancellationToken = default)
    {
        var rows = new List<FileEntry>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(searchText) && !file.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase)) continue;
            if (extension != "*" && !string.Equals(file.Extension, extension, StringComparison.OrdinalIgnoreCase)) continue;
            if (folderPath is not null && !string.Equals(file.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase)) continue;
            rows.Add(file);
        }

        rows.Sort((left, right) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            int comparison = sortKey switch
            {
                "size-asc" => left.SizeBytes.CompareTo(right.SizeBytes),
                "name" => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name),
                "extension" => StringComparer.OrdinalIgnoreCase.Compare(left.Extension, right.Extension),
                "date" => right.LastModifiedUtc.CompareTo(left.LastModifiedUtc),
                _ => right.SizeBytes.CompareTo(left.SizeBytes),
            };
            return comparison != 0 ? comparison : StringComparer.OrdinalIgnoreCase.Compare(left.FullPath, right.FullPath);
        });

        return rows;
    }
}
