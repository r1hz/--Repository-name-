using System.Diagnostics;
using GeolabTITools.Application.Features.Windows.Contracts;
using GeolabTITools.Application.Features.Windows.DTOs;

namespace GeolabTITools.Infrastructure.FileSystem;

public sealed class WindowsFileMetadataService : IFileMetadataService
{
    public FileMetadata Read(string path)
    {
        var version = FileVersionInfo.GetVersionInfo(path);
        return new FileMetadata(version.FileDescription, version.CompanyName);
    }
}
