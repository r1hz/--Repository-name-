using GeolabTITools.Application.Features.Disks.DTOs;

namespace GeolabTITools.Application.Features.Disks.Contracts;

public interface IDiskFileQueryService
{
    IReadOnlyList<FileEntry> FilterAndSortFiles(
        IReadOnlyList<FileEntry> files,
        string searchText,
        string extension,
        string? folderPath,
        string sortKey,
        CancellationToken cancellationToken = default);
}
