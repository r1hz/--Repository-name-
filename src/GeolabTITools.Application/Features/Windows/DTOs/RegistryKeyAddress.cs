namespace GeolabTITools.Application.Features.Windows.DTOs;

public sealed record RegistryKeyAddress(
    RegistryRoot Root,
    string SubKey,
    RegistryViewMode View = RegistryViewMode.Default);
