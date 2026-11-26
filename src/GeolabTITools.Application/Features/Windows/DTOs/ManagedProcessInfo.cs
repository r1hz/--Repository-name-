namespace GeolabTITools.Application.Features.Windows.DTOs;

public sealed record ManagedProcessInfo(
    int Id,
    string ProcessName,
    int? SessionId,
    bool HasMainWindow,
    string? Description,
    long WorkingSetBytes,
    DateTime? StartTimeUtc = null);
