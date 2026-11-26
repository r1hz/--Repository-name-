using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using GeolabTITools.Infrastructure;
using GeolabTITools.Application.Features.Disks.Contracts;
using GeolabTITools.Application.Features.Disks.Services;
using Microsoft.Extensions.DependencyInjection;
using ResetExplode.DiskAnalyzer;
using ResetExplode.Presentation.Services;
using ResetExplode.Presentation.ViewModels.System;

namespace ResetExplode;

public partial class App : Application
{
    // Uma única instância: abrir o GEOLAB TI TOOLS de novo só traz a janela que já está aberta para a frente.
    private static Mutex? _single;
    private ServiceProvider? _services;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? cls, string title);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);

    protected override void OnStartup(StartupEventArgs e)
    {
        bool first;
        try { _single = new Mutex(true, @"Local\ResetExplode.SingleInstance", out first); }
        catch (UnauthorizedAccessException) { first = false; } // criado por outra instância com outro nível de permissão

        if (!first)
        {
            IntPtr h = FindWindow(null, "GEOLAB TI TOOLS");
            if (h != IntPtr.Zero)
            {
                if (IsIconic(h)) ShowWindow(h, 9); // SW_RESTORE
                SetForegroundWindow(h);
            }
            Shutdown();
            return;
        }
        base.OnStartup(e);
        var registrations = new ServiceCollection();
        registrations.AddGeolabInfrastructure();
        registrations.AddSingleton<IDiskFileQueryService, DiskFileQueryService>();
        registrations.AddSingleton<IDiskClipboardService, WpfDiskClipboardService>();
        registrations.AddSingleton<IDriveMapReader, DriveMapReader>();
        registrations.AddTransient<SystemViewModel>();
        registrations.AddTransient<DiskAnalyzerViewModel>();
        registrations.AddSingleton<MainWindow>();
        _services = registrations.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        _services.GetRequiredService<MainWindow>().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
