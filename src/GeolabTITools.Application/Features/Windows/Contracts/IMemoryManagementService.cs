using GeolabTITools.Application.Features.Windows.DTOs;

namespace GeolabTITools.Application.Features.Windows.Contracts;

public interface IMemoryManagementService
{
    PhysicalMemoryStatus ReadPhysicalMemoryStatus();
    bool TrimProcessWorkingSet(ManagedProcessInfo process);
    MemoryOperationResult PurgeStandbyList();
}
