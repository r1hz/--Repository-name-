namespace GeolabTITools.Application.Features.Windows.Contracts;

/// <summary>Filesystem operations used by Windows tooling, separated from the workflows that coordinate them.</summary>
public interface IFileSystemService
{
    bool FileExists(string path);
    long GetFileLength(string path);
    bool DirectoryExists(string path);
    IReadOnlyList<string> EnumerateFiles(string path, string searchPattern, bool recursive = false);
    IReadOnlyList<string> EnumerateDirectories(string path, bool recursive = false);
    void CreateDirectory(string path);
    void DeleteFile(string path);
    bool DeleteDirectory(string path, bool recursive = false);
    void MoveFile(string sourcePath, string destinationPath, bool overwrite = false);
    Stream OpenRead(string path);
    Stream CreateFile(string path);
}
