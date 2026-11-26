using GeolabTITools.Application.Features.Disks.Contracts;
using GeolabTITools.Infrastructure.FileSystem.FileOperations;
using GeolabTITools.Infrastructure.FileSystem.DiskServices;
using GeolabTITools.Application.Features.SystemInfo.Contracts;
using GeolabTITools.Infrastructure.Hardware.SystemInfo;
using GeolabTITools.Application.Features.Windows.Contracts;
using GeolabTITools.Infrastructure.Windows;
using Microsoft.Extensions.DependencyInjection;

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
        return services;
    }
}
