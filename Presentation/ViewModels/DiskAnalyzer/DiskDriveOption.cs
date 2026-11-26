using System.ComponentModel;
using System.Runtime.CompilerServices;
using GeolabTITools.Application.Features.Disks.Services;

namespace ResetExplode.DiskAnalyzer;

/// <summary>Volume lógico pronto para leitura, com informações de uso e do disco físico associado.</summary>
public sealed class DiskDriveOption : INotifyPropertyChanged
{
    private bool _isSelected;

    public string RootPath { get; init; } = "";
    public string DriveLetter { get; init; } = "";
    public string Label { get; init; } = "";
    public string DriveKind { get; init; } = "Unidade";
    public string FileSystem { get; init; } = "Indisponível";
    public long TotalBytes { get; init; }
    public long UsedBytes { get; init; }
    public long FreeBytes { get; init; }
    public double UsagePercent => TotalBytes > 0 ? Math.Clamp(UsedBytes * 100d / TotalBytes, 0, 100) : 0;
    public double UsageFraction => UsagePercent / 100d;
    public string UsagePercentText => $"{UsagePercent:0.#}% utilizado";
    public string CapacityText => FormatBytes(TotalBytes);
    public string UsedText => $"{FormatBytes(UsedBytes)} usados";
    public string FreeText => $"{FormatBytes(FreeBytes)} livres";
    public string DisplayName => $"{DriveLetter}\\  ·  {Label}  ·  {CapacityText}";

    public bool IsSelected
    {
        get => _isSelected;
        private set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    internal void SetSelected(bool selected) => IsSelected = selected;

    public static string FormatBytes(long bytes) => DiskFormatting.FormatBytes(bytes);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
