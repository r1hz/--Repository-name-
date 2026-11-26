namespace GeolabTITools.Application.Features.Windows.DTOs;

public enum RegistryRoot
{
    CurrentUser,
    LocalMachine,
    ClassesRoot,
    Users,
    CurrentConfig
}

public enum RegistryDataKind
{
    String,
    ExpandString,
    Binary,
    DWord,
    MultiString,
    QWord
}

public enum RegistryViewMode
{
    Default,
    Registry32,
    Registry64
}

public sealed record RegistryAddress(
    RegistryRoot Root,
    string SubKey,
    string ValueName,
    RegistryViewMode View = RegistryViewMode.Default);
