using GeolabTITools.Application.Features.Windows.DTOs;

namespace GeolabTITools.Application.Features.Windows.Contracts;

public interface IFileMetadataService
{
    FileMetadata Read(string path);
}
