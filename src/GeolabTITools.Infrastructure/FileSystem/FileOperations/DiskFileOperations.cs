using System.Diagnostics;
using System.IO;
using GeolabTITools.Application.Features.Disks.Contracts;

namespace GeolabTITools.Infrastructure.FileSystem.FileOperations;

public sealed class DiskFileOperations : IDiskFileOperations
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void OpenDirectory(string path)
        => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    public void RevealFile(string path)
    {
        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
        start.ArgumentList.Add($"/select,{path}");
        Process.Start(start);
    }
}
