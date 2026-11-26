using GeolabTITools.Application.Features.Windows.DTOs;

namespace GeolabTITools.Application.Features.Windows.Contracts;

/// <summary>Queries and controls processes already running on the machine; this is separate from launching child processes.</summary>
public interface IProcessManagementService
{
    int CurrentSessionId { get; }
    IReadOnlyList<ManagedProcessInfo> EnumerateProcesses();
    IReadOnlyList<ManagedProcessInfo> FindByName(string processName);
    bool IsRunning(string processName);
    bool RequestClose(ManagedProcessInfo process);
    bool Terminate(ManagedProcessInfo process, bool entireProcessTree = false);
}
