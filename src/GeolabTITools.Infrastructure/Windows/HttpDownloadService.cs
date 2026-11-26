using System.Diagnostics;
using GeolabTITools.Application.Features.Windows.Contracts;
using GeolabTITools.Application.Features.Windows.DTOs;

namespace GeolabTITools.Infrastructure.Windows;

public sealed class HttpDownloadService(HttpClient httpClient, IFileSystemService fileSystem) : IDownloadService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly IFileSystemService _fileSystem = fileSystem;

    public async Task<string> GetStringAsync(
        Uri source,
        Uri? referrer,
        bool ensureSuccessStatusCode,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, source);
        if (referrer != null) request.Headers.Referrer = referrer;
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (ensureSuccessStatusCode) response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DownloadAsync(
        Uri source,
        string destinationPath,
        Uri? referrer,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        string temporaryPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".download";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, source);
            if (referrer != null) request.Headers.Referrer = referrer;
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            long? totalBytes = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = _fileSystem.CreateFile(temporaryPath);
            var buffer = new byte[1 << 20];
            long received = 0;
            var timer = Stopwatch.StartNew();

            while (true)
            {
                int count = await input.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (count == 0) break;
                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                received += count;
                progress?.Report(new DownloadProgress(received, totalBytes,
                    received / Math.Max(0.1, timer.Elapsed.TotalSeconds)));
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            _fileSystem.MoveFile(temporaryPath, destinationPath, overwrite: true);
        }
        catch
        {
            try { _fileSystem.DeleteFile(temporaryPath); } catch { }
            throw;
        }
    }
}
