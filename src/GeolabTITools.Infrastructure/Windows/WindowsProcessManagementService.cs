using System.Diagnostics;
using GeolabTITools.Application.Features.Windows.Contracts;
using GeolabTITools.Application.Features.Windows.DTOs;

namespace GeolabTITools.Infrastructure.Windows;

public sealed class WindowsProcessManagementService : IProcessManagementService
{
    public int CurrentSessionId
    {
        get
        {
            using var current = Process.GetCurrentProcess();
            return current.SessionId;
        }
    }

    public IReadOnlyList<ManagedProcessInfo> EnumerateProcesses()
    {
        var result = new List<ManagedProcessInfo>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var snapshot = TrySnapshot(process);
                if (snapshot != null) result.Add(snapshot);
            }
        }
        return result;
    }

    public IReadOnlyList<ManagedProcessInfo> FindByName(string processName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processName);
        var result = new List<ManagedProcessInfo>();
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                var snapshot = TrySnapshot(process);
                if (snapshot != null) result.Add(snapshot);
            }
        }
        return result;
    }

    public bool IsRunning(string processName)
    {
        try { return FindByName(processName).Count != 0; }
        catch { return false; }
    }

    public bool RequestClose(ManagedProcessInfo expected)
    {
        try
        {
            using var process = TryOpenExpected(expected);
            if (process == null) return false;
            return process.CloseMainWindow();
        }
        catch { return false; }
    }

    public bool Terminate(ManagedProcessInfo expected, bool entireProcessTree = false)
    {
        try
        {
            using var process = TryOpenExpected(expected);
            if (process == null) return false;
            process.Kill(entireProcessTree);
            return true;
        }
        catch { return false; }
    }

    private static ManagedProcessInfo? TrySnapshot(Process process)
    {
        string name;
        int id;
        try { name = process.ProcessName; id = process.Id; }
        catch { return null; }

        int? session = null;
        try { session = process.SessionId; }
        catch { }

        bool hasWindow = false;
        try { hasWindow = process.MainWindowHandle != IntPtr.Zero; }
        catch { }

        string? description = null;
        try
        {
            description = process.MainModule?.FileVersionInfo.FileDescription;
            if (string.IsNullOrWhiteSpace(description)) description = null;
        }
        catch { }

        long workingSet = 0;
        try { workingSet = process.WorkingSet64; }
        catch { }

        DateTime? startTimeUtc = null;
        try { startTimeUtc = process.StartTime.ToUniversalTime(); }
        catch { }

        return new ManagedProcessInfo(id, name, session, hasWindow, description, workingSet, startTimeUtc);
    }

    private static Process? TryOpenExpected(ManagedProcessInfo expected)
    {
        if (expected.StartTimeUtc is not DateTime expectedStart) return null;
        Process? process = null;
        try
        {
            process = Process.GetProcessById(expected.Id);
            bool sameProcess = string.Equals(process.ProcessName, expected.ProcessName, StringComparison.OrdinalIgnoreCase)
                && process.StartTime.ToUniversalTime() == expectedStart;
            if (sameProcess) return process;
        }
        catch { }

        process?.Dispose();
        return null;
    }
}
