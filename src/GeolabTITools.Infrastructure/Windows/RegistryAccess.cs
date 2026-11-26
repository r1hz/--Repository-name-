using GeolabTITools.Application.Features.Windows.Contracts;
using GeolabTITools.Application.Features.Windows.DTOs;
using Microsoft.Win32;

namespace GeolabTITools.Infrastructure.Windows;

public sealed class RegistryAccess : IRegistryAccess
{
    public object? ReadValue(RegistryAddress address)
    {
        using var baseKey = RegistryKey.OpenBaseKey(ToHive(address.Root), ToView(address.View));
        using var key = baseKey.OpenSubKey(address.SubKey, writable: false);
        return key?.GetValue(address.ValueName);
    }

    public void WriteValue(RegistryAddress address, object value, RegistryDataKind kind)
    {
        ArgumentNullException.ThrowIfNull(value);
        using var baseKey = RegistryKey.OpenBaseKey(ToHive(address.Root), ToView(address.View));
        using var key = baseKey.CreateSubKey(address.SubKey, writable: true)
            ?? throw new InvalidOperationException($"Não foi possível abrir a chave HK{address.Root}\\{address.SubKey}.");
        key.SetValue(address.ValueName, value, ToKind(kind));
    }

    public bool DeleteValue(RegistryAddress address)
    {
        using var baseKey = RegistryKey.OpenBaseKey(ToHive(address.Root), ToView(address.View));
        using var key = baseKey.OpenSubKey(address.SubKey, writable: true);
        if (key == null || !key.GetValueNames().Contains(address.ValueName, StringComparer.OrdinalIgnoreCase))
            return false;
        key.DeleteValue(address.ValueName, throwOnMissingValue: true);
        return true;
    }

    private static RegistryHive ToHive(RegistryRoot root) => root switch
    {
        RegistryRoot.CurrentUser => RegistryHive.CurrentUser,
        RegistryRoot.LocalMachine => RegistryHive.LocalMachine,
        RegistryRoot.ClassesRoot => RegistryHive.ClassesRoot,
        RegistryRoot.Users => RegistryHive.Users,
        RegistryRoot.CurrentConfig => RegistryHive.CurrentConfig,
        _ => throw new ArgumentOutOfRangeException(nameof(root), root, "Hive não suportada.")
    };

    private static RegistryView ToView(RegistryViewMode view) => view switch
    {
        RegistryViewMode.Default => RegistryView.Default,
        RegistryViewMode.Registry32 => RegistryView.Registry32,
        RegistryViewMode.Registry64 => RegistryView.Registry64,
        _ => throw new ArgumentOutOfRangeException(nameof(view), view, "Visão do Registro não suportada.")
    };

    private static RegistryValueKind ToKind(RegistryDataKind kind) => kind switch
    {
        RegistryDataKind.String => RegistryValueKind.String,
        RegistryDataKind.ExpandString => RegistryValueKind.ExpandString,
        RegistryDataKind.Binary => RegistryValueKind.Binary,
        RegistryDataKind.DWord => RegistryValueKind.DWord,
        RegistryDataKind.MultiString => RegistryValueKind.MultiString,
        RegistryDataKind.QWord => RegistryValueKind.QWord,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Tipo de dado não suportado.")
    };
}
