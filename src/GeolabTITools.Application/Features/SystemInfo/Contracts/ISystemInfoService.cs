using GeolabTITools.Application.Features.SystemInfo.DTOs;

namespace GeolabTITools.Application.Features.SystemInfo.Contracts;

public interface ISystemInfoService
{
    IReadOnlyList<InfoNode> Collect();
    void RefreshDisks(InfoNode storageNode);
    string ReadCpuName();
}
