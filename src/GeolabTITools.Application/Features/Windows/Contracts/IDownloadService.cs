using GeolabTITools.Application.Features.Windows.DTOs;

namespace GeolabTITools.Application.Features.Windows.Contracts;

public interface IDownloadService
{
    Task<string> GetStringAsync(
        Uri source,
        Uri? referrer,
        bool ensureSuccessStatusCode,
        CancellationToken cancellationToken = default);

    Task DownloadAsync(
        Uri source,
        string destinationPath,
        Uri? referrer,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken = default);
}
