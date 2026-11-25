using System.IO;
using System.Text.Json;

namespace ResetExplode;

public sealed record BenchRun(DateTime Date, double? Multi, double? Single, bool Sustained, string Cpu);

/// <summary>Resultado dos benchmarks adicionais (Test = "gpu", "sim" ou "mem"). Score é usado para a barra do histórico.</summary>
public sealed record BenchRecord(DateTime Date, string Test, double Score, string Summary);

public sealed class AppSettings
{
    public static string Folder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GEOLAB TI TOOLS");
    private static string LegacyFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ResetExplode");
    private static string FilePath => Path.Combine(Folder, "settings.json");

    // GEOLAB BENCH: últimos resultados (mais recente primeiro)
    public List<BenchRun> BenchRuns { get; set; } = new();
    public List<BenchRecord> BenchRecords { get; set; } = new();

    // GEOLAB OTIMIZA: valores originais dos ajustes do Windows antes da primeira alteração ("<ausente>" = não existia)
    public Dictionary<string, string?> TweakBackup { get; set; } = new();
    /// <summary>ComDB original (reservas de números COM) antes da primeira "liberação".</summary>
    public byte[]? ComDbBackup { get; set; }
    /// <summary>Programas de inicialização removidos, para "Restaurar removidos".</summary>
    public List<StartupBackup> StartupRemoved { get; set; } = new();

    // Aparência
    public bool LightTheme { get; set; }
    public bool HeroBarCollapsed { get; set; }

    public static AppSettings Load()
    {
        try
        {
            MigrateLegacyData();
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                if (json.Contains("HowManyFpsKeyProtected", StringComparison.Ordinal) ||
                    json.Contains("GameProfiles", StringComparison.Ordinal) ||
                    json.Contains("CustomGames", StringComparison.Ordinal) ||
                    json.Contains("OverlayItems", StringComparison.Ordinal) ||
                    json.Contains("OverlayCorner", StringComparison.Ordinal) ||
                    json.Contains("OverlayScale", StringComparison.Ordinal))
                {
                    try { settings.Save(); } catch { }
                    try { File.Delete(Path.Combine(Folder, "fps-cache.json")); } catch { }
                }
                return settings;
            }
        }
        catch { }
        return new AppSettings();
    }

    private static void MigrateLegacyData()
    {
        try
        {
            string marker = Path.Combine(Folder, ".legacy-data-migrated");
            if (File.Exists(marker) || !Directory.Exists(LegacyFolder)) return;
            foreach (string source in Directory.EnumerateFiles(LegacyFolder, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(LegacyFolder, source);
                string destination = Path.Combine(Folder, relative);
                if (File.Exists(destination)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: false);
            }
            Directory.CreateDirectory(Folder);
            File.WriteAllText(marker, "Migrated");
        }
        catch { }
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
