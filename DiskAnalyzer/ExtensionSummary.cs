namespace ResetExplode.DiskAnalyzer;

/// <summary>Contagem e tamanho acumulado de arquivos de uma extensão.</summary>
public sealed class ExtensionSummary
{
    public string Extension { get; init; } = "";
    public int FileCount { get; internal set; }
    public long TotalBytes { get; internal set; }
    public double PercentOfAnalyzedSpace { get; internal set; }
    public string FileCountText => $"{FileCount:N0} arquivo(s)";
    public string TotalBytesText => DiskDriveOption.FormatBytes(TotalBytes);
    public string PercentText => $"{PercentOfAnalyzedSpace:0.##}%";
}
