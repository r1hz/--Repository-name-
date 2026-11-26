namespace GeolabTITools.Application.Features.Windows.DTOs;

public sealed record ProcessExecutionResult(
    bool Started,
    int? ExitCode,
    string StandardOutput,
    string StandardError);
