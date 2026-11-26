using System.ComponentModel;
using GeolabTITools.Application.Features.Windows.Contracts;
using GeolabTITools.Application.Features.Windows.DTOs;

namespace ResetExplode;

/// <summary>
/// Ajuste do Windows para jogos: lê o estado atual, aplica e guarda o valor original (em AppSettings.TweakBackup)
/// na primeira vez que é alterado, para "Restaurar originais" desfazer tudo.
/// Só ajustes documentados e reversíveis; nada que reduza a segurança (Defender, VBS/HVCI, UAC, Windows Update).
/// </summary>
public sealed class Tweak : INotifyPropertyChanged
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string Effect { get; init; } = "";
    public bool NeedsRestart { get; init; }
    internal Func<bool?> Read { get; init; } = () => null;
    internal Action<bool, AppSettings> Write { get; init; } = (_, _) => { };
    internal Action<AppSettings> Restore { get; init; } = _ => { };

    private bool _isOn;
    public bool IsOn { get => _isOn; set { _isOn = value; Changed(nameof(IsOn)); } }
    private string _status = "";
    public string Status { get => _status; set { _status = value; Changed(nameof(Status)); } }
    public string Badge => NeedsRestart ? "REQUER REINICIAR" : "";

    public void Refresh() => IsOn = Read() ?? false;

    private void Changed(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class WindowsTweaks(
    IRegistryAccess registry,
    IProcessExecutionService processes,
    IMouseSettingsService mouseSettings)
{
    private const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private readonly IRegistryAccess _registry = registry;
    private readonly IProcessExecutionService _processes = processes;
    private readonly IMouseSettingsService _mouseSettings = mouseSettings;

    public List<Tweak> All() => new()
    {
        new Tweak
        {
            Id = "power", Title = "Plano de energia: Alto desempenho",
            Description = "Mantém a CPU pronta para subir o clock na hora, sem economia agressiva de energia.",
            Effect = "Menos engasgos e 1% low melhor em alguns jogos. Em notebooks gasta mais bateria.",
            Read = () => ActiveScheme()?.Equals(HighPerformance, StringComparison.OrdinalIgnoreCase),
            Write = (on, s) =>
            {
                if (!s.TweakBackup.ContainsKey("power")) s.TweakBackup["power"] = ActiveScheme();
                if (on)
                {
                    // Alguns PCs vêm sem o plano listado: duplicar o modelo padrão o recria com o mesmo GUID.
                    if (!PowerCfg("/list").Contains(HighPerformance, StringComparison.OrdinalIgnoreCase))
                        PowerCfg($"-duplicatescheme {HighPerformance} {HighPerformance}");
                    PowerCfg($"/setactive {HighPerformance}");
                }
                else if (s.TweakBackup["power"] is string prev && !prev.Equals(HighPerformance, StringComparison.OrdinalIgnoreCase))
                    PowerCfg($"/setactive {prev}");
                else PowerCfg("/setactive 381b4222-f694-41f0-9685-ff5bb260df2e"); // Equilibrado
            },
            Restore = s => { if (s.TweakBackup.GetValueOrDefault("power") is string prev) PowerCfg($"/setactive {prev}"); },
        },
        RegTweak("gamemode", "Modo de Jogo",
            "Dá prioridade ao jogo e evita que o Windows Update instale ou reinicie durante a partida.",
            "Recomendado ligado (é o padrão do Windows 11).",
            RegistryRoot.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", on: 1, off: 0, absentMeansOn: true),
        new Tweak
        {
            Id = "gamedvr", Title = "Desativar gravação em segundo plano (Game DVR)",
            Description = "Desliga a captura contínua do Xbox Game Bar, que grava os últimos minutos de jogo o tempo todo.",
            Effect = "Recupera alguns FPS e reduz uso de GPU/disco. Você perde o \"gravar o que aconteceu\" do Game Bar.",
            Read = () => (ReadDword(RegistryRoot.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled") ?? 1) == 0 &&
                         (ReadDword(RegistryRoot.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled") ?? 1) == 0,
            Write = (on, s) =>
            {
                SetDword(s, "gamedvr.a", RegistryRoot.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", on ? 0 : 1);
                SetDword(s, "gamedvr.b", RegistryRoot.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", on ? 0 : 1);
            },
            Restore = s =>
            {
                RestoreDword(s, "gamedvr.a", RegistryRoot.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled");
                RestoreDword(s, "gamedvr.b", RegistryRoot.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled");
            },
        },
        RegTweak("hags", "Agendamento de GPU acelerado por hardware",
            "A própria placa de vídeo gerencia sua fila de trabalho, em vez da CPU.",
            "Necessário para o Frame Generation do DLSS 3/4. Pode reduzir latência. Requer GPU e driver compatíveis.",
            RegistryRoot.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", on: 2, off: 1,
            absentMeansOn: false, needsRestart: true),
        new Tweak
        {
            Id = "windowed", Title = "Otimizações para jogos em janela",
            Description = "Faz jogos DirectX 10/11 em janela ou \"sem bordas\" usarem o modelo de apresentação moderno (flip).",
            Effect = "Menor latência e suporte a VRR/G-Sync em janela. Recurso do Windows 11.",
            Read = () => DxSettings().GetValueOrDefault("SwapEffectUpgradeEnable") == "1",
            Write = (on, s) =>
            {
                if (!s.TweakBackup.ContainsKey("windowed"))
                    s.TweakBackup["windowed"] = ReadString(RegistryRoot.CurrentUser, DxPath, "DirectXUserGlobalSettings") ?? "<ausente>";
                var d = DxSettings();
                d["SwapEffectUpgradeEnable"] = on ? "1" : "0";
                _registry.WriteValue(Address(RegistryRoot.CurrentUser, DxPath, "DirectXUserGlobalSettings"),
                    string.Concat(d.Select(kv => $"{kv.Key}={kv.Value};")), RegistryDataKind.String);
            },
            Restore = s =>
            {
                if (s.TweakBackup.GetValueOrDefault("windowed") is not string prev) return;
                var address = Address(RegistryRoot.CurrentUser, DxPath, "DirectXUserGlobalSettings");
                if (prev == "<ausente>") _registry.DeleteValue(address);
                else _registry.WriteValue(address, prev, RegistryDataKind.String);
            },
        },
        new Tweak
        {
            Id = "mouse", Title = "Desligar aceleração do mouse",
            Description = "Desmarca \"Aprimorar precisão do ponteiro\": o cursor anda sempre a mesma distância para o mesmo movimento.",
            Effect = "Não muda FPS, mas deixa a mira consistente em jogos de tiro.",
            Read = () => ReadString(RegistryRoot.CurrentUser, @"Control Panel\Mouse", "MouseSpeed") == "0",
            Write = (on, s) =>
            {
                foreach (var n in new[] { "MouseSpeed", "MouseThreshold1", "MouseThreshold2" })
                    if (!s.TweakBackup.ContainsKey($"mouse.{n}")) s.TweakBackup[$"mouse.{n}"] = ReadString(RegistryRoot.CurrentUser, @"Control Panel\Mouse", n);
                var v = on ? new[] { "0", "0", "0" } : new[] { "1", "6", "10" }; // padrão do Windows
                ApplyMouse(v);
            },
            Restore = s =>
            {
                if (!s.TweakBackup.ContainsKey("mouse.MouseSpeed")) return;
                ApplyMouse(new[] { "MouseSpeed", "MouseThreshold1", "MouseThreshold2" }
                    .Select(n => s.TweakBackup.GetValueOrDefault($"mouse.{n}") ?? "0").ToArray());
            },
        },
    };

    /// <summary>Desfaz tudo que foi alterado pelo app (valores guardados antes da primeira alteração).</summary>
    public void RestoreAll(IEnumerable<Tweak> tweaks, AppSettings s)
    {
        foreach (var t in tweaks)
            try { t.Restore(s); } catch { }
        s.TweakBackup.Clear();
        s.Save();
    }

    // ------------------------------------------------------------------ registro

    private Tweak RegTweak(string id, string title, string desc, string effect, RegistryRoot hive, string path, string name,
                                  int on, int off, bool absentMeansOn, bool needsRestart = false) => new()
    {
        Id = id, Title = title, Description = desc, Effect = effect, NeedsRestart = needsRestart,
        Read = () => (ReadDword(hive, path, name) is int v) ? v == on : absentMeansOn,
        Write = (enable, s) => SetDword(s, id, hive, path, name, enable ? on : off),
        Restore = s => RestoreDword(s, id, hive, path, name),
    };

    private int? ReadDword(RegistryRoot hive, string path, string name)
    {
        return _registry.ReadValue(Address(hive, path, name)) is int value ? value : null;
    }

    private string? ReadString(RegistryRoot hive, string path, string name) =>
        _registry.ReadValue(Address(hive, path, name))?.ToString();

    private void SetDword(AppSettings s, string backupKey, RegistryRoot hive, string path, string name, int value)
    {
        if (!s.TweakBackup.ContainsKey(backupKey))
            s.TweakBackup[backupKey] = ReadDword(hive, path, name)?.ToString() ?? "<ausente>";
        _registry.WriteValue(Address(hive, path, name), value, RegistryDataKind.DWord);
    }

    private void RestoreDword(AppSettings s, string backupKey, RegistryRoot hive, string path, string name)
    {
        if (s.TweakBackup.GetValueOrDefault(backupKey) is not string prev) return;
        var address = Address(hive, path, name);
        if (prev == "<ausente>") _registry.DeleteValue(address);
        else _registry.WriteValue(address, int.Parse(prev), RegistryDataKind.DWord);
    }

    private const string DxPath = @"Software\Microsoft\DirectX\UserGpuPreferences";

    /// <summary>"Chave=Valor;Chave=Valor;" → dicionário (o valor pode ter outras opções do usuário, que preservamos).</summary>
    private Dictionary<string, string> DxSettings()
    {
        var d = new Dictionary<string, string>();
        string raw = ReadString(RegistryRoot.CurrentUser, DxPath, "DirectXUserGlobalSettings") ?? "";
        foreach (var part in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2) d[kv[0].Trim()] = kv[1].Trim();
        }
        return d;
    }

    private void ApplyMouse(string[] v)
    {
        _registry.WriteValue(Address(RegistryRoot.CurrentUser, @"Control Panel\Mouse", "MouseSpeed"), v[0], RegistryDataKind.String);
        _registry.WriteValue(Address(RegistryRoot.CurrentUser, @"Control Panel\Mouse", "MouseThreshold1"), v[1], RegistryDataKind.String);
        _registry.WriteValue(Address(RegistryRoot.CurrentUser, @"Control Panel\Mouse", "MouseThreshold2"), v[2], RegistryDataKind.String);
        // Aplica na hora, sem precisar sair da conta.
        _mouseSettings.ApplyMouseParameters(int.Parse(v[0]), int.Parse(v[1]), int.Parse(v[2]));
    }

    // ------------------------------------------------------------------ plano de energia

    private string? ActiveScheme()
    {
        var m = System.Text.RegularExpressions.Regex.Match(PowerCfg("/getactivescheme"), @"[0-9a-fA-F\-]{36}");
        return m.Success ? m.Value.ToLowerInvariant() : null;
    }

    private string PowerCfg(string args)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = _processes.ExecuteAsync(new ProcessExecutionRequest(
            "powercfg.exe", args.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            WaitForExit: true, CaptureOutput: true), timeout.Token).GetAwaiter().GetResult();
        if (!result.Started) throw new InvalidOperationException("Não foi possível iniciar powercfg.exe.");
        return result.StandardOutput;
    }

    private static RegistryAddress Address(RegistryRoot root, string path, string name) => new(root, path, name);

}
