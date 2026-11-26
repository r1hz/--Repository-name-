using System.Diagnostics;
using System.IO;
using System.Security;
using GeolabTITools.Application.Features.Disks.Services;
using GeolabTITools.Application.Features.Disks.Contracts;
using GeolabTITools.Application.Features.Disks.DTOs;

namespace GeolabTITools.Infrastructure.FileSystem.DiskServices;

/// <summary>Varre recursivamente um volume por APIs do sistema de arquivos, sem ler ou alterar os dados.</summary>
public sealed class DiskScannerService : IDiskScannerService
{
    private const int ProgressIntervalMilliseconds = 250;
    private const int LargestItemsLimit = 50;

    public Task<DiskAnalysisResult> AnalyzeAsync(
        string rootPath,
        IProgress<DiskScanProgress>? progress,
        DiskScanControl control,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        ArgumentNullException.ThrowIfNull(control);
        return Task.Run(() => ScanCoreAsync(rootPath, progress, control, cancellationToken), cancellationToken);
    }

    private static async Task<DiskAnalysisResult> ScanCoreAsync(
        string rootPath,
        IProgress<DiskScanProgress>? progress,
        DiskScanControl control,
        CancellationToken cancellationToken)
    {
        string root = Path.GetFullPath(rootPath);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException("A unidade selecionada não está disponível para leitura.");

        DateTime startedAt = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var rootFolder = new FolderEntry
        {
            Name = GetFolderName(root), FullPath = root,
            LastModifiedUtc = TryGetDirectoryLastWriteTimeUtc(root),
        };
        var pending = new Stack<FolderEntry>();
        pending.Push(rootFolder);
        var visitedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { NormalizePath(root) };
        var extensions = new Dictionary<string, ExtensionSummary>(StringComparer.OrdinalIgnoreCase);
        var largestFiles = new PriorityQueue<FileEntry, long>();
        var allFiles = new List<FileEntry>();
        int filesScanned = 0, foldersScanned = 0, errors = 0, reparsePointsSkipped = 0;
        long bytesScanned = 0;
        string currentDirectory = root;
        long lastProgressTicks = 0;

        void ReportProgress(bool force = false)
        {
            if (progress is null) return;
            long now = stopwatch.ElapsedTicks;
            if (!force && now - lastProgressTicks < Stopwatch.Frequency * ProgressIntervalMilliseconds / 1000) return;
            lastProgressTicks = now;
            progress.Report(new DiskScanProgress(root, currentDirectory, filesScanned, foldersScanned,
                bytesScanned, stopwatch.Elapsed, errors, reparsePointsSkipped));
        }

        while (pending.Count > 0)
        {
            await control.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            FolderEntry folder = pending.Pop();
            currentDirectory = folder.FullPath;
            foldersScanned++;
            ReportProgress();
            var childDirectories = new List<FolderEntry>();

            try
            {
                foreach (string entryPath in Directory.EnumerateFileSystemEntries(folder.FullPath))
                {
                    await control.WaitIfPausedAsync(cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();

                    FileAttributes attributes;
                    try
                    {
                        attributes = File.GetAttributes(entryPath);
                    }
                    catch (Exception ex) when (IsFileSystemError(ex))
                    {
                        errors++;
                        ReportProgress();
                        continue;
                    }

                    // Não segue symlinks, junctions nem outros reparse points.
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        reparsePointsSkipped++;
                        ReportProgress();
                        continue;
                    }

                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        string normalized = NormalizePath(entryPath);
                        if (!visitedDirectories.Add(normalized))
                        {
                            reparsePointsSkipped++;
                            continue;
                        }

                        var child = new FolderEntry
                        {
                            Name = Path.GetFileName(entryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                            FullPath = entryPath,
                            LastModifiedUtc = TryGetDirectoryLastWriteTimeUtc(entryPath),
                            Parent = folder,
                        };
                        folder.Folders.Add(child);
                        childDirectories.Add(child);
                    }
                    else
                    {
                        try
                        {
                            var fileInfo = new FileInfo(entryPath);
                            long size = fileInfo.Length;
                            string extension = Path.GetExtension(fileInfo.Name).ToLowerInvariant();
                            if (extension.Length == 0) extension = "[sem extensão]";

                            var file = new FileEntry
                            {
                                Name = fileInfo.Name,
                                FullPath = fileInfo.FullName,
                                FolderPath = folder.FullPath,
                                Extension = extension,
                                SizeBytes = size,
                                LastModifiedUtc = fileInfo.LastWriteTimeUtc,
                            };
                            folder.Files.Add(file);
                            allFiles.Add(file);
                            filesScanned++;
                            bytesScanned = SaturatingAdd(bytesScanned, size);

                            for (FolderEntry? ancestor = folder; ancestor is not null; ancestor = ancestor.Parent)
                                ancestor.TotalSizeBytes = SaturatingAdd(ancestor.TotalSizeBytes, size);

                            if (!extensions.TryGetValue(extension, out var summary))
                                extensions[extension] = summary = new ExtensionSummary { Extension = extension };
                            summary.FileCount++;
                            summary.TotalBytes = SaturatingAdd(summary.TotalBytes, size);
                            AddLargestFile(largestFiles, file);
                        }
                        catch (Exception ex) when (IsFileSystemError(ex))
                        {
                            // Arquivo removido, bloqueado ou inacessível: registra o erro e segue para o próximo.
                            errors++;
                        }
                    }

                    ReportProgress();
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (IsFileSystemError(ex))
            {
                // Falha na enumeração afeta esta pasta; as demais pastas pendentes continuam sendo processadas.
                errors++;
            }

            // A pilha mantém a busca em profundidade sem chamadas recursivas nem enumeradores abertos.
            for (int i = childDirectories.Count - 1; i >= 0; i--) pending.Push(childDirectories[i]);
            ReportProgress();
        }

        stopwatch.Stop();
        ReportProgress(force: true);
        var largestFolders = CollectLargestFolders(rootFolder);
        foreach (var extension in extensions.Values)
            extension.PercentOfAnalyzedSpace = bytesScanned > 0 ? extension.TotalBytes * 100d / bytesScanned : 0;
        return new DiskAnalysisResult
        {
            RootPath = root,
            StartedAt = startedAt,
            CompletedAt = DateTime.UtcNow,
            IsComplete = true,
            TotalBytes = bytesScanned,
            ScannedBytes = bytesScanned,
            FilesScanned = filesScanned,
            FoldersScanned = foldersScanned,
            Files = allFiles,
            RootFolder = rootFolder,
            LargestFiles = largestFiles.UnorderedItems.Select(x => x.Element).OrderByDescending(f => f.SizeBytes).ToArray(),
            LargestFolders = largestFolders,
            FilesByExtension = extensions,
            ErrorsEncountered = errors,
            ReparsePointsSkipped = reparsePointsSkipped,
            Message = BuildCompletionMessage(filesScanned, foldersScanned, bytesScanned, errors, reparsePointsSkipped, stopwatch.Elapsed),
        };
    }

    private static void AddLargestFile(PriorityQueue<FileEntry, long> queue, FileEntry file)
    {
        if (queue.Count < LargestItemsLimit)
        {
            queue.Enqueue(file, file.SizeBytes);
            return;
        }

        if (queue.TryPeek(out _, out long smallest) && file.SizeBytes > smallest)
        {
            queue.Dequeue();
            queue.Enqueue(file, file.SizeBytes);
        }
    }

    private static IReadOnlyList<FolderEntry> CollectLargestFolders(FolderEntry root)
    {
        var largest = new PriorityQueue<FolderEntry, long>();
        var pending = new Stack<FolderEntry>(root.Folders);
        while (pending.Count > 0)
        {
            var folder = pending.Pop();
            if (largest.Count < LargestItemsLimit)
            {
                largest.Enqueue(folder, folder.TotalSizeBytes);
            }
            else if (largest.TryPeek(out _, out long smallest) && folder.TotalSizeBytes > smallest)
            {
                largest.Dequeue();
                largest.Enqueue(folder, folder.TotalSizeBytes);
            }

            foreach (var child in folder.Folders) pending.Push(child);
        }
        return largest.UnorderedItems.Select(item => item.Element)
            .OrderByDescending(folder => folder.TotalSizeBytes).ToArray();
    }

    private static string BuildCompletionMessage(int files, int folders, long bytes, int errors, int skippedLinks, TimeSpan elapsed)
    {
        string message = $"Análise concluída: {files:N0} arquivo(s), {folders:N0} pasta(s), {DiskFormatting.FormatBytes(bytes)} contabilizados em {elapsed:hh\\:mm\\:ss}.";
        if (errors > 0) message += $" {errors:N0} item(ns) não puderam ser lidos.";
        if (skippedLinks > 0) message += $" {skippedLinks:N0} link(s) ou junção(ões) foram ignorados.";
        return message;
    }

    private static string GetFolderName(string path)
    {
        string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private static DateTime TryGetDirectoryLastWriteTimeUtc(string path)
    {
        try { return Directory.GetLastWriteTimeUtc(path); }
        catch (Exception ex) when (IsFileSystemError(ex)) { return default; }
    }

    private static string NormalizePath(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static long SaturatingAdd(long left, long right)
        => right > 0 && left > long.MaxValue - right ? long.MaxValue : left + right;

    private static bool IsFileSystemError(Exception ex)
        => ex is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException;
}
