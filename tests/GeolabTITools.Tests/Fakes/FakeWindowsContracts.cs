using GeolabTITools.Application.Features.Windows.Contracts;
using GeolabTITools.Application.Features.Windows.DTOs;

namespace GeolabTITools.Tests.Fakes;

internal sealed class FakeRegistryAccess : IRegistryAccess
{
    public Dictionary<RegistryAddress, object> Values { get; } = [];
    public Dictionary<RegistryAddress, RegistryValueSnapshot> Snapshots { get; } = [];
    public Dictionary<RegistryKeyAddress, IReadOnlyList<string>> SubKeys { get; } = [];
    public Dictionary<RegistryKeyAddress, IReadOnlyList<RegistryValueSnapshot>> KeyValues { get; } = [];
    public RegistryAddress? LastWrite { get; private set; }
    public object? LastValue { get; private set; }
    public RegistryDataKind LastKind { get; private set; }
    public object? ReadValue(RegistryAddress address) => Values.GetValueOrDefault(address);
    public RegistryValueSnapshot? ReadValueWithKind(RegistryAddress address) => Snapshots.GetValueOrDefault(address);
    public bool KeyExists(RegistryKeyAddress address) => KeyValues.ContainsKey(address) || SubKeys.ContainsKey(address);
    public IReadOnlyList<string> EnumerateSubKeys(RegistryKeyAddress address) => SubKeys.GetValueOrDefault(address, []);
    public IReadOnlyList<RegistryValueSnapshot> EnumerateValues(RegistryKeyAddress address) => KeyValues.GetValueOrDefault(address, []);
    public void CreateKey(RegistryKeyAddress address) { }
    public void WriteValue(RegistryAddress address, object value, RegistryDataKind kind)
    { LastWrite = address; LastValue = value; LastKind = kind; Values[address] = value; }
    public bool DeleteValue(RegistryAddress address) => Values.Remove(address);
    public bool DeleteKeyTree(RegistryKeyAddress address) => KeyValues.Remove(address);
}

internal sealed class FakeProcessManagementService : IProcessManagementService
{
    public int CurrentSessionId { get; init; }
    public IReadOnlyList<ManagedProcessInfo> Processes { get; init; } = [];
    public IReadOnlyList<ManagedProcessInfo> EnumerateProcesses() => Processes;
    public IReadOnlyList<ManagedProcessInfo> FindByName(string processName) => Processes.Where(p => p.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase)).ToArray();
    public bool IsRunning(string processName) => FindByName(processName).Count > 0;
    public bool RequestClose(ManagedProcessInfo process) => false;
    public bool Terminate(ManagedProcessInfo process, bool entireProcessTree = false) => false;
}

internal sealed class FakeMemoryManagementService : IMemoryManagementService
{
    public PhysicalMemoryStatus Status { get; set; } = new(0, 0);
    public PhysicalMemoryStatus ReadPhysicalMemoryStatus() => Status;
    public bool TrimProcessWorkingSet(ManagedProcessInfo process) => false;
    public MemoryOperationResult PurgeStandbyList() => new(false, "not available in fake");
}

internal sealed class FakeFileSystemService : IFileSystemService
{
    public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Directories { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> DeletedFiles { get; } = [];
    public bool FileExists(string path) => Files.Contains(path);
    public long GetFileLength(string path) => throw new FileNotFoundException(path);
    public bool DirectoryExists(string path) => Directories.Contains(path);
    public IReadOnlyList<string> EnumerateFiles(string path, string searchPattern, bool recursive = false) => [];
    public IReadOnlyList<string> EnumerateDirectories(string path, bool recursive = false) => [];
    public void CreateDirectory(string path) => Directories.Add(path);
    public void DeleteFile(string path) { DeletedFiles.Add(path); Files.Remove(path); }
    public bool DeleteDirectory(string path, bool recursive = false) => Directories.Remove(path);
    public void MoveFile(string sourcePath, string destinationPath, bool overwrite = false)
    { Files.Remove(sourcePath); Files.Add(destinationPath); }
    public Stream OpenRead(string path) => throw new FileNotFoundException(path);
    public Stream CreateFile(string path) { Files.Add(path); return new MemoryStream(); }
}

internal sealed class FakeDownloadService : IDownloadService
{
    public Exception? Failure { get; set; }
    public Task<string> GetStringAsync(Uri source, Uri? referrer, bool ensureSuccessStatusCode, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return Failure is null ? Task.FromResult(string.Empty) : Task.FromException<string>(Failure); }
    public Task DownloadAsync(Uri source, string destinationPath, Uri? referrer, IProgress<DownloadProgress>? progress, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); return Failure is null ? Task.CompletedTask : Task.FromException(Failure); }
}

internal sealed class FakeShortcutReader : IShortcutReader
{
    public Dictionary<string, ShortcutDetails?> Shortcuts { get; } = new(StringComparer.OrdinalIgnoreCase);
    public ShortcutDetails? Read(string shortcutPath) => Shortcuts.GetValueOrDefault(shortcutPath);
}

internal sealed class FakeFileMetadataService : IFileMetadataService
{
    public Dictionary<string, FileMetadata> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);
    public FileMetadata Read(string path) => Metadata.GetValueOrDefault(path) ?? new FileMetadata(null, null);
}

internal sealed class FakeProcessExecutionService : IProcessExecutionService
{
    public ProcessExecutionRequest? LastRequest { get; private set; }
    public List<ProcessExecutionRequest> Requests { get; } = [];
    public ProcessExecutionResult Result { get; set; } = new(true, 0, "", "");
    public Task<ProcessExecutionResult> ExecuteAsync(ProcessExecutionRequest request, CancellationToken cancellationToken = default)
    { LastRequest = request; Requests.Add(request); cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(Result); }
}

internal sealed class FakeHardwareInventoryService : IHardwareInventoryService
{
    public IReadOnlyList<InstalledDriverInfo> InstalledDrivers { get; set; } = [];
    public IReadOnlyList<VideoControllerInfo> VideoControllers { get; set; } = [];
    public BoardSystemInfo Board { get; set; } = new("", "", "", "", "");
    public Exception? Failure { get; set; }
    public Task<IReadOnlyList<InstalledDriverInfo>> GetInstalledDriversAsync(CancellationToken cancellationToken = default) =>
        Respond(InstalledDrivers, cancellationToken);
    public Task<IReadOnlyList<VideoControllerInfo>> GetVideoControllersAsync(CancellationToken cancellationToken = default) =>
        Respond(VideoControllers, cancellationToken);
    public Task<BoardSystemInfo> GetBoardSystemInfoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Failure is null ? Task.FromResult(Board) : Task.FromException<BoardSystemInfo>(Failure);
    }
    private Task<IReadOnlyList<T>> Respond<T>(IReadOnlyList<T> value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Failure is null ? Task.FromResult(value) : Task.FromException<IReadOnlyList<T>>(Failure);
    }
}

internal sealed class FakeAuthenticodeSignatureVerifier : IAuthenticodeSignatureVerifier
{
    public AuthenticodeVerificationResult Result { get; set; } = new(true, true, "CN=NVIDIA", 0, null);
    public string? LastFilePath { get; private set; }
    public AuthenticodeVerificationResult Verify(string filePath) { LastFilePath = filePath; return Result; }
}

internal sealed class FakeWindowsUpdateService : IWindowsUpdateService
{
    public IReadOnlyList<WindowsUpdateDriverInfo> Results { get; set; } = [];
    public Exception? SearchFailure { get; set; }
    public Exception? InstallFailure { get; set; }
    public string InstallResult { get; set; } = "Instalação concluída.";
    public IReadOnlyList<WindowsUpdateDriverInfo>? Installed { get; private set; }
    public Task<IReadOnlyList<WindowsUpdateDriverInfo>> SearchDriversAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return SearchFailure is null ? Task.FromResult(Results) : Task.FromException<IReadOnlyList<WindowsUpdateDriverInfo>>(SearchFailure);
    }
    public Task<string> InstallDriversAsync(IReadOnlyList<WindowsUpdateDriverInfo> drivers, IProgress<string> progress,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Installed = drivers;
        progress.Report("Baixando drivers do Windows Update…");
        return InstallFailure is null ? Task.FromResult(InstallResult) : Task.FromException<string>(InstallFailure);
    }
}
