namespace GeolabTITools.Application.Features.Windows.DTOs;

public sealed record ProcessExecutionRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory = null,
    bool RunAsAdministrator = false,
    bool WaitForExit = true,
    bool CaptureOutput = false,
    IProgress<string>? StandardOutputProgress = null,
    System.Text.Encoding? StandardOutputEncoding = null,
    System.Text.Encoding? StandardErrorEncoding = null);
