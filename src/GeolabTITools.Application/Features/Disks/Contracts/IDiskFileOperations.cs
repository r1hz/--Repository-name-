namespace GeolabTITools.Application.Features.Disks.Contracts;

public interface IDiskFileOperations
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    void OpenDirectory(string path);
    void RevealFile(string path);
}
