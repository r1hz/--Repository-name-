using GeolabTITools.Application.Features.Windows.DTOs;

namespace GeolabTITools.Application.Features.Windows.Contracts;

public interface IProcessExecutionService
{
    Task<ProcessExecutionResult> ExecuteAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken = default);
}
