using GeolabTITools.Application.Features.Windows.DTOs;

namespace GeolabTITools.Application.Features.Windows.Contracts;

public interface IWindowsUpdateService
{
    Task<IReadOnlyList<WindowsUpdateDriverInfo>> SearchDriversAsync(CancellationToken cancellationToken = default);
    Task<string> InstallDriversAsync(IReadOnlyList<WindowsUpdateDriverInfo> drivers, IProgress<string> progress, CancellationToken cancellationToken = default);
}
