using GeolabTITools.Application.Features.Disks.DTOs;
using GeolabTITools.Application.Features.Disks.Services;

namespace GeolabTITools.Application.Features.Disks.Contracts;

public interface IDiskScannerService
{
    Task<DiskAnalysisResult> AnalyzeAsync(
        string rootPath,
        IProgress<DiskScanProgress>? progress,
        DiskScanControl control,
        CancellationToken cancellationToken = default);
}
