using System.Collections.ObjectModel;
using GeolabTITools.Application.Features.SystemInfo.Contracts;
using GeolabTITools.Application.Features.SystemInfo.DTOs;
using ResetExplode.Presentation.Common;
using ResetExplode.Presentation.Services;

namespace ResetExplode.Presentation.ViewModels.System;

public sealed class SystemViewModel : ObservableObject
{
    private readonly ISystemInfoService _systemInfo;
    private readonly IDriveMapReader _driveMapReader;
    private InfoNode? _selectedCategory;
    private IReadOnlyList<InfoNode> _selectedDetails = Array.Empty<InfoNode>();
    private string _statusText = "Lendo informações do sistema…";
    private string _drivesSummary = "";

    public SystemViewModel(ISystemInfoService systemInfo, IDriveMapReader driveMapReader)
    {
        _systemInfo = systemInfo;
        _driveMapReader = driveMapReader;
    }

    public ObservableCollection<InfoNode> Categories { get; } = new();
    public ObservableCollection<DriveCard> Drives { get; } = new();

    public InfoNode? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (ReferenceEquals(_selectedCategory, value)) return;
            _selectedCategory = value;
            OnPropertyChanged();
            SelectedDetails = value == null ? Array.Empty<InfoNode>() : value.Children.ToList();
            OnPropertyChanged(nameof(IsStorageSelected));
            if (IsStorageSelected) _ = RefreshDrivesAsync();
        }
    }

    public IReadOnlyList<InfoNode> SelectedDetails
    {
        get => _selectedDetails;
        private set { _selectedDetails = value; OnPropertyChanged(); }
    }

    public bool IsStorageSelected => SelectedCategory?.Header == "Armazenamento";

    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public string DrivesSummary
    {
        get => _drivesSummary;
        private set { _drivesSummary = value; OnPropertyChanged(); }
    }

    public async Task LoadAsync()
    {
        try
        {
            var categories = await Task.Run(_systemInfo.Collect);
            Categories.Clear();
            foreach (var category in categories) Categories.Add(category);
            StatusText = $"{Environment.MachineName} · {categories.Count} categorias";
            SelectedCategory = Categories.FirstOrDefault();
            await RefreshDrivesAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Não foi possível ler as informações do sistema: {ex.Message}";
        }
    }

    public Task<string> ReadCpuNameAsync()
        => Task.Run(_systemInfo.ReadCpuName);

    public async Task<IReadOnlyList<DriveCard>> RefreshDrivesAsync()
    {
        var drives = await _driveMapReader.ReadAsync();
        Drives.Clear();
        foreach (var drive in drives) Drives.Add(drive);
        long total = drives.Sum(d => d.Total), free = drives.Sum(d => d.Free);
        DrivesSummary = $"{drives.Count} unidades · {DriveCard.Fmt(free)} livres de {DriveCard.Fmt(total)}";
        return drives;
    }

    public async Task RefreshDiskInventoryAsync()
    {
        var storage = Categories.FirstOrDefault(category => category.Header == "Armazenamento");
        if (storage == null) return;
        await Task.Run(() => _systemInfo.RefreshDisks(storage));
        if (ReferenceEquals(SelectedCategory, storage))
            SelectedDetails = storage.Children.ToList();
    }
}
