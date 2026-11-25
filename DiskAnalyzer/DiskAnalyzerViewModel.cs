using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace ResetExplode.DiskAnalyzer;

/// <summary>Estado, progresso e comandos de análise do Analisador de Disco.</summary>
public sealed class DiskAnalyzerViewModel : INotifyPropertyChanged
{
    private readonly DiskInfoService _diskInfo;
    private readonly DiskScannerService _scanner;
    private DiskDriveOption? _selectedDrive;
    private DiskAnalysisResult? _analysisResult;
    private IReadOnlyList<FileEntry> _visibleFiles = Array.Empty<FileEntry>();
    private FileEntry? _selectedFile;
    private FolderEntry? _selectedFolder;
    private FolderEntry? _treemapFolder;
    private IReadOnlyList<TreemapTile> _treemapTiles = Array.Empty<TreemapTile>();
    private CancellationTokenSource? _treemapBuildCancellation;
    private CancellationTokenSource? _fileQueryCancellation;
    private CancellationTokenSource? _scanCancellation;
    private DiskScanControl? _scanControl;
    private string _status = "Carregando unidades…";
    private string _currentDirectory = "—";
    private int _filesScanned;
    private int _foldersScanned;
    private long _bytesScanned;
    private TimeSpan _elapsed;
    private bool _isRefreshing;
    private bool _isScanning;
    private bool _isPaused;
    private string _searchText = "";

    public DiskAnalyzerViewModel(DiskInfoService diskInfo, DiskScannerService scanner)
    {
        _diskInfo = diskInfo;
        _scanner = scanner;
        Drives = new ObservableCollection<DiskDriveOption>();
        ExtensionFilters = new ObservableCollection<DiskAnalyzerChoice>();
        SortOptions = new ObservableCollection<DiskAnalyzerChoice>();
        AddChoice(ExtensionFilters, "*", "Todas", true);
        foreach (string extension in new[] { ".exe", ".dll", ".iso", ".zip", ".rar", ".mp4", ".jpg", ".png", ".pdf", ".docx" })
            AddChoice(ExtensionFilters, extension, extension.ToUpperInvariant());
        AddChoice(SortOptions, "size-desc", "Maior tamanho", true);
        AddChoice(SortOptions, "size-asc", "Menor tamanho");
        AddChoice(SortOptions, "name", "Nome");
        AddChoice(SortOptions, "extension", "Extensão");
        AddChoice(SortOptions, "date", "Data de modificação");
        RefreshDrivesCommand = new RelayCommand(_ => { _ = RefreshDrivesAsync(); }, _ => !IsRefreshing && !IsScanning);
        SelectDriveCommand = new RelayCommand(parameter =>
        {
            if (parameter is DiskDriveOption drive) SelectDrive(drive);
        }, parameter => parameter is DiskDriveOption && !IsScanning);
        StartAnalysisCommand = new RelayCommand(_ => { _ = StartAnalysisAsync(); }, _ => SelectedDrive is not null && !IsScanning && !IsRefreshing);
        PauseAnalysisCommand = new RelayCommand(_ => PauseAnalysis(), _ => IsScanning && !IsPaused);
        ResumeAnalysisCommand = new RelayCommand(_ => ResumeAnalysis(), _ => IsScanning && IsPaused);
        CancelAnalysisCommand = new RelayCommand(_ => CancelAnalysis(), _ => IsScanning);
        OpenSelectedFolderCommand = new RelayCommand(_ => OpenSelectedFolder(), _ => SelectedFolder is not null || AnalysisResult?.RootFolder is not null);
        OpenFileLocationCommand = new RelayCommand(_ => OpenFileLocation(), _ => SelectedFile is not null);
        OpenFileFolderCommand = new RelayCommand(_ => OpenFileFolder(), _ => SelectedFile is not null);
        CopyFilePathCommand = new RelayCommand(_ => CopyFilePath(), _ => SelectedFile is not null);
        GoUpTreemapCommand = new RelayCommand(_ => GoUpTreemap(), _ => TreemapFolder?.Parent is not null);
    }

    public ObservableCollection<DiskDriveOption> Drives { get; }
    public ICommand RefreshDrivesCommand { get; }
    public ICommand SelectDriveCommand { get; }
    public ICommand StartAnalysisCommand { get; }
    public ICommand PauseAnalysisCommand { get; }
    public ICommand ResumeAnalysisCommand { get; }
    public ICommand CancelAnalysisCommand { get; }
    public ICommand OpenSelectedFolderCommand { get; }
    public ICommand OpenFileLocationCommand { get; }
    public ICommand OpenFileFolderCommand { get; }
    public ICommand CopyFilePathCommand { get; }
    public ICommand GoUpTreemapCommand { get; }
    public ObservableCollection<DiskAnalyzerChoice> ExtensionFilters { get; }
    public ObservableCollection<DiskAnalyzerChoice> SortOptions { get; }

    public IReadOnlyList<FolderEntry> FolderRoots => AnalysisResult?.RootFolder is { } root
        ? new[] { root } : Array.Empty<FolderEntry>();

    public IReadOnlyList<FileEntry> VisibleFiles
    {
        get => _visibleFiles;
        private set
        {
            _visibleFiles = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VisibleFilesCountText));
            if (SelectedFile is not null) SelectedFile = null;
        }
    }

    public string VisibleFilesCountText => $"{VisibleFiles.Count:N0} arquivo(s) exibido(s)";

    public string SearchText
    {
        get => _searchText;
        set { if (_searchText == value) return; _searchText = value; OnPropertyChanged(); ScheduleFileRefresh(); }
    }

    public FileEntry? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (ReferenceEquals(_selectedFile, value)) return;
            _selectedFile = value;
            OnPropertyChanged();
            CommandCanExecuteChanged();
        }
    }

    public FolderEntry? SelectedFolder
    {
        get => _selectedFolder;
        private set
        {
            if (ReferenceEquals(_selectedFolder, value)) return;
            _selectedFolder = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedFolderName));
            OnPropertyChanged(nameof(SelectedFolderSizeText));
            OnPropertyChanged(nameof(SelectedFolderFileCountText));
            OnPropertyChanged(nameof(SelectedFolderSubfolderCountText));
            ScheduleFileRefresh();
            CommandCanExecuteChanged();
        }
    }

    public string SelectedFolderName => SelectedFolder?.FullPath ?? "Nenhuma pasta selecionada";
    public string SelectedFolderSizeText => SelectedFolder?.TotalSizeText ?? "—";
    public string SelectedFolderFileCountText => SelectedFolder is null ? "—" : $"{SelectedFolder.FileCount:N0}";
    public string SelectedFolderSubfolderCountText => SelectedFolder is null ? "—" : $"{SelectedFolder.SubfolderCount:N0}";

    public FolderEntry? TreemapFolder => _treemapFolder;
    public IReadOnlyList<TreemapTile> TreemapTiles => _treemapTiles;
    public string TreemapFolderPath => TreemapFolder?.FullPath ?? "Aguardando uma análise concluída";
    public bool CanGoUpTreemap => TreemapFolder?.Parent is not null;

    public DiskDriveOption? SelectedDrive
    {
        get => _selectedDrive;
        private set
        {
            if (ReferenceEquals(_selectedDrive, value)) return;
            _selectedDrive?.SetSelected(false);
            _selectedDrive = value;
            _selectedDrive?.SetSelected(true);
            OnPropertyChanged();
            CommandCanExecuteChanged();
        }
    }

    public DiskAnalysisResult? AnalysisResult
    {
        get => _analysisResult;
        private set
        {
            if (ReferenceEquals(_analysisResult, value)) return;
            _analysisResult = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProgressValue));
            OnPropertyChanged(nameof(FolderRoots));
            SelectedFile = null;
            SelectedFolder = null;
            SetTreemapFolder(value?.RootFolder);
            CommandCanExecuteChanged();
            if (value is null) VisibleFiles = Array.Empty<FileEntry>();
            UpdateExtensionChoices(value);
            ScheduleFileRefresh();
        }
    }

    public string Status
    {
        get => _status;
        private set { if (_status == value) return; _status = value; OnPropertyChanged(); }
    }

    public string CurrentDirectory
    {
        get => _currentDirectory;
        private set { if (_currentDirectory == value) return; _currentDirectory = value; OnPropertyChanged(); }
    }

    public int FilesScanned
    {
        get => _filesScanned;
        private set { if (_filesScanned == value) return; _filesScanned = value; OnPropertyChanged(); OnPropertyChanged(nameof(FilesScannedText)); }
    }

    public int FoldersScanned
    {
        get => _foldersScanned;
        private set { if (_foldersScanned == value) return; _foldersScanned = value; OnPropertyChanged(); OnPropertyChanged(nameof(FoldersScannedText)); }
    }

    public long BytesScanned
    {
        get => _bytesScanned;
        private set { if (_bytesScanned == value) return; _bytesScanned = value; OnPropertyChanged(); OnPropertyChanged(nameof(BytesScannedText)); }
    }

    public TimeSpan Elapsed
    {
        get => _elapsed;
        private set { if (_elapsed == value) return; _elapsed = value; OnPropertyChanged(); OnPropertyChanged(nameof(ElapsedText)); }
    }

    public string FilesScannedText => FilesScanned.ToString("N0");
    public string FoldersScannedText => FoldersScanned.ToString("N0");
    public string BytesScannedText => DiskDriveOption.FormatBytes(BytesScanned);
    public string ElapsedText => $"{(int)Elapsed.TotalHours:00}:{Elapsed.Minutes:00}:{Elapsed.Seconds:00}";

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set
        {
            if (_isRefreshing == value) return;
            _isRefreshing = value;
            OnPropertyChanged();
            CommandCanExecuteChanged();
        }
    }

    public bool IsScanning
    {
        get => _isScanning;
        private set
        {
            if (_isScanning == value) return;
            _isScanning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsProgressIndeterminate));
            CommandCanExecuteChanged();
        }
    }

    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (_isPaused == value) return;
            _isPaused = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsProgressIndeterminate));
            CommandCanExecuteChanged();
        }
    }

    public bool IsProgressIndeterminate => IsScanning && !IsPaused;
    public double ProgressValue => AnalysisResult?.IsComplete == true ? 100 : 0;

    public async Task RefreshDrivesAsync()
    {
        if (IsRefreshing || IsScanning) return;
        IsRefreshing = true;
        Status = "Procurando unidades disponíveis para leitura…";
        try
        {
            string? previousSelection = SelectedDrive?.RootPath;
            DiskInfoReadResult result = await _diskInfo.GetAvailableDrivesAsync();
            Drives.Clear();
            foreach (var drive in result.Drives) Drives.Add(drive);

            SelectedDrive = Drives.FirstOrDefault(d => string.Equals(d.RootPath, previousSelection, StringComparison.OrdinalIgnoreCase))
                            ?? Drives.FirstOrDefault();
            if (AnalysisResult is not null
                && !string.Equals(AnalysisResult.RootPath, SelectedDrive?.RootPath, StringComparison.OrdinalIgnoreCase))
                AnalysisResult = null;

            if (Drives.Count == 0)
            {
                Status = result.UnavailableCount > 0
                    ? "Nenhuma unidade está pronta para leitura. Verifique se estão conectadas e tente atualizar."
                    : "Nenhuma unidade disponível para leitura foi encontrada.";
            }
            else
            {
                Status = $"{Drives.Count} unidade(s) disponível(is) para leitura."
                         + (result.UnavailableCount > 0 ? $" {result.UnavailableCount} unidade(s) estavam indisponíveis e foram ignoradas." : "");
            }
        }
        catch (Exception ex)
        {
            Status = $"Não foi possível consultar as unidades. Tente atualizar novamente. Detalhe: {ex.Message}";
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    private void SelectDrive(DiskDriveOption drive)
    {
        if (!Drives.Contains(drive) || IsScanning) return;
        SelectedDrive = drive;
        AnalysisResult = null;
        Status = $"Unidade {drive.DriveLetter} selecionada para análise.";
    }

    private async Task StartAnalysisAsync()
    {
        if (SelectedDrive is null || IsScanning || IsRefreshing) return;

        var drive = SelectedDrive;
        var cancellation = new CancellationTokenSource();
        var control = new DiskScanControl();
        _scanCancellation = cancellation;
        _scanControl = control;
        AnalysisResult = null;
        FilesScanned = 0;
        FoldersScanned = 0;
        BytesScanned = 0;
        Elapsed = TimeSpan.Zero;
        CurrentDirectory = drive.RootPath;
        IsPaused = false;
        IsScanning = true;
        Status = $"Analisando {drive.RootPath}…";

        var progress = new Progress<DiskScanProgress>(ApplyProgress);
        try
        {
            DiskAnalysisResult result = await _scanner.AnalyzeAsync(
                drive.RootPath, progress, control, cancellation.Token);
            AnalysisResult = result;
            FilesScanned = result.FilesScanned;
            FoldersScanned = result.FoldersScanned;
            BytesScanned = result.ScannedBytes;
            Elapsed = result.CompletedAt.GetValueOrDefault().Subtract(result.StartedAt);
            CurrentDirectory = result.RootPath;
            Status = result.Message ?? "Análise concluída.";
        }
        catch (OperationCanceledException)
        {
            Status = "Análise cancelada. Os arquivos e diretórios encontrados até agora não foram alterados.";
        }
        catch (Exception ex)
        {
            Status = $"Não foi possível concluir a análise. Detalhe: {ex.Message}";
        }
        finally
        {
            cancellation.Dispose();
            if (ReferenceEquals(_scanCancellation, cancellation)) _scanCancellation = null;
            _scanControl = null;
            IsPaused = false;
            IsScanning = false;
        }
    }

    private void ApplyProgress(DiskScanProgress progress)
    {
        if (!IsScanning || IsPaused || AnalysisResult is not null) return;
        CurrentDirectory = progress.CurrentDirectory;
        FilesScanned = progress.FilesScanned;
        FoldersScanned = progress.FoldersScanned;
        BytesScanned = progress.ScannedBytes;
        Elapsed = progress.Elapsed;
        if (progress.ErrorsEncountered > 0 || progress.ReparsePointsSkipped > 0)
            Status = $"Analisando; {progress.ErrorsEncountered:N0} erro(s) de leitura e {progress.ReparsePointsSkipped:N0} link(s)/junção(ões) ignorado(s).";
    }

    private void PauseAnalysis()
    {
        if (!IsScanning || IsPaused || _scanControl is null) return;
        _scanControl.Pause();
        IsPaused = true;
        Status = "Análise pausada.";
    }

    private void ResumeAnalysis()
    {
        if (!IsScanning || !IsPaused || _scanControl is null) return;
        _scanControl.Resume();
        IsPaused = false;
        Status = $"Analisando {CurrentDirectory}…";
    }

    private void CancelAnalysis()
    {
        if (!IsScanning) return;
        Status = "Cancelando análise…";
        _scanCancellation?.Cancel();
    }

    public void SelectFolder(FolderEntry? folder)
    {
        SelectedFolder = folder;
        if (folder is not null)
        {
            SetTreemapFolder(folder);
            Status = $"Pasta selecionada: {folder.FullPath}";
        }
    }

    public void EnterTreemapFolder(FolderEntry folder)
    {
        SelectedFolder = folder;
        SetTreemapFolder(folder);
        Status = $"Pasta selecionada: {folder.FullPath}";
    }

    private void GoUpTreemap()
    {
        if (TreemapFolder?.Parent is not { } parent) return;
        SelectedFolder = parent;
        SetTreemapFolder(parent);
        Status = $"Pasta selecionada: {parent.FullPath}";
    }

    private void SetTreemapFolder(FolderEntry? folder)
    {
        if (ReferenceEquals(_treemapFolder, folder)) return;
        SelectedFile = null;
        _treemapFolder = folder;
        OnPropertyChanged(nameof(TreemapFolder));
        OnPropertyChanged(nameof(TreemapFolderPath));
        OnPropertyChanged(nameof(CanGoUpTreemap));
        (GoUpTreemapCommand as RelayCommand)?.NotifyCanExecuteChanged();
        _treemapBuildCancellation?.Cancel();

        if (folder is null)
        {
            _treemapTiles = Array.Empty<TreemapTile>();
            OnPropertyChanged(nameof(TreemapTiles));
            return;
        }

        _ = BuildTreemapTilesAsync(folder);
    }

    private async Task BuildTreemapTilesAsync(FolderEntry folder)
    {
        _treemapBuildCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _treemapBuildCancellation = cancellation;
        try
        {
            var tiles = await Task.Run(() => TreemapDataBuilder.Build(folder, cancellation.Token), cancellation.Token);
            if (!cancellation.IsCancellationRequested && ReferenceEquals(_treemapBuildCancellation, cancellation)
                && ReferenceEquals(_treemapFolder, folder))
            {
                _treemapTiles = tiles;
                OnPropertyChanged(nameof(TreemapTiles));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (ReferenceEquals(_treemapBuildCancellation, cancellation))
                Status = $"Não foi possível preparar o mapa de espaço: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_treemapBuildCancellation, cancellation)) _treemapBuildCancellation = null;
            cancellation.Dispose();
        }
    }

    private void AddChoice(ObservableCollection<DiskAnalyzerChoice> choices, string key, string label, bool selected = false)
    {
        var choice = new DiskAnalyzerChoice(key, label, selected);
        choice.PropertyChanged += Choice_PropertyChanged;
        choices.Add(choice);
    }

    private void Choice_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiskAnalyzerChoice.IsSelected)
            && sender is DiskAnalyzerChoice { IsSelected: true })
            ScheduleFileRefresh();
    }

    private void UpdateExtensionChoices(DiskAnalysisResult? result)
    {
        foreach (var choice in ExtensionFilters) choice.PropertyChanged -= Choice_PropertyChanged;
        ExtensionFilters.Clear();
        AddChoice(ExtensionFilters, "*", "Todas", true);
        var extensions = new HashSet<string>(
            new[] { ".exe", ".dll", ".iso", ".zip", ".rar", ".mp4", ".jpg", ".png", ".pdf", ".docx" },
            StringComparer.OrdinalIgnoreCase);
        if (result is not null) extensions.UnionWith(result.FilesByExtension.Keys);
        foreach (string extension in extensions.OrderBy(e => e, StringComparer.OrdinalIgnoreCase))
            AddChoice(ExtensionFilters, extension, extension.ToUpperInvariant());
        OnPropertyChanged(nameof(ExtensionFilters));
    }

    private void ScheduleFileRefresh()
    {
        SelectedFile = null;
        _ = RefreshVisibleFilesAsync();
    }

    private async Task RefreshVisibleFilesAsync()
    {
        _fileQueryCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _fileQueryCancellation = cancellation;
        DiskAnalysisResult? result = AnalysisResult;
        string search = SearchText.Trim();
        string extension = ExtensionFilters.FirstOrDefault(c => c.IsSelected)?.Key ?? "*";
        string? folderPath = SelectedFolder?.FullPath;
        string sortKey = SortOptions.FirstOrDefault(c => c.IsSelected)?.Key ?? "size-desc";

        try
        {
            await Task.Delay(180, cancellation.Token);
            IReadOnlyList<FileEntry> rows = result is null
                ? Array.Empty<FileEntry>()
                : await Task.Run(() => FilterAndSortFiles(result.Files, search, extension, folderPath, sortKey, cancellation.Token), cancellation.Token);

            if (ReferenceEquals(_fileQueryCancellation, cancellation)) VisibleFiles = rows;
        }
        catch (OperationCanceledException) { }
        catch (InvalidOperationException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (ReferenceEquals(_fileQueryCancellation, cancellation))
                Status = $"Não foi possível atualizar a lista de arquivos: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_fileQueryCancellation, cancellation)) _fileQueryCancellation = null;
            cancellation.Dispose();
        }
    }

    private static IReadOnlyList<FileEntry> FilterAndSortFiles(
        IReadOnlyList<FileEntry> source, string search, string extension, string? folderPath,
        string sortKey, CancellationToken cancellationToken)
    {
        var rows = new List<FileEntry>();
        foreach (var file in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(search) && !file.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) continue;
            if (extension != "*" && !string.Equals(file.Extension, extension, StringComparison.OrdinalIgnoreCase)) continue;
            if (folderPath is not null && !string.Equals(file.FolderPath, folderPath, StringComparison.OrdinalIgnoreCase)) continue;
            rows.Add(file);
        }
        rows.Sort((left, right) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            int comparison = sortKey switch
            {
                "size-asc" => left.SizeBytes.CompareTo(right.SizeBytes),
                "name" => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name),
                "extension" => StringComparer.OrdinalIgnoreCase.Compare(left.Extension, right.Extension),
                "date" => right.LastModifiedUtc.CompareTo(left.LastModifiedUtc),
                _ => right.SizeBytes.CompareTo(left.SizeBytes),
            };
            return comparison != 0 ? comparison : StringComparer.OrdinalIgnoreCase.Compare(left.FullPath, right.FullPath);
        });
        return rows;
    }

    private void OpenSelectedFolder()
    {
        string? path = SelectedFolder?.FullPath ?? AnalysisResult?.RootFolder?.FullPath;
        if (string.IsNullOrWhiteSpace(path)) return;
        if (!Directory.Exists(path))
        {
            Status = "Esta pasta não está mais disponível.";
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { Status = $"Não foi possível abrir a pasta: {ex.Message}"; }
    }

    private void OpenFileLocation()
    {
        if (SelectedFile is not { } file) return;
        if (!File.Exists(file.FullPath))
        {
            Status = "Este arquivo não está mais disponível.";
            return;
        }
        try
        {
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            start.ArgumentList.Add($"/select,{file.FullPath}");
            Process.Start(start);
        }
        catch (Exception ex) { Status = $"Não foi possível abrir a localização: {ex.Message}"; }
    }

    private void OpenFileFolder()
    {
        if (SelectedFile is not { } file) return;
        if (!Directory.Exists(file.FolderPath))
        {
            Status = "A pasta deste arquivo não está mais disponível.";
            return;
        }
        try { Process.Start(new ProcessStartInfo(file.FolderPath) { UseShellExecute = true }); }
        catch (Exception ex) { Status = $"Não foi possível abrir a pasta do arquivo: {ex.Message}"; }
    }

    private void CopyFilePath()
    {
        if (SelectedFile is not { } file) return;
        try
        {
            Clipboard.SetText(file.FullPath);
            Status = "Caminho completo copiado.";
        }
        catch (Exception ex) { Status = $"Não foi possível copiar o caminho: {ex.Message}"; }
    }

    private void CommandCanExecuteChanged()
    {
        (RefreshDrivesCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (SelectDriveCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (StartAnalysisCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (PauseAnalysisCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ResumeAnalysisCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (CancelAnalysisCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (OpenSelectedFolderCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (OpenFileLocationCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (OpenFileFolderCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (CopyFilePathCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (GoUpTreemapCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class RelayCommand(Action<object?> execute, Func<object?, bool> canExecute) : ICommand
    {
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => canExecute(parameter);
        public void Execute(object? parameter) => execute(parameter);
        public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
