using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using GeolabTITools.Application.Features.Windows.Contracts;
using GeolabTITools.Application.Features.Windows.DTOs;

namespace GeolabTITools.Infrastructure.Windows;

public sealed class WindowsMemoryManagementService : IMemoryManagementService
{
    public PhysicalMemoryStatus ReadPhysicalMemoryStatus()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status)
            ? new PhysicalMemoryStatus((long)status.TotalPhysical, (long)status.AvailablePhysical)
            : new PhysicalMemoryStatus(0, 0);
    }

    public bool TrimProcessWorkingSet(ManagedProcessInfo expected)
    {
        Process? process = null;
        try
        {
            if (expected.StartTimeUtc is not DateTime expectedStart) return false;
            process = Process.GetProcessById(expected.Id);
            if (!string.Equals(process.ProcessName, expected.ProcessName, StringComparison.OrdinalIgnoreCase)
                || process.StartTime.ToUniversalTime() != expectedStart) return false;
            return EmptyWorkingSet(process.Handle);
        }
        catch { return false; }
        finally { process?.Dispose(); }
    }

    public MemoryOperationResult PurgeStandbyList()
    {
        try
        {
            EnablePrivilege("SeProfileSingleProcessPrivilege");
            int command = 4; // MemoryPurgeStandbyList
            int status = NtSetSystemInformation(80 /* SystemMemoryListInformation */, ref command, sizeof(int));
            return status == 0
                ? new MemoryOperationResult(true, null)
                : new MemoryOperationResult(false, $"o Windows recusou (0x{status:X8}); é preciso rodar como administrador");
        }
        catch (Exception ex)
        {
            return new MemoryOperationResult(false, ex.Message);
        }
    }

    private static void EnablePrivilege(string name)
    {
        using var current = Process.GetCurrentProcess();
        if (!OpenProcessToken(current.Handle, 0x0020 | 0x0008, out var token))
            throw new Win32Exception();
        try
        {
            if (!LookupPrivilegeValue(null, name, out long luid)) throw new Win32Exception();
            var privileges = new TokenPrivileges { Count = 1, Luid = luid, Attributes = 2 };
            AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally { CloseHandle(token); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile,
            TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct TokenPrivileges { public uint Count; public long Luid; public uint Attributes; }

    [DllImport("psapi.dll")] private static extern bool EmptyWorkingSet(IntPtr process);
    [DllImport("ntdll.dll")] private static extern int NtSetSystemInformation(int informationClass, ref int information, int informationLength);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out long luid);
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges state, uint length, IntPtr previous, IntPtr returnLength);
}
