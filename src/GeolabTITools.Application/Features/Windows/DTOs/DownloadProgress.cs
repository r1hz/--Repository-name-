namespace GeolabTITools.Application.Features.Windows.DTOs;

public sealed record DownloadProgress(long BytesReceived, long? TotalBytes, double BytesPerSecond);
