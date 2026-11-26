using System.ComponentModel;
using System.Diagnostics;
using GeolabTITools.Application.Features.Windows.Contracts;
using GeolabTITools.Application.Features.Windows.DTOs;

namespace ResetExplode;

/// <summary>Programa (agrupado pelo nome do executável) que pode ser fechado para liberar recursos.</summary>
public sealed class BackgroundApp : INotifyPropertyChanged
{
    public string ProcessName { get; init; } = "";
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public long MemoryBytes { get; init; }
    public int Count { get; init; }
    public bool HasWindow { get; init; }
    public bool Suggested { get; init; }
    public string MemoryText => $"{MemoryBytes / 1048576.0:N0} MB";

    private bool _selected;
    public bool Selected
    {
        get => _selected;
        set { _selected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record CleanupResult(long FreedBytes, int Trimmed, bool StandbyPurged, string? StandbyError);
public sealed record CloseResult(List<string> Closed, List<string> NotClosed);

/// <summary>Limpeza de memória e fechamento de programas em segundo plano.</summary>
public sealed class Optimizer(IProcessManagementService processes, IMemoryManagementService memory)
{
    private readonly IProcessManagementService _processes = processes;
    private readonly IMemoryManagementService _memory = memory;
    /// <summary>Processos que nunca aparecem na lista (sistema, segurança, drivers, o próprio app).</summary>
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "lsaiso", "svchost",
        "dwm", "explorer", "fontdrvhost", "sihost", "ctfmon", "taskhostw", "RuntimeBroker", "StartMenuExperienceHost",
        "ShellExperienceHost", "SearchHost", "SearchIndexer", "TextInputHost", "SecurityHealthService", "SecurityHealthSystray",
        "MsMpEng", "NisSrv", "SgrmBroker", "audiodg", "conhost", "dllhost", "spoolsv", "WmiPrvSE", "LockApp", "ApplicationFrameHost",
        "SystemSettings", "smartscreen", "MemCompression", "Secure System", "WUDFHost", "dasHost", "nvcontainer", "NVDisplay.Container",
        "atiesrxx", "atieclxx", "amdfendrsr", "RadeonSoftware", "igfxEM", "PawnIO", "ResetExplode", "GeolabTITools", "cmd", "powershell", "WindowsTerminal",
        "OpenConsole", "devenv", "Code", "dotnet",
    };

    /// <summary>Processos de segundo plano seguros de fechar durante o jogo (pré-marcados).</summary>
    private static readonly Dictionary<string, string> SuggestedApps = new(StringComparer.OrdinalIgnoreCase)
    {
        ["OneDrive"] = "Sincronização do OneDrive (pode usar disco e rede)",
        ["ms-teams"] = "Microsoft Teams",
        ["Teams"] = "Microsoft Teams (clássico)",
        ["Skype"] = "Skype",
        ["PhoneExperienceHost"] = "Vincular ao Celular",
        ["YourPhone"] = "Vincular ao Celular",
        ["Widgets"] = "Widgets do Windows",
        ["WidgetService"] = "Serviço de Widgets",
        ["AdobeARM"] = "Atualizador da Adobe",
        ["CCXProcess"] = "Adobe Creative Cloud",
        ["Creative Cloud"] = "Adobe Creative Cloud",
        ["AdobeIPCBroker"] = "Adobe (comunicação em segundo plano)",
        ["GoogleDriveFS"] = "Google Drive",
        ["Dropbox"] = "Dropbox",
        ["Cortana"] = "Cortana",
        ["OfficeClickToRun"] = "Atualizador do Office",
    };

    public List<BackgroundApp> ListApps()
    {
        int session = _processes.CurrentSessionId;
        var groups = new Dictionary<string, List<ManagedProcessInfo>>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in _processes.EnumerateProcesses())
        {
            try
            {
                if (p.SessionId != session || Protected.Contains(p.ProcessName)) continue;
                if (!groups.TryGetValue(p.ProcessName, out var list)) groups[p.ProcessName] = list = new List<ManagedProcessInfo>();
                list.Add(p);
            }
            catch { }
        }

        var result = new List<BackgroundApp>();
        foreach (var (name, procs) in groups)
        {
            bool hasWindow = procs.Any(p => p.HasMainWindow);
            bool suggested = SuggestedApps.ContainsKey(name);
            if (!hasWindow && !suggested) continue; // processos auxiliares sem janela ficam de fora

            string title = name, detail;
            title = procs[0].Description is { Length: > 0 } description ? description : name;
            detail = suggested ? SuggestedApps[name] : hasWindow ? "Programa aberto" : "";
            if (procs.Count > 1) detail += $" · {procs.Count} processos";
            long mem = procs.Sum(p => p.WorkingSetBytes);

            result.Add(new BackgroundApp
            {
                ProcessName = name, Title = title, Detail = detail, MemoryBytes = mem, Count = procs.Count,
                HasWindow = hasWindow, Suggested = suggested, Selected = suggested
            });
        }
        return result.OrderByDescending(a => a.Suggested).ThenByDescending(a => a.MemoryBytes).ToList();
    }

    /// <summary>
    /// Fecha os programas: com janela → pede para fechar (como clicar no X), sem forçar, para não perder nada
    /// não salvo; sem janela (segundo plano) → encerra.
    /// </summary>
    public CloseResult CloseApps(IEnumerable<BackgroundApp> apps)
    {
        var closed = new List<string>();
        var notClosed = new List<string>();
        foreach (var app in apps)
        {
            var procs = _processes.FindByName(app.ProcessName);
            foreach (var p in procs)
            {
                if (p.HasMainWindow) _processes.RequestClose(p);
                else if (app.Suggested) _processes.Terminate(p);
            }
            // Dá até 4 s para fecharem sozinhos.
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < 4 && _processes.IsRunning(app.ProcessName)) Thread.Sleep(200);
            if (!_processes.IsRunning(app.ProcessName)) closed.Add(app.Title);
            else notClosed.Add(app.Title);
        }
        return new CloseResult(closed, notClosed);
    }

    /// <summary>
    /// Esvazia a lista de espera (standby) da RAM, como o ISLC, e enxuga o working set dos processos.
    /// A lista de espera é cache que o Windows libera sozinho quando precisa; esvaziá-la antes de jogar
    /// ajuda quando a RAM está quase cheia e o jogo engasga ao carregar áreas novas.
    /// </summary>
    public CleanupResult CleanMemory()
    {
        long before = _memory.ReadPhysicalMemoryStatus().AvailableBytes;
        int trimmed = 0;
        int self = Environment.ProcessId;
        foreach (var p in _processes.EnumerateProcesses())
        {
            if (p.Id == self || p.Id <= 4) continue;
            if (_memory.TrimProcessWorkingSet(p)) trimmed++;
        }

        MemoryOperationResult purge = _memory.PurgeStandbyList();

        Thread.Sleep(300);
        return new CleanupResult(Math.Max(0, _memory.ReadPhysicalMemoryStatus().AvailableBytes - before),
            trimmed, purge.Succeeded, purge.Error);
    }

    public (long Total, long Available) MemoryStatus()
    {
        var status = _memory.ReadPhysicalMemoryStatus();
        return (status.TotalBytes, status.AvailableBytes);
    }

}
