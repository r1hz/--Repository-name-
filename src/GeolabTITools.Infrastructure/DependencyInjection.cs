using GeolabTITools.Application.Features.Disks.Contracts;
using GeolabTITools.Infrastructure.FileSystem.FileOperations;
using GeolabTITools.Infrastructure.FileSystem;
using GeolabTITools.Infrastructure.FileSystem.DiskServices;
using GeolabTITools.Application.Features.SystemInfo.Contracts;
using GeolabTITools.Infrastructure.Hardware.SystemInfo;
using GeolabTITools.Application.Features.Windows.Contracts;
using GeolabTITools.Infrastructure.Windows;
using GeolabTITools.Infrastructure.Windows.Devices;
using GeolabTITools.Infrastructure.Windows.Security;
using GeolabTITools.Infrastructure.Windows.Updates;
using GeolabTITools.Infrastructure.Windows.Wmi;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;

namespace GeolabTITools.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddGeolabInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IDiskInfoService, DiskInfoService>();
        services.AddSingleton<IDiskScannerService, DiskScannerService>();
        services.AddSingleton<IDiskFileOperations, DiskFileOperations>();
        services.AddSingleton<ISystemInfoService, SystemInfoCollector>();
        services.AddSingleton<IProcessExecutionService, ProcessExecutionService>();
        services.AddSingleton<IRegistryAccess, RegistryAccess>();
        services.AddSingleton<IFileSystemService, WindowsFileSystemService>();
        services.AddSingleton(_ =>
        {
            var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) GeolabTITools");
            return client;
        });
        services.AddSingleton<IDownloadService, HttpDownloadService>();
        services.AddSingleton<IShortcutReader, WindowsShortcutReader>();
        services.AddSingleton<IFileMetadataService, WindowsFileMetadataService>();
        services.AddSingleton<IProcessManagementService, WindowsProcessManagementService>();
        services.AddSingleton<IMemoryManagementService, WindowsMemoryManagementService>();
        services.AddSingleton<IMouseSettingsService, WindowsMouseSettingsService>();
        services.AddSingleton<WindowsUsbDeviceService>();
        services.AddSingleton<IUsbDeviceService>(provider => provider.GetRequiredService<WindowsUsbDeviceService>());
        services.AddSingleton<IComPortReservationService>(provider => provider.GetRequiredService<WindowsUsbDeviceService>());
        services.AddSingleton<IHardwareInventoryService, WmiHardwareInventoryService>();
        services.AddSingleton<IAuthenticodeSignatureVerifier, WindowsAuthenticodeSignatureVerifier>();
        services.AddSingleton<IWindowsUpdateService, WindowsUpdateDriverService>();
        return services;
    }
}
