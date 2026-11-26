using GeolabTITools.Application.Features.Disks.DTOs;

namespace GeolabTITools.Application.Features.Disks.Contracts;

public interface IDiskInfoService
{
    Task<DiskInfoReadResult> GetAvailableDrivesAsync(CancellationToken cancellationToken = default);
}
