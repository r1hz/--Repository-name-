using GeolabTITools.Application.Features.Windows.DTOs;

namespace GeolabTITools.Application.Features.Windows.Contracts;

public interface IRegistryAccess
{
    object? ReadValue(RegistryAddress address);
    RegistryValueSnapshot? ReadValueWithKind(RegistryAddress address);
    bool KeyExists(RegistryKeyAddress address);
    IReadOnlyList<string> EnumerateSubKeys(RegistryKeyAddress address);
    IReadOnlyList<RegistryValueSnapshot> EnumerateValues(RegistryKeyAddress address);
    void CreateKey(RegistryKeyAddress address);
    void WriteValue(RegistryAddress address, object value, RegistryDataKind kind);
    bool DeleteValue(RegistryAddress address);
    bool DeleteKeyTree(RegistryKeyAddress address);
}
