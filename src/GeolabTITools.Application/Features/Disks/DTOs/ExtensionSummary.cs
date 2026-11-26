using GeolabTITools.Application.Features.Disks.Services;

namespace GeolabTITools.Application.Features.Disks.DTOs;

/// <summary>Contagem e tamanho acumulado de arquivos de uma extensão.</summary>
public sealed class ExtensionSummary
{
    public string Extension { get; init; } = "";
    public int FileCount { get; set; }
    public long TotalBytes { get; set; }
    public double PercentOfAnalyzedSpace { get; set; }
    public string FileCountText => $"{FileCount:N0} arquivo(s)";
    public string TotalBytesText => DiskFormatting.FormatBytes(TotalBytes);
    public string PercentText => $"{PercentOfAnalyzedSpace:0.##}%";
}
