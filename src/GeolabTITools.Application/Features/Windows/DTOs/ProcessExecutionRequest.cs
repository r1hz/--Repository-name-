namespace GeolabTITools.Application.Features.Windows.DTOs;

public sealed record ProcessExecutionRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    bool RunAsAdministrator = false,
    bool WaitForExit = true,
    bool CaptureOutput = false);
