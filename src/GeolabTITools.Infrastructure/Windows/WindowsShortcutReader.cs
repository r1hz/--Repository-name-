using System.Runtime.InteropServices;
using GeolabTITools.Application.Features.Windows.Contracts;
using GeolabTITools.Application.Features.Windows.DTOs;

namespace GeolabTITools.Infrastructure.Windows;

public sealed class WindowsShortcutReader : IShortcutReader
{
    public ShortcutDetails? Read(string shortcutPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcutPath);
        object? shell = null;
        object? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;
            shell = Activator.CreateInstance(shellType);
            if (shell == null) return null;

            dynamic shellObject = shell;
            shortcut = shellObject.CreateShortcut(shortcutPath);
            if (shortcut == null) return null;
            dynamic shortcutObject = shortcut;
            string target = shortcutObject.TargetPath;
            string arguments = shortcutObject.Arguments;
            return string.IsNullOrWhiteSpace(target) ? null : new ShortcutDetails(target, arguments ?? "");
        }
        catch
        {
            // Invalid, missing, inaccessible, or malformed shortcuts are treated as unresolved.
            return null;
        }
        finally
        {
            try { ReleaseComObject(shortcut); } catch { }
            try { ReleaseComObject(shell); } catch { }
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value != null && Marshal.IsComObject(value))
            Marshal.FinalReleaseComObject(value);
    }
}
