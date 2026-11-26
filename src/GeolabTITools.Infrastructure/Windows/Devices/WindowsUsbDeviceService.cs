using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using GeolabTITools.Application.Features.Windows.Contracts;
using GeolabTITools.Application.Features.Windows.DTOs;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace GeolabTITools.Infrastructure.Windows.Devices;

/// <summary>Windows-only adapter for SetupAPI, Configuration Manager and COM port reservations.</summary>
public sealed class WindowsUsbDeviceService(IRegistryAccess registry) : IUsbDeviceService, IComPortReservationService
{
    private readonly IRegistryAccess _registry = registry;
    private static readonly HashSet<string> Classes = new(StringComparer.OrdinalIgnoreCase)
        { "Ports", "USB", "USBDevice", "libusbK devices", "libusb-win32 devices", "WinUSB", "Modem" };
    private static readonly Dictionary<string, string> Vendors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["2341"] = "Arduino", ["2A03"] = "Arduino", ["1A86"] = "CH340/CH341", ["0403"] = "FTDI", ["10C4"] = "CP210x",
        ["303A"] = "Espressif (ESP32)", ["239A"] = "Adafruit", ["16C0"] = "Teensy", ["1B4F"] = "SparkFun",
        ["2E8A"] = "Raspberry Pi Pico", ["067B"] = "Prolific PL2303", ["0483"] = "STMicro", ["1366"] = "SEGGER",
    };
    private const string ArbiterKey = @"SYSTEM\CurrentControlSet\Control\COM Name Arbiter";
    private const string SerialCommKey = @"HARDWARE\DEVICEMAP\SERIALCOMM";

    public IReadOnlyList<UsbDeviceInfo> ListDevices()
    {
        var list = new List<UsbDeviceInfo>();
        IntPtr set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DIGCF_ALLCLASSES);
        if (set == InvalidHandle) throw new Win32Exception();
        try
        {
            var data = new DeviceInfoData { Size = (uint)Marshal.SizeOf<DeviceInfoData>() };
            for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
            {
                string cls = ReadDeviceString(set, ref data, SPDRP_CLASS) ?? "";
                string id = ReadInstanceId(set, ref data);
                Match vid = Regex.Match(id, @"VID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
                bool knownVid = vid.Success && Vendors.ContainsKey(vid.Groups[1].Value);
                if (!Classes.Contains(cls) && !knownVid) continue;
                bool present = CM_Get_DevNode_Status(out uint status, out uint problem, data.DevInst, 0) == 0;
                if (cls.Equals("USB", StringComparison.OrdinalIgnoreCase) && (id.StartsWith("USB\\ROOT_HUB") || id.StartsWith("PCI\\"))) continue;
                string name = ReadDeviceString(set, ref data, SPDRP_FRIENDLYNAME) ?? ReadDeviceString(set, ref data, SPDRP_DEVICEDESC) ?? id;
                Match pid = Regex.Match(id, @"PID_([0-9A-F]{4})", RegexOptions.IgnoreCase);
                bool selected = !present && !id.StartsWith("ACPI\\", StringComparison.OrdinalIgnoreCase)
                    && (cls.Equals("Ports", StringComparison.OrdinalIgnoreCase) || knownVid);
                list.Add(new UsbDeviceInfo(id, name, cls, ReadPortName(set, ref data), present,
                    knownVid ? Vendors[vid.Groups[1].Value] : null,
                    vid.Success ? vid.Groups[1].Value.ToUpperInvariant() : null,
                    pid.Success ? pid.Groups[1].Value.ToUpperInvariant() : null,
                    present && (status & DN_HAS_PROBLEM) != 0 ? problem : null,
                    ReadDeviceString(set, ref data, SPDRP_DRIVER), selected));
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return list.OrderBy(d => d.Present).ThenByDescending(d => d.Maker != null).ThenBy(d => d.Port == null).ThenBy(d => d.Name).ToArray();
    }

    public UsbDeviceRemovalResult RemoveAbsentDevices(IEnumerable<string> instanceIds)
    {
        var wanted = new HashSet<string>(instanceIds, StringComparer.OrdinalIgnoreCase);
        int removed = 0;
        var failed = new List<string>();
        IntPtr set = SetupDiGetClassDevsW(IntPtr.Zero, null, IntPtr.Zero, DIGCF_ALLCLASSES);
        if (set == InvalidHandle) throw new Win32Exception();
        try
        {
            var data = new DeviceInfoData { Size = (uint)Marshal.SizeOf<DeviceInfoData>() };
            for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
            {
                string id = ReadInstanceId(set, ref data);
                if (!wanted.Contains(id)) continue;
                if (CM_Get_DevNode_Status(out _, out _, data.DevInst, 0) == 0) { failed.Add($"{id} (está conectado)"); continue; }
                if (SetupDiCallClassInstaller(DIF_REMOVE, set, ref data)) removed++;
                else failed.Add($"{id} ({new Win32Exception(Marshal.GetLastWin32Error()).Message})");
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return new UsbDeviceRemovalResult(removed, failed);
    }

    public ComPortStatus ReadComPortStatus()
    {
        var database = ReadComDatabase() ?? Array.Empty<byte>();
        var reserved = database.SelectMany((b, i) => Enumerable.Range(0, 8).Where(bit => (b & (1 << bit)) != 0).Select(bit => i * 8 + bit + 1)).ToArray();
        var inUse = new HashSet<int>();
        foreach (var value in _registry.EnumerateValues(new RegistryKeyAddress(RegistryRoot.LocalMachine, SerialCommKey)))
            if (value.Value is string port && ParseCom(port) is int number) inUse.Add(number);
        foreach (var device in ListDevices().Where(d => d.Present))
            if (ParseCom(device.Port) is int number) inUse.Add(number);
        return new ComPortStatus(reserved, inUse.OrderBy(n => n).ToArray());
    }

    public byte[] RebuildComPortDatabase()
    {
        EnsureArbiterKeyExists();
        byte[] old = ReadComDatabase() ?? new byte[32];
        var db = new byte[Math.Max(32, old.Length)];
        foreach (int port in ReadComPortStatus().InUse)
            if (port >= 1 && port <= db.Length * 8) db[(port - 1) / 8] |= (byte)(1 << ((port - 1) % 8));
        _registry.WriteValue(new RegistryAddress(RegistryRoot.LocalMachine, ArbiterKey, "ComDB"), db, RegistryDataKind.Binary);
        return old;
    }

    public void RestoreComPortDatabase(byte[] backup)
    {
        EnsureArbiterKeyExists();
        _registry.WriteValue(new RegistryAddress(RegistryRoot.LocalMachine, ArbiterKey, "ComDB"), backup, RegistryDataKind.Binary);
    }

    private byte[]? ReadComDatabase() => _registry.ReadValue(new RegistryAddress(RegistryRoot.LocalMachine, ArbiterKey, "ComDB")) as byte[];
    private void EnsureArbiterKeyExists()
    {
        if (!_registry.KeyExists(new RegistryKeyAddress(RegistryRoot.LocalMachine, ArbiterKey)))
            throw new InvalidOperationException("Chave COM Name Arbiter não encontrada.");
    }
    private static int? ParseCom(string? value) => value != null && value.StartsWith("COM", StringComparison.OrdinalIgnoreCase) && int.TryParse(value[3..], out int n) ? n : null;
    private static string ReadInstanceId(IntPtr set, ref DeviceInfoData data)
    {
        var buffer = new StringBuilder(512);
        return SetupDiGetDeviceInstanceIdW(set, ref data, buffer, buffer.Capacity, out _) ? buffer.ToString() : "";
    }
    private static string? ReadDeviceString(IntPtr set, ref DeviceInfoData data, uint property)
    {
        var buffer = new byte[1024];
        if (!SetupDiGetDeviceRegistryPropertyW(set, ref data, property, out _, buffer, (uint)buffer.Length, out uint required)) return null;
        string value = Encoding.Unicode.GetString(buffer, 0, (int)Math.Min(required, buffer.Length)).TrimEnd('\0');
        return value.Length == 0 ? null : value;
    }
    private static string? ReadPortName(IntPtr set, ref DeviceInfoData data)
    {
        IntPtr handle = SetupDiOpenDevRegKey(set, ref data, DICS_FLAG_GLOBAL, 0, DIREG_DEV, KEY_READ);
        if (handle == InvalidHandle || handle == IntPtr.Zero) return null;
        using var key = RegistryKey.FromHandle(new SafeRegistryHandle(handle, ownsHandle: true));
        return key.GetValue("PortName") as string;
    }

    private static readonly IntPtr InvalidHandle = new(-1);
    private const int DIGCF_ALLCLASSES = 0x4;
    private const uint SPDRP_DEVICEDESC = 0, SPDRP_CLASS = 0x7, SPDRP_DRIVER = 0x9, SPDRP_FRIENDLYNAME = 0xC;
    private const uint DN_HAS_PROBLEM = 0x400, DIF_REMOVE = 0x5, DICS_FLAG_GLOBAL = 0x1, DIREG_DEV = 0x1, KEY_READ = 0x20019;
    [StructLayout(LayoutKind.Sequential)] private struct DeviceInfoData { public uint Size; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string? enumerator, IntPtr hwnd, int flags);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref DeviceInfoData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref DeviceInfoData data, StringBuilder id, int size, out int required);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr set, ref DeviceInfoData data, uint property, out uint regType, byte[] buffer, uint size, out uint required);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern IntPtr SetupDiOpenDevRegKey(IntPtr set, ref DeviceInfoData data, uint scope, uint hwProfile, uint keyType, uint access);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiCallClassInstaller(uint function, IntPtr set, ref DeviceInfoData data);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Get_DevNode_Status(out uint status, out uint problem, uint devInst, uint flags);
}
