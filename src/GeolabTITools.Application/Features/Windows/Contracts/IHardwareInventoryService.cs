using GeolabTITools.Application.Features.Windows.DTOs;

namespace GeolabTITools.Application.Features.Windows.Contracts;

/// <summary>Hardware and driver inventory collected from the host operating system.</summary>
public interface IHardwareInventoryService
{
    Task<IReadOnlyList<InstalledDriverInfo>> GetInstalledDriversAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VideoControllerInfo>> GetVideoControllersAsync(CancellationToken cancellationToken = default);
    Task<BoardSystemInfo> GetBoardSystemInfoAsync(CancellationToken cancellationToken = default);
}
