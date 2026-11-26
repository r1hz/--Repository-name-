namespace GeolabTITools.Application.Features.Windows.DTOs;

/// <summary>A registry value together with its native kind, so backups can restore the original representation.</summary>
public sealed record RegistryValueSnapshot(string Name, object Value, RegistryDataKind Kind);
