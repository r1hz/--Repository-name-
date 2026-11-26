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

    public RegistryValueSnapshot? ReadValueWithKind(RegistryAddress address)
    {
        using var baseKey = RegistryKey.OpenBaseKey(ToHive(address.Root), ToView(address.View));
        using var key = baseKey.OpenSubKey(address.SubKey, writable: false);
        if (key == null || !key.GetValueNames().Contains(address.ValueName, StringComparer.OrdinalIgnoreCase))
            return null;

        object value = key.GetValue(address.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames)!;
        return new RegistryValueSnapshot(address.ValueName, value, ToDataKind(key.GetValueKind(address.ValueName)));
    }

    public bool KeyExists(RegistryKeyAddress address)
    {
        using var baseKey = RegistryKey.OpenBaseKey(ToHive(address.Root), ToView(address.View));
        using var key = baseKey.OpenSubKey(address.SubKey, writable: false);
        return key != null;
    }

    public IReadOnlyList<string> EnumerateSubKeys(RegistryKeyAddress address)
    {
        using var baseKey = RegistryKey.OpenBaseKey(ToHive(address.Root), ToView(address.View));
        using var key = baseKey.OpenSubKey(address.SubKey, writable: false);
        return key?.GetSubKeyNames() ?? Array.Empty<string>();
    }

    public IReadOnlyList<RegistryValueSnapshot> EnumerateValues(RegistryKeyAddress address)
    {
        using var baseKey = RegistryKey.OpenBaseKey(ToHive(address.Root), ToView(address.View));
        using var key = baseKey.OpenSubKey(address.SubKey, writable: false);
        if (key == null) return Array.Empty<RegistryValueSnapshot>();

        return key.GetValueNames().Select(name => new RegistryValueSnapshot(
            name,
            key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)!,
            ToDataKind(key.GetValueKind(name)))).ToArray();
    }

    public void CreateKey(RegistryKeyAddress address)
    {
        using var baseKey = RegistryKey.OpenBaseKey(ToHive(address.Root), ToView(address.View));
        using var key = baseKey.CreateSubKey(address.SubKey, writable: true)
            ?? throw new InvalidOperationException($"Não foi possível criar a chave HK{address.Root}\\{address.SubKey}.");
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

    public bool DeleteKeyTree(RegistryKeyAddress address)
    {
        using var baseKey = RegistryKey.OpenBaseKey(ToHive(address.Root), ToView(address.View));
        using var parent = baseKey.OpenSubKey(ParentPath(address.SubKey), writable: true);
        if (parent == null || !parent.GetSubKeyNames().Contains(LeafName(address.SubKey), StringComparer.OrdinalIgnoreCase))
            return false;

        parent.DeleteSubKeyTree(LeafName(address.SubKey), throwOnMissingSubKey: true);
        return true;
    }

    private static string ParentPath(string path)
    {
        int separator = path.LastIndexOf('\\');
        return separator < 0 ? string.Empty : path[..separator];
    }

    private static string LeafName(string path) => path[(path.LastIndexOf('\\') + 1)..];

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
        RegistryDataKind.None => RegistryValueKind.None,
        RegistryDataKind.String => RegistryValueKind.String,
        RegistryDataKind.ExpandString => RegistryValueKind.ExpandString,
        RegistryDataKind.Binary => RegistryValueKind.Binary,
        RegistryDataKind.DWord => RegistryValueKind.DWord,
        RegistryDataKind.MultiString => RegistryValueKind.MultiString,
        RegistryDataKind.QWord => RegistryValueKind.QWord,
        RegistryDataKind.Unknown => throw new ArgumentOutOfRangeException(nameof(kind), kind, "O tipo Unknown é somente informativo e não pode ser gravado."),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Tipo de dado não suportado.")
    };

    private static RegistryDataKind ToDataKind(RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.None => RegistryDataKind.None,
        RegistryValueKind.String => RegistryDataKind.String,
        RegistryValueKind.ExpandString => RegistryDataKind.ExpandString,
        RegistryValueKind.Binary => RegistryDataKind.Binary,
        RegistryValueKind.DWord => RegistryDataKind.DWord,
        RegistryValueKind.MultiString => RegistryDataKind.MultiString,
        RegistryValueKind.QWord => RegistryDataKind.QWord,
        _ => RegistryDataKind.Unknown
    };
}
