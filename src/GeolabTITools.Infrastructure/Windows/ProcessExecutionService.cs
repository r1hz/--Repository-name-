using System.Diagnostics;
using GeolabTITools.Application.Features.Windows.Contracts;
using GeolabTITools.Application.Features.Windows.DTOs;

namespace GeolabTITools.Infrastructure.Windows;

public sealed class ProcessExecutionService : IProcessExecutionService
{
    public async Task<ProcessExecutionResult> ExecuteAsync(
        ProcessExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        ArgumentNullException.ThrowIfNull(request.Arguments);

        if (request.RunAsAdministrator && request.CaptureOutput)
            throw new ArgumentException("A execução elevada usa o shell e não pode capturar stdout/stderr.", nameof(request));

        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory)
                ? Environment.CurrentDirectory
                : request.WorkingDirectory,
            UseShellExecute = request.RunAsAdministrator,
            Verb = request.RunAsAdministrator ? "runas" : "",
            CreateNoWindow = !request.RunAsAdministrator,
            RedirectStandardOutput = request.CaptureOutput,
            RedirectStandardError = request.CaptureOutput,
        };

        foreach (var argument in request.Arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo);
        if (process == null)
            return new ProcessExecutionResult(false, null, "", "O processo não foi iniciado.");

        if (!request.WaitForExit)
            return new ProcessExecutionResult(true, null, "", "");

        Task<string> stdout = request.CaptureOutput
            ? process.StandardOutput.ReadToEndAsync(cancellationToken)
            : Task.FromResult("");
        Task<string> stderr = request.CaptureOutput
            ? process.StandardError.ReadToEndAsync(cancellationToken)
            : Task.FromResult("");

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
        return new ProcessExecutionResult(true, process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }
}
