using GeolabTITools.Application.Features.Windows.DTOs;

namespace GeolabTITools.Application.Features.Windows.Contracts;

public interface IRegistryAccess
{
    object? ReadValue(RegistryAddress address);
    void WriteValue(RegistryAddress address, object value, RegistryDataKind kind);
    bool DeleteValue(RegistryAddress address);
}
