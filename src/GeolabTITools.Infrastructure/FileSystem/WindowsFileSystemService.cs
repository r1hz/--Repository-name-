using GeolabTITools.Application.Features.Windows.Contracts;

namespace GeolabTITools.Infrastructure.FileSystem;

public sealed class WindowsFileSystemService : IFileSystemService
{
    public bool FileExists(string path) => File.Exists(path);
    public long GetFileLength(string path) => new FileInfo(path).Length;
    public bool DirectoryExists(string path) => Directory.Exists(path);

    public IReadOnlyList<string> EnumerateFiles(string path, string searchPattern, bool recursive = false) =>
        Directory.EnumerateFiles(path, searchPattern,
            recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).ToArray();

    public IReadOnlyList<string> EnumerateDirectories(string path, bool recursive = false) =>
        Directory.EnumerateDirectories(path, "*",
            recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).ToArray();

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public void DeleteFile(string path) => File.Delete(path);

    public bool DeleteDirectory(string path, bool recursive = false)
    {
        if (!Directory.Exists(path)) return false;
        Directory.Delete(path, recursive);
        return true;
    }

    public void MoveFile(string sourcePath, string destinationPath, bool overwrite = false) =>
        File.Move(sourcePath, destinationPath, overwrite);

    public Stream OpenRead(string path) => File.OpenRead(path);
    public Stream CreateFile(string path) => File.Create(path);
}
