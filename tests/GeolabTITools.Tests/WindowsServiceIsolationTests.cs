using GeolabTITools.Application.Features.Windows.Contracts;
using GeolabTITools.Application.Features.Windows.DTOs;
using GeolabTITools.Infrastructure.Windows.Devices;
using GeolabTITools.Infrastructure.Windows;
using GeolabTITools.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using GeolabTITools.Tests.Fakes;
using System.Net;
using ResetExplode;

namespace GeolabTITools.Tests;

public sealed class WindowsServiceIsolationTests
{
    [Fact]
    public void DependencyInjection_ResolvesSixWindowsServicesWithoutCallingThem()
    {
        var registrations = new ServiceCollection();
        registrations.AddGeolabInfrastructure();
        registrations.AddSingleton<WindowsTweaks>();
        registrations.AddSingleton<StartupManager>();
        registrations.AddSingleton<DriverManager>();
        registrations.AddSingleton<MakerDrivers>();
        registrations.AddSingleton<Optimizer>();
        using var provider = registrations.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        Assert.NotNull(provider.GetRequiredService<WindowsTweaks>());
        Assert.NotNull(provider.GetRequiredService<StartupManager>());
        Assert.NotNull(provider.GetRequiredService<DriverManager>());
        Assert.NotNull(provider.GetRequiredService<MakerDrivers>());
        Assert.NotNull(provider.GetRequiredService<Optimizer>());
        Assert.NotNull(provider.GetRequiredService<IUsbDeviceService>());
        Assert.NotNull(provider.GetRequiredService<IComPortReservationService>());
        Assert.Same(provider.GetRequiredService<IUsbDeviceService>(), provider.GetRequiredService<IComPortReservationService>());
        Assert.NotNull(provider.GetRequiredService<IHardwareInventoryService>());
        Assert.NotNull(provider.GetRequiredService<IAuthenticodeSignatureVerifier>());
        Assert.NotNull(provider.GetRequiredService<IWindowsUpdateService>());
    }

    [Fact]
    public void Optimizer_FiltersProtectedAndOtherSessionProcesses_AndGroupsApplications()
    {
        var processes = new FakeProcessManagementService
        {
            Processes =
            [
                new(10, "OneDrive", 4, false, null, 100),
                new(11, "OneDrive", 4, true, "OneDrive", 200),
                new(12, "explorer", 4, true, null, 500),
                new(13, "Editor", 9, true, null, 900),
                new(14, "Editor", 4, false, null, 1000),
            ]
        };

        var apps = new Optimizer(processes, new FakeMemoryManagementService()).ListApps();

        var app = Assert.Single(apps);
        Assert.Equal("OneDrive", app.ProcessName);
        Assert.Equal(2, app.Count);
        Assert.Equal(300, app.MemoryBytes);
        Assert.True(app.Suggested);
        Assert.True(app.Selected);
        Assert.DoesNotContain(apps, item => item.ProcessName == "explorer" || item.ProcessName == "Editor");
    }

    [Fact]
    public void UsbDevicePresentationMapping_PreservesInventoryAndDefaultSelection()
    {
        var info = new UsbDeviceInfo("USB\\VID_2341", "Arduino", "Ports", "COM4", false,
            "Arduino", "2341", "0043", 28, "{class}\\0001", true);

        var device = UsbDevice.FromInfo(info);

        Assert.Equal(info.InstanceId, device.InstanceId);
        Assert.Equal(info.DeviceClass, device.Class);
        Assert.Equal(info.Port, device.Port);
        Assert.Equal(info.Problem, device.Problem);
        Assert.True(device.Selected);
    }

    [Fact]
    public void WindowsUsbAdapter_RestoresComDatabaseThroughInjectedRegistryContract()
    {
        var registry = new FakeRegistryAccess();
        var service = new WindowsUsbDeviceService(registry);
        byte[] backup = [0x01, 0x80];

        service.RestoreComPortDatabase(backup);

        Assert.Equal(RegistryRoot.LocalMachine, registry.LastWrite!.Root);
        Assert.Equal(@"SYSTEM\CurrentControlSet\Control\COM Name Arbiter", registry.LastWrite.SubKey);
        Assert.Equal("ComDB", registry.LastWrite.ValueName);
        Assert.Equal(RegistryDataKind.Binary, registry.LastKind);
        Assert.Equal(backup, Assert.IsType<byte[]>(registry.LastValue));
    }

    [Fact]
    public void WindowsUsbAdapter_DoesNotCreateMissingComArbiterKeyDuringRestore()
    {
        var registry = new FakeRegistryAccess { ArbiterExists = false };
        var service = new WindowsUsbDeviceService(registry);

        Assert.Throws<InvalidOperationException>(() => service.RestoreComPortDatabase([0x01]));
        Assert.Null(registry.LastWrite);
    }

    [Fact]
    public async Task HttpDownloadService_RemovesTemporaryFileWhenServerReturnsFailure()
    {
        var fileSystem = new FakeFileSystemService();
        using var client = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        var service = new HttpDownloadService(client, fileSystem);

        await Assert.ThrowsAsync<HttpRequestException>(() => service.DownloadAsync(
            new Uri("https://example.invalid/driver.exe"), "destination.exe", null, null));

        Assert.Single(fileSystem.DeletedFiles);
        Assert.EndsWith(".download", fileSystem.DeletedFiles[0]);
        Assert.Empty(fileSystem.Files);
    }

    [Fact]
    public async Task HttpDownloadService_PropagatesCancellationAndCleansTemporaryFile()
    {
        var fileSystem = new FakeFileSystemService();
        using var client = new HttpClient(new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var service = new HttpDownloadService(client, fileSystem);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.DownloadAsync(
            new Uri("https://example.invalid/driver.exe"), "destination.exe", null, null, cancellation.Token));

        Assert.Single(fileSystem.DeletedFiles);
        Assert.Empty(fileSystem.Files);
    }

    [Fact]
    public async Task DriverManager_MapsAndFiltersWmiDriverRows_AndHandlesMissingProperties()
    {
        var hardware = new FakeHardwareInventoryService
        {
            InstalledDrivers =
            [
                new("Display Adapter", "DISPLAY", "Microsoft", "1.0", "01/01/2025", true),
                new("", "NET", "Vendor", "2.0", "—", false),
                new("Network Adapter", "NET", "Microsoft", "", "", true),
                new("Generic", "SOFTWARE", "Vendor", "3.0", "—", true),
                new("Audio", "MEDIA", "Vendor", "4.0", "02/02/2025", false),
            ]
        };
        var manager = CreateDriverManager(hardwareInventory: hardware);

        var drivers = await manager.InventoryAsync();

        Assert.Equal(new[] { "Display Adapter", "Audio", "Network Adapter" }, drivers.Select(d => d.Device));
        Assert.Equal("—", drivers.Single(d => d.Device == "Network Adapter").Version);
        Assert.Equal("—", drivers.Single(d => d.Device == "Network Adapter").Date);
    }

    [Fact]
    public async Task DriverManager_MapsGpuVendorsAndSkipsBasicOrUnnamedControllers()
    {
        var hardware = new FakeHardwareInventoryService
        {
            VideoControllers =
            [
                new("NVIDIA GeForce RTX 4070", "31.0.15.4601", "NVIDIA"),
                new("Radeon RX 6800", "24.1", "Advanced Micro Devices"),
                new("Intel Graphics", "1.2", "Intel"),
                new("Microsoft Basic Display Adapter", "", "Microsoft"),
                new("", "", ""),
            ]
        };

        var gpus = await CreateDriverManager(hardwareInventory: hardware).ReadGpusAsync();

        Assert.Equal(new[] { "NVIDIA", "AMD", "Intel" }, gpus.Select(g => g.Vendor));
        Assert.Equal("546.01", gpus[0].Installed);
    }

    [Fact]
    public async Task DriverManager_ReturnsEmptyInventoryOnWmiFailureAndPropagatesCancellation()
    {
        var hardware = new FakeHardwareInventoryService { Failure = new InvalidOperationException("WMI unavailable") };
        var manager = CreateDriverManager(hardwareInventory: hardware);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Empty(await manager.InventoryAsync());
        Assert.Empty(await manager.ReadGpusAsync());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.InventoryAsync(cancelled.Token));
    }

    [Fact]
    public async Task DriverManager_MapsBoardAndSystemIdentityAndReturnsEmptyResultWhenMissing()
    {
        var hardware = new FakeHardwareInventoryService
        {
            Board = new BoardSystemInfo("ASUSTeK COMPUTER INC.", "PRIME X", "BIOS (01/01/2025)", "ASUS", "PRIME X")
        };
        var board = await CreateDriverManager(hardwareInventory: hardware).ReadBoardAsync();
        Assert.Equal("ASUS", board.Brand);
        Assert.Equal("https://www.asus.com/support/download-center/", board.SupportUrl);
        Assert.Equal("BIOS (01/01/2025)", board.Bios);

        hardware.Failure = new InvalidOperationException("WMI access denied");
        var unavailable = await CreateDriverManager(hardwareInventory: hardware).ReadBoardAsync();
        Assert.Equal("", unavailable.Maker);
        Assert.Equal("", unavailable.Model);
        Assert.Equal("", unavailable.SupportUrl);
    }

    [Fact]
    public async Task DriverManager_UsesOnlyTrustedAuthenticodeSignaturesAndChecksPublisher()
    {
        var fileSystem = new FakeFileSystemService();
        var processes = new FakeProcessExecutionService();
        var signature = new FakeAuthenticodeSignatureVerifier
        {
            Result = new AuthenticodeVerificationResult(true, false, "CN=NVIDIA", unchecked((int)0x800B0109), "untrusted")
        };
        var manager = CreateDriverManager(fileSystem: fileSystem, processExecution: processes, signatureVerifier: signature);
        var gpu = new GpuDriverStatus { Vendor = "NVIDIA", DownloadUrl = "https://example.invalid/driver.exe", Latest = "1.0" };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.DownloadAndLaunchAsync(gpu, (_, _) => { }, CancellationToken.None));

        Assert.Contains("Assinatura digital inválida", error.Message);
        Assert.Single(fileSystem.DeletedFiles);
        Assert.Empty(processes.Requests);
    }

    [Fact]
    public async Task DriverManager_RejectsTrustedSignatureFromUnexpectedPublisher()
    {
        var fileSystem = new FakeFileSystemService();
        var processes = new FakeProcessExecutionService();
        var signature = new FakeAuthenticodeSignatureVerifier
        {
            Result = new AuthenticodeVerificationResult(true, true, "CN=Other Publisher", 0, null)
        };
        var manager = CreateDriverManager(fileSystem: fileSystem, processExecution: processes, signatureVerifier: signature);
        var gpu = new GpuDriverStatus { Vendor = "NVIDIA", DownloadUrl = "https://example.invalid/driver.exe", Latest = "1.0" };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.DownloadAndLaunchAsync(gpu, (_, _) => { }, CancellationToken.None));

        Assert.Contains("assinado por outro fabricante", error.Message);
        Assert.Single(fileSystem.DeletedFiles);
        Assert.Empty(processes.Requests);
    }

    [Fact]
    public async Task DriverManager_RejectsAnUnsignedFile()
    {
        var fileSystem = new FakeFileSystemService();
        var processes = new FakeProcessExecutionService();
        var signature = new FakeAuthenticodeSignatureVerifier
        {
            Result = new AuthenticodeVerificationResult(false, false, null, unchecked((int)0x800B0100), "no signature")
        };
        var manager = CreateDriverManager(fileSystem: fileSystem, processExecution: processes, signatureVerifier: signature);
        var gpu = new GpuDriverStatus { Vendor = "NVIDIA", DownloadUrl = "https://example.invalid/driver.exe", Latest = "1.0" };

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.DownloadAndLaunchAsync(gpu, (_, _) => { }, CancellationToken.None));

        Assert.Empty(processes.Requests);
        Assert.Single(fileSystem.DeletedFiles);
    }

    [Fact]
    public async Task DriverManager_AllowsTrustedExpectedPublisherAndUsesExistingRestorePointAndLaunchFlow()
    {
        var processes = new FakeProcessExecutionService();
        var signature = new FakeAuthenticodeSignatureVerifier
        {
            Result = new AuthenticodeVerificationResult(true, true, "CN=NVIDIA Corporation", 0, null)
        };
        var manager = CreateDriverManager(processExecution: processes, signatureVerifier: signature);
        var gpu = new GpuDriverStatus { Vendor = "NVIDIA", DownloadUrl = "https://example.invalid/driver.exe", Latest = "1.0" };

        var result = await manager.DownloadAndLaunchAsync(gpu, (_, _) => { }, CancellationToken.None);

        Assert.Contains("assinatura NVIDIA válida", result);
        Assert.Equal(2, processes.Requests.Count);
        Assert.Equal("powershell.exe", processes.Requests[0].FileName);
        Assert.EndsWith("driver.exe", processes.Requests[1].FileName);
    }

    [Fact]
    public async Task DriverManager_UsesWindowsUpdateContractForEmptyResultsAndFailures()
    {
        var updates = new FakeWindowsUpdateService();
        var manager = CreateDriverManager(windowsUpdate: updates);
        Assert.Empty(await manager.SearchWindowsUpdateAsync());

        var selected = new WindowsUpdateDriverInfo("selection-token", "Display driver", 1024);
        updates.Results = [selected];
        Assert.Equal(selected, Assert.Single(await manager.SearchWindowsUpdateAsync()));
        updates.SearchFailure = new InvalidOperationException("COM initialization failed");
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.SearchWindowsUpdateAsync());

        updates.SearchFailure = null;
        var progress = new List<string>();
        updates.InstallFailure = new InvalidOperationException("Windows Update communication failed");
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.InstallWindowsUpdateAsync([selected], progress.Add));
        Assert.Equal(new[] { selected }, updates.Installed);
        Assert.Equal("Baixando drivers do Windows Update…", Assert.Single(progress));
    }

    private static DriverManager CreateDriverManager(
        FakeHardwareInventoryService? hardwareInventory = null,
        FakeFileSystemService? fileSystem = null,
        FakeProcessExecutionService? processExecution = null,
        FakeAuthenticodeSignatureVerifier? signatureVerifier = null,
        FakeWindowsUpdateService? windowsUpdate = null) =>
        new(processExecution ?? new FakeProcessExecutionService(), fileSystem ?? new FakeFileSystemService(),
            new FakeDownloadService(), hardwareInventory ?? new FakeHardwareInventoryService(),
            signatureVerifier ?? new FakeAuthenticodeSignatureVerifier(), windowsUpdate ?? new FakeWindowsUpdateService());

    private sealed class FakeProcessManagementService : IProcessManagementService
    {
        public int CurrentSessionId => 4;
        public IReadOnlyList<ManagedProcessInfo> Processes { get; init; } = [];
        public IReadOnlyList<ManagedProcessInfo> EnumerateProcesses() => Processes;
        public IReadOnlyList<ManagedProcessInfo> FindByName(string processName) => Processes.Where(p => p.ProcessName == processName).ToArray();
        public bool IsRunning(string processName) => FindByName(processName).Count > 0;
        public bool RequestClose(ManagedProcessInfo process) => false;
        public bool Terminate(ManagedProcessInfo process, bool entireProcessTree = false) => false;
    }

    private sealed class FakeMemoryManagementService : IMemoryManagementService
    {
        public PhysicalMemoryStatus ReadPhysicalMemoryStatus() => new(0, 0);
        public bool TrimProcessWorkingSet(ManagedProcessInfo process) => false;
        public MemoryOperationResult PurgeStandbyList() => new(false, "not used");
    }

    private sealed class FakeRegistryAccess : IRegistryAccess
    {
        public RegistryAddress? LastWrite { get; private set; }
        public object? LastValue { get; private set; }
        public RegistryDataKind LastKind { get; private set; }
        public bool ArbiterExists { get; init; } = true;
        public object? ReadValue(RegistryAddress address) => null;
        public RegistryValueSnapshot? ReadValueWithKind(RegistryAddress address) => null;
        public bool KeyExists(RegistryKeyAddress address) => ArbiterExists;
        public IReadOnlyList<string> EnumerateSubKeys(RegistryKeyAddress address) => [];
        public IReadOnlyList<RegistryValueSnapshot> EnumerateValues(RegistryKeyAddress address) => [];
        public void CreateKey(RegistryKeyAddress address) { }
        public void WriteValue(RegistryAddress address, object value, RegistryDataKind kind)
        { LastWrite = address; LastValue = value; LastKind = kind; }
        public bool DeleteValue(RegistryAddress address) => false;
        public bool DeleteKeyTree(RegistryKeyAddress address) => false;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }
}
