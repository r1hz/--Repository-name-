namespace GeolabTITools.Application.Features.Windows.DTOs;

/// <summary>Opaque selection token and display data for one driver update; contains no COM object.</summary>
public sealed record WindowsUpdateDriverInfo(string SelectionToken, string Title, long SizeBytes);
