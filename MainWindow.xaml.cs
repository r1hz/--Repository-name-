using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using LibreHardwareMonitor.Hardware;
using ResetExplode.DiskAnalyzer;
using GeolabTITools.Application.Features.SystemInfo.Contracts;
using GeolabTITools.Application.Features.SystemInfo.DTOs;
using ResetExplode.Presentation.ViewModels.System;

namespace ResetExplode;

public partial class MainWindow : Window
{
    private readonly SensorMonitor _monitor;
    private readonly SystemViewModel _systemViewModel;
    private readonly DispatcherTimer _timer;
    private readonly ICollectionView _view;
    private string? _category;   // filtro por chip (null = todos)
    private string _search = ""; // filtro por texto

    // Placa de vídeo
    private List<GpuInfo> _gpus = new();
    private GpuInfo? _gpu;

    // Stress test
    private readonly StressTester _stress = new();
    private readonly List<double> _tempHistory = new();
    private const int HistoryLength = 300; // 5 min a 1 amostra/s
    private DateTime _stressStart;
    private TimeSpan _stressDuration;
    private double _tempLimit = 90;
    private float _stressMaxTemp;

    private readonly AppSettings _settings = AppSettings.Load();
    private string _cpuName = "";
    public sealed record MeterItem(string Label, string Value, double Fraction);

    // GEOLAB BENCH
    private readonly BenchRenderer _bench = new();
    private System.Windows.Media.Imaging.WriteableBitmap? _benchBitmap;
    private CancellationTokenSource? _benchCts;
    private DispatcherTimer? _benchTimer;
    private DateTime _benchStart;
    private string _benchPhase = "";
    private DateTime _benchPhaseStart;

    // GEOLAB OTIMIZA
    private List<BackgroundApp> _apps = new();
    private readonly List<Tweak> _tweaks = WindowsTweaks.All(); // só as definições; o estado é lido ao abrir a página
    private bool _optInit;
    private bool _heroBarAnimationRunning;

    // Perfis locais do Windows que podem ser removidos pela página de ferramentas.
    private readonly List<UserProfileEntry> _userProfiles = new();

    private sealed class UserProfileEntry
    {
        public required string Sid { get; init; }
        public required string LocalPath { get; init; }
        public required string Details { get; init; }
        public bool IsSelected { get; set; }
    }

    // Stress de GPU (estilo FurMark)
    private readonly GpuBurn _burn = new();
    private bool _gpuBurnOn;
    private double _gpuTempLimit = 85;
    private float _gpuMaxTemp;
    private readonly List<double> _gpuTempHistory = new();

    private Brush Fg => (Brush)FindResource("Fg");
    private Brush Ink => (Brush)FindResource("Ink");
    private Brush Muted => (Brush)FindResource("Muted");
    private Brush Card => (Brush)FindResource("Card");

    public MainWindow(DiskAnalyzerViewModel diskAnalyzerViewModel, SystemViewModel systemViewModel)
    {
        InitializeComponent();
        _systemViewModel = systemViewModel;
        PageDiskAnalyzer.DataContext = diskAnalyzerViewModel;
        PageSystem.DataContext = systemViewModel;
        ThemeManager.Apply(_settings.LightTheme);
        if (_settings.HeroBarCollapsed) DashHeroBar.Visibility = Visibility.Collapsed;
        UpdateHeroBarToggle();
        UpdateThemeButton();
        ShowPage("PageDash");

        // Sensores: a lista começa vazia e é preenchida quando a detecção do hardware termina (LoadHardwareAsync).
        _monitor = new SensorMonitor();
        _view = CollectionViewSource.GetDefaultView(_monitor.Rows);
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SensorRow.Hardware)));
        SensorGrid.ItemsSource = _view;
        StatusText.Text = "Detectando o hardware…";

        InitBench();
        SnapshotUsb();
        TitlePage.Text = "Visão geral";
        UpdateDashboard();

        // A janela aparece já; as coletas pesadas (WMI, sensores, GPU) rodam em paralelo fora da thread da janela.
        Loaded += async (_, _) => await LoadHardwareAsync();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            // Durante a cena de GPU as leituras de sensores (NVML/driver) ficam pausadas: cada consulta trava o driver
            // por um instante e aparecia como engasgo no 1% low do benchmark.
            if (_gpuBenchActive) return;
            _monitor.Refresh();
            if (_monitor.IsOpen) StatusText.Text = $"{_monitor.Rows.Count} sensores · atualizado {DateTime.Now:HH:mm:ss}";
            UpdateStressPanel();
            UpdateGpuLive();
            UpdateCpuSensorWarning();
            CollectDashSample();
            if (PageBench.IsVisible) UpdateBenchLive();
            if (PageOptimize.IsVisible) UpdateRamStatus();
        };
        _timer.Start();

        StateChanged += (_, _) =>
        {
            bool max = WindowState == WindowState.Maximized;
            Root.Margin = new Thickness(0); // MaximizeHook limita a janela à área útil: não há mais sobra de 7 px
            BtnMax.Content = max ? "" : "";
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _stress.Stop(); _burn.Stop(); _benchCts?.Cancel();
            _toast?.Close();
            _monitor.Dispose();
        };
    }

    private void HeroBarToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_heroBarAnimationRunning) return;

        bool collapsing = DashHeroBar.Visibility == Visibility.Visible;
        _heroBarAnimationRunning = true;
        _settings.HeroBarCollapsed = collapsing;
        try { _settings.Save(); } catch { }

        if (collapsing)
        {
            DashHeroBar.Height = double.NaN;
        }
        else
        {
            DashHeroBar.Visibility = Visibility.Visible;
            DashHeroBar.Height = double.NaN;
            UpdateLayout();
        }

        double targetHeight = collapsing ? 0 : DashHeroBar.ActualHeight;
        double startHeight = collapsing ? DashHeroBar.ActualHeight : 0;
        if (targetHeight <= 0 && !collapsing)
        {
            _heroBarAnimationRunning = false;
            UpdateHeroBarToggle();
            return;
        }

        if (!collapsing) DashHeroBar.Height = 0;
        var animation = new DoubleAnimation(startHeight, targetHeight, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        };
        animation.Completed += (_, _) =>
        {
            DashHeroBar.BeginAnimation(FrameworkElement.HeightProperty, null);
            DashHeroBar.Height = double.NaN;
            if (collapsing) DashHeroBar.Visibility = Visibility.Collapsed;
            _heroBarAnimationRunning = false;
            UpdateHeroBarToggle();
        };
        DashHeroBar.BeginAnimation(FrameworkElement.HeightProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void UpdateHeroBarToggle()
    {
        bool expanded = DashHeroBar.Visibility == Visibility.Visible;
        BtnHeroBarToggle.Content = expanded ? "\uE70E" : "\uE70D";
        BtnHeroBarToggle.ToolTip = expanded ? "Recolher a barra de destaque" : "Expandir a barra de destaque";
    }

    /// <summary>Coletas lentas da abertura, em paralelo e fora da thread da janela; cada parte preenche a tela quando fica pronta.</summary>
    private async Task LoadHardwareAsync()
    {
        async Task LoadInfo()
        {
            await _systemViewModel.LoadAsync();
        }

        async Task LoadSensors()
        {
            try { await _monitor.OpenAsync(); }
            catch (Exception ex) { StatusText.Text = $"Sensores indisponíveis: {ex.Message}"; return; }
            _monitor.Refresh();
            UpdateCpuSensorWarning();
            AddChip("Todos", null, isChecked: true);
            foreach (var c in _monitor.Rows.Select(r => r.Category).Distinct().OrderBy(x => x))
                AddChip(c, c);
            UpdateDashboard();
        }

        async Task LoadCpuName()
        {
            try
            {
                _cpuName = await _systemViewModel.ReadCpuNameAsync();
            }
            catch { _cpuName = ""; }
        }

        async Task LoadGpu()
        {
            try { _gpus = await Task.Run(GpuInfoCollector.Collect); }
            catch { _gpus = new(); }
            for (int i = 0; i < _gpus.Count && _gpus.Count > 1; i++)
            {
                var g = _gpus[i];
                var chip = new RadioButton { Content = g.Name, GroupName = "Gpu", Style = (Style)FindResource("Chip"), IsChecked = i == 0 };
                chip.Checked += (_, _) => ShowGpu(g);
                GpuChips.Children.Add(chip);
            }
            if (_gpus.Count > 0) ShowGpu(_gpus[0]);

            UpdateDashboard();
        }

        await Task.WhenAll(LoadInfo(), LoadSensors(), LoadGpu(), LoadCpuName());
        UpdateDashboard();
    }

    // ---------------- Sensores da CPU (PawnIO) ----------------

    /// <summary>Mostra o aviso no menu quando temperatura/consumo da CPU não respondem, com o motivo provável.</summary>
    private void UpdateCpuSensorWarning()
    {
        if (!_monitor.IsOpen) return; // ainda detectando: não acusar falta de sensor antes da hora
        var p = _monitor.DiagnoseCpuSensors();
        CpuSensorWarn.Visibility = p == SensorMonitor.CpuSensorProblem.None ? Visibility.Collapsed : Visibility.Visible;
        CpuSensorWarnText.Text = p switch
        {
            SensorMonitor.CpuSensorProblem.NotElevated => "Sem permissão de administrador.",
            SensorMonitor.CpuSensorProblem.PawnIOMissing => "Falta o driver PawnIO.",
            SensorMonitor.CpuSensorProblem.ReadFailed => "O driver não respondeu.",
            _ => ""
        };
    }

    private void BtnFixCpuSensors_Click(object sender, RoutedEventArgs e)
    {
        var p = _monitor.DiagnoseCpuSensors();
        string why = SensorMonitor.Explain(p);
        if (p != SensorMonitor.CpuSensorProblem.PawnIOMissing)
        {
            MessageBox.Show(why, "GEOLAB TI TOOLS", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // winget vem com o Windows 11 e com o Windows 10 atualizado (App Installer).
        string winget = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                               @"Microsoft\WindowsApps\winget.exe");
        if (!System.IO.File.Exists(winget))
        {
            MessageBox.Show(why + "\n\nO instalador de pacotes do Windows (winget) não foi encontrado neste PC. " +
                            "Atualize o \"Instalador de Aplicativo\" pela Microsoft Store e use o botão de novo, " +
                            "ou instale o PawnIO (autor: namazso) manualmente.",
                            "GEOLAB TI TOOLS", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var r = MessageBox.Show(why + "\n\nInstalar o PawnIO agora? Vai abrir uma janela com o instalador do Windows (winget). " +
                                "Quando terminar, feche o GEOLAB TI TOOLS e abra de novo.",
                                "GEOLAB TI TOOLS", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe",
            "/k winget install --id namazso.PawnIO -e --accept-package-agreements --accept-source-agreements" +
            " && echo. && echo Pronto! Feche esta janela e reabra o GEOLAB TI TOOLS.")
        { UseShellExecute = true });
    }

    // ---------------- Janela / navegação ----------------

    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Max_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PageSystem == null) return; // disparado durante InitializeComponent
        var item = (FrameworkElement)sender;
        string page = (string)item.Tag;
        ShowPage(page);
        if (page == "PageStress") UpdateStressHardware();

        // Abre a categoria da página (ex.: quando o painel pula direto para o Benchmark)
        // e mostra "CATEGORIA · página" na barra de título.
        string name = item is ContentControl { Content: Panel panel }
            ? panel.Children.OfType<TextBlock>().LastOrDefault()?.Text ?? "" : "";
        if (item.Parent is FrameworkElement { Name: var items } && items.EndsWith("Items")
            && FindName(items[..^5]) is System.Windows.Controls.Primitives.ToggleButton group)
        {
            group.IsChecked = true;
            name = $"{group.Content} · {name}";
        }
        TitlePage.Text = name;

        // Algumas páginas carregam dados só quando abertas.
        switch (page)
        {
            case "PageDash": UpdateDashboard(); break;
            case "PageDisks": RefreshDiskHealth(); break;
            case "PageEnergy": RefreshEnergy(); break;
            case "PageDriver": InitDriver(); break;
            case "PageStartup": ScanStartup(); break;
            case "PageMakerPorts": ScanUsb(); break;
            case "PageMakerDrv": InitMakerDrivers(); break;
            case "PageOptimize": InitOptimize(); break;
            case "PageUsers": _ = ScanUserProfilesAsync(); break;
        }
    }

    private void ShowPage(string name)
    {
        foreach (var p in new FrameworkElement[] { PageDash, PageSystem, PageSensors, PageGpu, PageBench, PageOptimize, PageDriver, PageStartup, PageUsers, PageMakerPorts, PageMakerDrv, PageDisks, PageDiskAnalyzer, PageEnergy, PageNet, PageStress })
            p.Visibility = p.Name == name ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------------- Perfis de usuário locais ----------------

    private async void BtnUsersScan_Click(object sender, RoutedEventArgs e) => await ScanUserProfilesAsync();

    private async Task ScanUserProfilesAsync()
    {
        UsersSummary.Text = "Procurando perfis locais inativos…";
        UsersResult.Text = "";
        try
        {
            var profiles = await Task.Run(() =>
            {
                var results = new List<UserProfileEntry>();
                string usersRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "..", "Users"));
                string currentProfile = System.IO.Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
                using var searcher = new System.Management.ManagementObjectSearcher("SELECT SID, LocalPath, Loaded, Special, LastUseTime FROM Win32_UserProfile");
                foreach (System.Management.ManagementObject profile in searcher.Get())
                {
                    using (profile)
                    {
                        string sid = profile["SID"]?.ToString() ?? "";
                        string rawPath = profile["LocalPath"]?.ToString() ?? "";
                        if (!System.Text.RegularExpressions.Regex.IsMatch(sid, @"^S-1-5-21-(?:\d+-){2,}\d+$") || string.IsNullOrWhiteSpace(rawPath)) continue;
                        if (Convert.ToBoolean(profile["Loaded"] ?? false) || Convert.ToBoolean(profile["Special"] ?? false)) continue;

                        string path;
                        try { path = System.IO.Path.GetFullPath(rawPath); } catch { continue; }
                        // Aceita apenas pastas diretamente dentro de C:\Users; evita caminhos redirecionados ou especiais.
                        if (!string.Equals(System.IO.Path.GetDirectoryName(path)?.TrimEnd(System.IO.Path.DirectorySeparatorChar), usersRoot.TrimEnd(System.IO.Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)) continue;
                        if (string.Equals(path, currentProfile, StringComparison.OrdinalIgnoreCase)) continue;
                        string folderName = System.IO.Path.GetFileName(path);
                        if (folderName.Equals("Administrator", StringComparison.OrdinalIgnoreCase) || folderName.Equals("suporte", StringComparison.OrdinalIgnoreCase)) continue;

                        long bytes = 0;
                        try
                        {
                            if (System.IO.Directory.Exists(path))
                                foreach (var file in System.IO.Directory.EnumerateFiles(path, "*", System.IO.SearchOption.AllDirectories))
                                    try { bytes += new System.IO.FileInfo(file).Length; } catch { }
                        }
                        catch { /* tamanho é informativo; permissões podem impedir a leitura de parte do perfil */ }
                        string size = bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):N2} GB" : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):N2} MB" : $"{bytes / 1024d:N1} KB";
                        string lastUse = "último uso indisponível";
                        try
                        {
                            var value = profile["LastUseTime"];
                            if (value != null) lastUse = "último uso: " + System.Management.ManagementDateTimeConverter.ToDateTime(value.ToString()!).ToString("dd/MM/yyyy HH:mm");
                        }
                        catch { }
                        results.Add(new UserProfileEntry { Sid = sid, LocalPath = path, Details = $"{sid}  ·  {size}  ·  {lastUse}" });
                    }
                }
                return results.OrderBy(p => p.LocalPath, StringComparer.OrdinalIgnoreCase).ToList();
            });

            _userProfiles.Clear();
            _userProfiles.AddRange(profiles);
            UsersList.ItemsSource = null;
            UsersList.ItemsSource = _userProfiles;
            UsersSummary.Text = profiles.Count == 0 ? "Nenhum perfil elegível encontrado" : $"{profiles.Count} perfil(is) inativo(s) encontrado(s)";
        }
        catch (Exception ex)
        {
            UsersSummary.Text = "Não foi possível listar os perfis";
            UsersResult.Text = ex.Message;
        }
    }

    private void BtnUsersSelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var profile in _userProfiles) profile.IsSelected = true;
        UsersList.Items.Refresh();
    }

    private void BtnUsersClear_Click(object sender, RoutedEventArgs e)
    {
        foreach (var profile in _userProfiles) profile.IsSelected = false;
        UsersList.Items.Refresh();
    }

    private void BtnUsersDelete_Click(object sender, RoutedEventArgs e)
    {
        var selected = _userProfiles.Where(p => p.IsSelected).ToList();
        if (selected.Count == 0)
        {
            UsersResult.Text = "Marque ao menos um perfil para continuar.";
            return;
        }

        var dialog = new Window
        {
            Title = "CONFIRMAÇÃO DE EXCLUSÃO", Owner = this, Width = 620, Height = 510,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false, Topmost = true,
            Background = (Brush)FindResource("Card"), Foreground = Fg
        };
        var panel = new StackPanel { Margin = new Thickness(24) };
        var warning = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(75, 26, 30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(232, 74, 74)),
            BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 14)
        };
        var warningText = new StackPanel();
        warningText.Children.Add(new TextBlock
        {
            Text = "⚠  EXCLUSÃO PERMANENTE", FontSize = 20, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 225, 225)), Margin = new Thickness(0, 0, 0, 5)
        });
        warningText.Children.Add(new TextBlock
        {
            Text = $"Serão apagados {selected.Count} perfil(is), todos os arquivos dessas pastas e as entradas correspondentes do Windows. Esta ação não pode ser desfeita.",
            TextWrapping = TextWrapping.Wrap, FontSize = 14, Foreground = Brushes.White
        });
        warning.Child = warningText;
        panel.Children.Add(warning);
        panel.Children.Add(new TextBlock { Text = "PERFIS QUE SERÃO EXCLUÍDOS", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 0, 0, 6) });
        var targets = new TextBox { Text = string.Join(Environment.NewLine, selected.Select(p => p.LocalPath)), IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 155, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 0, 14), FontSize = 13 };
        panel.Children.Add(targets);
        panel.Children.Add(new TextBlock { Text = "Digite EXCLUIR para habilitar a confirmação:", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        var input = new TextBox { Height = 36, FontSize = 16, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 0, 16) };
        panel.Children.Add(input);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button
        {
            Content = "CANCELAR", Padding = new Thickness(20, 11, 20, 11), FontSize = 14,
            FontWeight = FontWeights.Bold, IsCancel = true, Margin = new Thickness(0, 0, 10, 0), MinWidth = 140,
            Background = new SolidColorBrush(Color.FromRgb(49, 67, 84)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(155, 180, 199)),
            BorderThickness = new Thickness(2), Foreground = Brushes.White
        };
        var confirm = new Button
        {
            Content = "EXCLUIR PERFIS", Padding = new Thickness(18, 10, 18, 10), FontSize = 13,
            FontWeight = FontWeights.Bold, IsDefault = true, IsEnabled = false, MinWidth = 160,
            Background = new SolidColorBrush(Color.FromRgb(183, 35, 43)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(235, 90, 96)), Foreground = Brushes.White
        };
        input.TextChanged += (_, _) => confirm.IsEnabled = string.Equals(input.Text.Trim(), "EXCLUIR", StringComparison.OrdinalIgnoreCase);
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        panel.Children.Add(buttons);
        confirm.Click += (_, _) => { dialog.DialogResult = true; dialog.Close(); };
        dialog.Content = panel;
        input.Focus();
        if (dialog.ShowDialog() != true) return;

        int removed = 0;
        var errors = new List<string>();
        foreach (var entry in selected)
        {
            try
            {
                // SID foi obtido do WMI e validado; usa o objeto de perfil do Windows para remover pasta e ProfileList.
                string escapedSid = entry.Sid.Replace("'", "''", StringComparison.Ordinal);
                using var searcher = new System.Management.ManagementObjectSearcher($"SELECT * FROM Win32_UserProfile WHERE SID='{escapedSid}'");
                var matches = searcher.Get().Cast<System.Management.ManagementObject>().ToList();
                if (matches.Count != 1) throw new InvalidOperationException("O perfil mudou desde a busca. Atualize a lista e tente novamente.");
                using var profile = matches[0];
                string pathNow = profile["LocalPath"]?.ToString() ?? "";
                if (Convert.ToBoolean(profile["Loaded"] ?? false) || Convert.ToBoolean(profile["Special"] ?? false) || !string.Equals(System.IO.Path.GetFullPath(pathNow), entry.LocalPath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("O perfil está em uso ou seus dados mudaram. Atualize a lista.");
                profile.Delete();
                removed++;
            }
            catch (Exception ex) { errors.Add($"{entry.LocalPath}: {ex.Message}"); }
        }

        UsersResult.Text = errors.Count == 0
            ? $"{removed} perfil(is) removido(s)."
            : $"{removed} removido(s); {errors.Count} falha(s):\n" + string.Join("\n", errors);
        _ = ScanUserProfilesAsync();
    }

    // ---------------- Sistema ----------------

    // ---------------- Unidades e dispositivos conectados ----------------

    private Dictionary<string, UsbDevice> _usbPresent = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastVolumeEvent = DateTime.MinValue;
    private DispatcherTimer? _usbDebounce;
    private ToastWindow? _toast;

    private void Toast(string title, string text, string icon)
    {
        AddEvent(icon, title, text.Split('\n')[0], "");
        _toast ??= new ToastWindow();
        _toast.Notify(title, text, icon);
    }

    private async void SnapshotUsb()
    {
        try
        {
            var list = await Task.Run(UsbCleaner.List);
            _usbPresent = list.Where(d => d.Present).GroupBy(d => d.InstanceId).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        }
        catch { }
    }

    /// <summary>WM_DEVICECHANGE: unidades (pendrive, HD externo) e qualquer dispositivo USB.</summary>
    private void OnDeviceChange(int ev, IntPtr lParam)
    {
        const int DBT_DEVNODES_CHANGED = 0x0007, DBT_DEVICEARRIVAL = 0x8000, DBT_DEVICEREMOVECOMPLETE = 0x8004, DBT_DEVTYP_VOLUME = 2;
        if ((ev == DBT_DEVICEARRIVAL || ev == DBT_DEVICEREMOVECOMPLETE) && lParam != IntPtr.Zero &&
            Marshal.ReadInt32(lParam, 4) == DBT_DEVTYP_VOLUME)
        {
            int mask = Marshal.ReadInt32(lParam, 12); // DEV_BROADCAST_VOLUME.dbcv_unitmask: bit 0 = A:
            var letters = Enumerable.Range(0, 26).Where(i => (mask & (1 << i)) != 0).Select(i => $"{(char)('A' + i)}:").ToList();
            _lastVolumeEvent = DateTime.Now;
            OnVolumes(letters, ev == DBT_DEVICEARRIVAL);
        }
        else if (ev == DBT_DEVNODES_CHANGED)
        {
            // Chega várias vezes seguidas por dispositivo: espera assentar e compara com a lista anterior.
            _usbDebounce ??= new DispatcherTimer(TimeSpan.FromMilliseconds(1500), DispatcherPriority.Normal, (_, _) =>
            {
                _usbDebounce!.Stop();
                DiffUsb();
            }, Dispatcher);
            _usbDebounce.Stop();
            _usbDebounce.Start();
        }
    }

    private async void OnVolumes(List<string> letters, bool arrived)
    {
        if (arrived) await Task.Delay(800); // o sistema de arquivos leva um instante para montar
        var before = _systemViewModel.Drives.ToList();
        var now = await _systemViewModel.RefreshDrivesAsync();
        foreach (var l in letters)
        {
            if (arrived && now.FirstOrDefault(d => d.Letter.Equals(l, StringComparison.OrdinalIgnoreCase)) is { } d)
                Toast(d.Removable ? "Pendrive conectado" : "Unidade conectada",
                      $"{d.Title} · {DriveCard.Fmt(d.Free)} livres de {DriveCard.Fmt(d.Total)}" + (d.Disk.Length > 0 ? $"\n{d.Disk}" : ""), "");
            else if (!arrived)
            {
                var old = before.FirstOrDefault(d => d.Letter.Equals(l, StringComparison.OrdinalIgnoreCase));
                Toast("Unidade removida", old != null ? $"{old.Title} pode ser desconectada." : $"{l} removida.", "");
            }
        }
        // Discos físicos (lista abaixo dos cartões) também mudam com pendrive/HD externo.
        await _systemViewModel.RefreshDiskInventoryAsync();
        if (_systemViewModel.IsStorageSelected) await _systemViewModel.RefreshDrivesAsync();
    }

    private async void DiffUsb()
    {
        List<UsbDevice> list;
        try { list = await Task.Run(UsbCleaner.List); } catch { return; }
        var present = list.Where(d => d.Present).GroupBy(d => d.InstanceId)
                          .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var added = present.Values.Where(d => !_usbPresent.ContainsKey(d.InstanceId)).ToList();
        var removed = _usbPresent.Values.Where(d => !present.ContainsKey(d.InstanceId)).ToList();
        _usbPresent = present;
        if (PageMakerPorts.IsVisible) ScanUsb();
        if (PageMakerDrv.IsVisible) RefreshMakerDevices(); // placa plugada/removida: atualiza o diagnóstico

        // Pendrives já geram aviso de unidade: não repete com o nome genérico "armazenamento em massa".
        bool volumeJustNow = (DateTime.Now - _lastVolumeEvent).TotalSeconds < 4;
        static bool Storage(UsbDevice d) => d.Name.Contains("Mass Storage", StringComparison.OrdinalIgnoreCase) ||
                                            d.Name.Contains("Armazenamento", StringComparison.OrdinalIgnoreCase);
        // Um dispositivo composto (ex.: Arduino Leonardo) gera vários registros: anuncia o mais descritivo.
        static UsbDevice? Best(List<UsbDevice> l) =>
            l.FirstOrDefault(d => d.Port != null) ?? l.FirstOrDefault(d => d.Maker != null) ??
            l.FirstOrDefault(d => !d.Name.Contains("Composite", StringComparison.OrdinalIgnoreCase) &&
                                  !d.Name.Contains("Hub", StringComparison.OrdinalIgnoreCase)) ?? l.FirstOrDefault();

        added = added.Where(d => !Storage(d)).ToList();
        removed = removed.Where(d => !Storage(d)).ToList();
        if (!volumeJustNow && Best(added) is { } a)
            Toast("Dispositivo USB conectado", a.Name + (a.Maker != null && !a.Name.Contains(a.Maker) ? $" · {a.Maker}" : ""), "");
        else if (!volumeJustNow && Best(removed) is { } r)
            Toast("Dispositivo USB desconectado", r.Name, "");
    }

    // ---------------- Placa de vídeo ----------------

    private void ShowGpu(GpuInfo g)
    {
        _gpu = g;
        GpuName.Text = g.Name;
        GpuSubtitle.Text = g.Subtitle;
        GpuVendor.Text = g.Vendor;
        GpuSections.ItemsSource = g.Sections;

        // Sensores do LibreHardwareMonitor deste hardware, em ordem fixa (clock, temperatura, carga...).
        var live = new ListCollectionView(_monitor.Rows)
        {
            Filter = o => ((SensorRow)o).Hardware == g.Name
        };
        live.SortDescriptions.Add(new SortDescription(nameof(SensorRow.Order), ListSortDirection.Ascending));
        live.SortDescriptions.Add(new SortDescription(nameof(SensorRow.Name), ListSortDirection.Ascending));
        GpuLive.ItemsSource = live;
        GpuExtras.ItemsSource = g.LiveExtras();
    }

    private void UpdateGpuLive()
    {
        // Barra lateral: GPU principal (dedicada = primeira da lista).
        if (_gpus.Count > 0)
        {
            var load = _monitor.Rows.FirstOrDefault(r => r.Hardware == _gpus[0].Name && r.Type == SensorType.Load
                                                         && r.Name == "GPU Core");
            SideGpuTemp.Text = MainGpuTemp() is float t ? $"{t:0.#} °C" : "—";
            SideGpuLoad.Text = load?.RawValue is float l ? $"{l:0} %" : "—";
        }
        if (_gpu != null && PageGpu.IsVisible) GpuExtras.ItemsSource = _gpu.LiveExtras();
    }

    // ---------------- GEOLAB BENCH ----------------

    private void InitBench()
    {
        _benchBitmap = new System.Windows.Media.Imaging.WriteableBitmap(BenchRenderer.Width, BenchRenderer.Height, 96, 96,
                                                                       PixelFormats.Bgra32, null);
        BenchImage.Source = _benchBitmap;
        UpdateBenchHistory();
        UpdateBenchLive();
    }

    private bool BenchRunning => _benchCts != null;

    private async void BtnBench_Click(object sender, RoutedEventArgs e)
    {
        if (BenchRunning) { _benchCts!.Cancel(); return; }
        if (TestRunning || _secondaryBenchRunning) { BenchHint.Text = "Pare o outro teste antes de rodar o benchmark."; return; }

        string mode = CheckedTag(BenchModePanel) ?? "both";
        bool sustained = CheckedTag(BenchDurationPanel) == "10";
        var minDuration = sustained ? TimeSpan.FromMinutes(10) : TimeSpan.Zero;
        int threads = Environment.ProcessorCount;

        _benchCts = new CancellationTokenSource();
        var ct = _benchCts.Token;
        BtnBench.Content = "PARAR";
        BtnBench.Style = (Style)FindResource("OutlineButton");
        BenchModePanel.IsEnabled = BenchDurationPanel.IsEnabled = BenchTestPanel.IsEnabled = false;
        BenchHint.Text = "";
        BenchIdle.Visibility = Visibility.Collapsed;
        BenchMt.Text = BenchSt.Text = "—";
        BenchRatio.Text = "";
        _benchStart = DateTime.Now;
        _benchTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Render, (_, _) => UpdateBenchView(), Dispatcher);
        _benchTimer.Start();

        double? mt = null, st = null;
        try
        {
            if (mode is "mt" or "both")
            {
                _benchPhase = $"Multi-core · {threads} threads";
                _benchPhaseStart = DateTime.Now;
                var r = await Task.Run(() => _bench.Run(threads, minDuration, ct), ct);
                if (!ct.IsCancellationRequested) { mt = r.Score; BenchMt.Text = $"{r.Score:N0}"; }
            }
            if (!ct.IsCancellationRequested && mode is "st" or "both")
            {
                _benchPhase = "Single-core · 1 thread";
                _benchPhaseStart = DateTime.Now;
                var r = await Task.Run(() => _bench.Run(1, minDuration, ct), ct);
                if (!ct.IsCancellationRequested) { st = r.Score; BenchSt.Text = $"{r.Score:N0}"; }
            }
        }
        catch (OperationCanceledException) { }

        _benchTimer.Stop();
        UpdateBenchView();
        bool cancelled = ct.IsCancellationRequested;
        _benchCts = null;
        BtnBench.Content = "INICIAR";
        BtnBench.Style = (Style)FindResource("PrimaryButton");
        BenchModePanel.IsEnabled = BenchDurationPanel.IsEnabled = BenchTestPanel.IsEnabled = true;
        BenchBuckets.Children.Clear();

        var total = DateTime.Now - _benchStart;
        if (cancelled)
        {
            BenchStatus.Text = "Cancelado";
            return;
        }
        BenchStatus.Text = $"Concluído em {total:m\\:ss}";
        if (mt is double m && st is double s)
            BenchRatio.Text = $"Multi ÷ single: {m / s:0.0}x com {threads} threads " +
                              $"({Environment.ProcessorCount / 2} núcleos com SMT costumam render 8–12x).";

        _settings.BenchRuns.Insert(0, new BenchRun(DateTime.Now, mt, st, sustained, HardwareNames.ShortCpuName(_cpuName)));
        AddEvent("", "GEOLAB BENCH", "Render de CPU", mt is double mte ? $"{mte:N0} pts" : st is double ste ? $"{ste:N0} pts" : "");
        if (_settings.BenchRuns.Count > 10) _settings.BenchRuns.RemoveRange(10, _settings.BenchRuns.Count - 10);
        _settings.Save();
        UpdateBenchHistory();
    }

    /// <summary>Copia a imagem parcial e desenha o contorno dos blocos que cada thread está renderizando.</summary>
    private void UpdateBenchView()
    {
        _benchBitmap!.WritePixels(new Int32Rect(0, 0, BenchRenderer.Width, BenchRenderer.Height),
                                  _bench.Pixels, BenchRenderer.Width * 4, 0);

        BenchBuckets.Children.Clear();
        var active = _bench.Active;
        foreach (int b in active)
        {
            if (b < 0) continue;
            var (x, y, w, h) = _bench.Buckets[b];
            var rect = new Rectangle { Width = w, Height = h, Stroke = Brushes.White, StrokeThickness = 1.5 };
            Canvas.SetLeft(rect, x); Canvas.SetTop(rect, y);
            BenchBuckets.Children.Add(rect);
        }

        if (BenchRunning)
        {
            double frac = _bench.BucketsTotal > 0 ? (double)_bench.BucketsDone / _bench.BucketsTotal : 0;
            BenchProgress.ScaleX = frac;
            // Tempo restante da passada atual, pela velocidade até agora (só depois de 3% para não oscilar).
            var phaseElapsed = DateTime.Now - _benchPhaseStart;
            string eta = frac >= 0.03 ? $" · faltam ~{TimeSpan.FromSeconds(phaseElapsed.TotalSeconds * (1 - frac) / frac):m\\:ss}" : "";
            BenchStatus.Text = $"{_benchPhase} · {frac:P0}{eta}";
            BenchElapsed.Text = (DateTime.Now - _benchStart).ToString(@"mm\:ss");
        }
        else BenchProgress.ScaleX = 0;
    }

    /// <summary>Temperatura, clock médio dos núcleos e consumo da CPU (atualizado junto com os sensores).</summary>
    private void UpdateBenchLive()
    {
        float? clockActive = CpuClockStats().Active;
        BenchLive.ItemsSource = new[]
        {
            new KeyValuePair<string, string>("CPU", HardwareNames.ShortCpuName(_cpuName)),
            new KeyValuePair<string, string>("Threads", $"{Environment.ProcessorCount}"),
            new KeyValuePair<string, string>("Temperatura", _monitor.CpuTemperature is float t ? $"{t:0.#} °C" : "—"),
            new KeyValuePair<string, string>("Clock (núcleos ativos)", clockActive is float ca ? $"{ca:0} MHz" : "—"),
            new KeyValuePair<string, string>("Consumo", _monitor.CpuPower is float p ? $"{p:0.#} W" : "—"),
            new KeyValuePair<string, string>("Carga", _monitor.CpuLoad is float l ? $"{l:0} %" : "—"),
        };
    }

    private void UpdateBenchHistory()
    {
        var runs = _settings.BenchRuns;
        BenchHistoryEmpty.Visibility = runs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        double bestMt = runs.Max(r => r.Multi) ?? 1, bestSt = runs.Max(r => r.Single) ?? 1;
        BenchHistory.ItemsSource = runs.Select(r =>
        {
            string label = $"{r.Date:dd/MM HH:mm}{(r.Sustained ? " · 10 min" : "")}";
            string value = $"{(r.Multi is double m ? $"{m:N0}" : "—")} / {(r.Single is double s ? $"{s:N0}" : "—")}";
            double frac = r.Multi is double mm ? mm / bestMt : r.Single is double ss ? ss / bestSt : 0;
            return new MeterItem(label, value, frac);
        }).ToList();
    }

    // ---------------- GEOLAB OTIMIZA ----------------

    /// <summary>Carrega a página GEOLAB OTIMIZA quando ela é aberta (os ajustes do Windows só na primeira vez).</summary>
    private void InitOptimize()
    {
        if (!_optInit)
        {
            _optInit = true;
            foreach (var t in _tweaks)
                try { t.Refresh(); } catch (Exception ex) { t.Status = $"Não foi possível ler: {ex.Message}"; }
            OptTweaks.ItemsSource = _tweaks;
        }
        RefreshApps();
        UpdateRamStatus();
    }

    private void UpdateRamStatus()
    {
        var (total, avail) = Optimizer.MemoryStatus();
        if (total == 0) return;
        double used = total - avail;
        OptRamText.Text = $"{used / 1073741824.0:0.0} de {total / 1073741824.0:0.0} GB · {used / total:P0}";
        OptRamBar.ScaleX = used / total;
    }

    private void RefreshApps()
    {
        _apps = Optimizer.ListApps();
        OptApps.ItemsSource = _apps;
    }

    private void BtnRefreshApps_Click(object sender, RoutedEventArgs e) => RefreshApps();

    private async void BtnOptimize_Click(object sender, RoutedEventArgs e)
    {
        var toClose = _apps.Where(a => a.Selected).ToList();
        string list = toClose.Count == 0 ? "nenhum programa marcado"
                                         : string.Join("\n", toClose.Select(a => $"  • {a.Title}"));
        var r = MessageBox.Show("O GEOLAB OTIMIZA vai:\n\n" +
                                "1. Limpar a memória em espera e enxugar a RAM dos programas.\n" +
                                $"2. Fechar os programas marcados:\n{list}\n\n" +
                                "Salve o que estiver aberto nesses programas antes. Continuar?",
                                "GEOLAB OTIMIZA", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        BtnOptimize.IsEnabled = false;
        BtnOptimize.Content = "OTIMIZANDO…";
        OptResult.Text = "";
        var closeResult = await Task.Run(() => Optimizer.CloseApps(toClose));
        var mem = await Task.Run(Optimizer.CleanMemory); // depois de fechar, para contar a memória liberada por eles também
        BtnOptimize.IsEnabled = true;
        BtnOptimize.Content = "GEOLAB OTIMIZA";

        var parts = new List<string> { $"Memória liberada: {mem.FreedBytes / 1048576.0:N0} MB" };
        parts.Add(mem.StandbyPurged ? "memória em espera limpa" : $"memória em espera não limpa ({mem.StandbyError})");
        if (closeResult.Closed.Count > 0) parts.Add($"fechados: {string.Join(", ", closeResult.Closed)}");
        if (closeResult.NotClosed.Count > 0)
            parts.Add($"não fecharam (podem ter algo não salvo ou pedir confirmação): {string.Join(", ", closeResult.NotClosed)}");
        OptResult.Text = string.Join(" · ", parts) + ".";
        RefreshApps();
        UpdateRamStatus();
    }

    private void Tweak_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { Tag: Tweak t } box) return;
        bool turnOn = box.IsChecked == true;
        try
        {
            t.Write(turnOn, _settings);
            _settings.Save();
            t.Refresh();
            t.Status = t.IsOn == turnOn
                ? (turnOn ? "Aplicado." : "Desligado.") + (t.NeedsRestart ? " Reinicie o PC para valer." : "")
                : "O Windows não aceitou a mudança (pode exigir administrador).";
        }
        catch (Exception ex)
        {
            t.Refresh();
            t.Status = $"Falhou: {ex.Message}";
        }
        box.IsChecked = t.IsOn; // o binding é só leitura: volta ao estado real
    }

    // ----- USB e portas COM -----

    private List<UsbDevice> _usb = new();

    private void ScanUsb()
    {
        try
        {
            _usb = UsbCleaner.List();
        }
        catch (Exception ex) { UsbResult.Text = $"Não foi possível listar os dispositivos: {ex.Message}"; return; }

        static bool Relevant(UsbDevice d) => d.Port != null || d.Maker != null;
        var connected = _usb.Where(d => d.Present && Relevant(d)).ToList();
        UsbConnected.Text = connected.Count == 0 ? "Nenhuma placa ou porta serial conectada."
                          : string.Join("\n", connected.Select(d => $"{d.Port ?? "—"} · {d.Name}"));

        var shown = _usb.Where(d => !d.Present && (UsbShowAll.IsChecked == true || Relevant(d))).ToList();
        UsbList.ItemsSource = shown;
        int ghosts = _usb.Count(d => !d.Present);
        UsbSummary.Text = shown.Count == 0
            ? "Nenhum registro antigo encontrado."
            : $"{shown.Count} registros antigos na lista ({ghosts} dispositivos ausentes no total).";

        try
        {
            var (reserved, inUse) = UsbCleaner.ComStatus();
            string R(IEnumerable<int> l) => l.Any() ? string.Join(", ", l.Select(n => $"COM{n}")) : "nenhum";
            UsbComDb.Text = $"{reserved.Count} reservados: {R(reserved)}\nEm uso agora: {R(inUse)}";
        }
        catch (Exception ex) { UsbComDb.Text = ex.Message; }
        BtnComDbRestore.IsEnabled = _settings.ComDbBackup != null;
    }

    private void BtnUsbScan_Click(object sender, RoutedEventArgs e) => ScanUsb();
    private void UsbShowAll_Changed(object sender, RoutedEventArgs e) { if (UsbList != null) ScanUsb(); }

    private bool RequireAdmin()
    {
        if (SensorMonitor.IsElevated) return true;
        MessageBox.Show("Esta ação precisa do GEOLAB TI TOOLS aberto como administrador.", "GEOLAB TI TOOLS",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private async void BtnUsbRemove_Click(object sender, RoutedEventArgs e)
    {
        var sel = _usb.Where(d => d.Selected && !d.Present).ToList();
        if (sel.Count == 0) { UsbResult.Text = "Nenhum registro selecionado."; return; }
        if (!RequireAdmin()) return;
        string names = string.Join("\n", sel.Take(12).Select(d => $"  • {d.Name}{(d.Port != null ? "" : $" ({d.Class})")}"))
                       + (sel.Count > 12 ? $"\n  … e mais {sel.Count - 12}" : "");
        var r = MessageBox.Show($"Remover {sel.Count} registros de dispositivos que não estão conectados?\n\n{names}\n\n" +
                                "Se plugar algum deles de novo, o Windows reinstala automaticamente.",
                                "GEOLAB MAKER", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;

        BtnUsbRemove.IsEnabled = false;
        UsbResult.Text = "Removendo…";
        var (removed, failed) = await Task.Run(() => UsbCleaner.Remove(sel.Select(d => d.InstanceId)));
        BtnUsbRemove.IsEnabled = true;
        UsbResult.Text = $"{removed} registros removidos." +
                         (failed.Count > 0 ? $" Não removidos: {string.Join("; ", failed.Take(5))}" : "") +
                         " Para liberar os números COM que eles prendiam, use \"Liberar números COM presos\".";
        ScanUsb();
    }

    private void BtnComDb_Click(object sender, RoutedEventArgs e)
    {
        if (!RequireAdmin()) return;
        var (reserved, inUse) = UsbCleaner.ComStatus();
        var freed = reserved.Except(inUse).ToList();
        if (freed.Count == 0) { UsbResult.Text = "Nenhum número COM preso: todos os reservados estão em uso."; return; }
        var r = MessageBox.Show($"Liberar {freed.Count} números COM presos por dispositivos que não estão conectados?\n\n" +
                                $"Liberados: {string.Join(", ", freed.Select(n => $"COM{n}"))}\n" +
                                $"Mantidos (em uso): {(inUse.Count > 0 ? string.Join(", ", inUse.Select(n => $"COM{n}")) : "nenhum")}\n\n" +
                                "A próxima placa plugada vai receber um número baixo (COM3, COM4…). " +
                                "O valor atual fica guardado para \"Desfazer liberação\".",
                                "GEOLAB MAKER", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;
        try
        {
            var old = UsbCleaner.RebuildComDb();
            _settings.ComDbBackup ??= old; // guarda o original da primeira vez
            _settings.Save();
            UsbResult.Text = $"Liberados: {string.Join(", ", freed.Select(n => $"COM{n}"))}. Plugue a placa de novo para ela pegar um número baixo.";
        }
        catch (Exception ex) { UsbResult.Text = $"Falhou: {ex.Message}"; }
        ScanUsb();
    }

    private void BtnComDbRestore_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.ComDbBackup == null || !RequireAdmin()) return;
        try
        {
            UsbCleaner.RestoreComDb(_settings.ComDbBackup);
            _settings.ComDbBackup = null;
            _settings.Save();
            UsbResult.Text = "Reservas de números COM restauradas para como estavam.";
        }
        catch (Exception ex) { UsbResult.Text = $"Falhou: {ex.Message}"; }
        ScanUsb();
    }

    private void BtnRestoreTweaks_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.TweakBackup.Count == 0)
        {
            MessageBox.Show("Nenhum ajuste foi alterado pelo GEOLAB TI TOOLS ainda.", "GEOLAB OTIMIZA",
                            MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var r = MessageBox.Show("Voltar todos os ajustes do Windows para como estavam antes do GEOLAB TI TOOLS alterá-los?",
                                "GEOLAB OTIMIZA", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (r != MessageBoxResult.Yes) return;
        bool restart = _tweaks.Any(t => t.NeedsRestart && _settings.TweakBackup.ContainsKey(t.Id));
        WindowsTweaks.RestoreAll(_tweaks, _settings);
        foreach (var t in _tweaks) { t.Refresh(); t.Status = "Restaurado."; }
        if (restart) MessageBox.Show("Alguns ajustes só voltam ao original depois de reiniciar o PC.", "GEOLAB OTIMIZA");
    }

    // ---------------- GEOLAB BENCH: benchmarks de jogo ----------------

    public sealed record MemRow(string Label, string Value, string Hint);
    private bool _secondaryBenchRunning;
    private GpuGameBench? _gpuBench;
    private bool _gpuBenchActive;
    private CancellationTokenSource? _secondaryBenchCts;
    private System.Windows.Media.Imaging.WriteableBitmap? _simBitmap;
    private int[] _simPixels = Array.Empty<int>();
    private const int SimView = 440;

    private bool AnyBenchRunning => BenchRunning || _secondaryBenchRunning;

    private void BenchTest_Checked(object sender, RoutedEventArgs e)
    {
        if (BenchPanelRender == null) return; // durante InitializeComponent
        string test = (string)((FrameworkElement)sender).Tag;
        BenchPanelRender.Visibility = test == "render" ? Visibility.Visible : Visibility.Collapsed;
        BenchPanelGpu.Visibility = test == "gpu" ? Visibility.Visible : Visibility.Collapsed;
        BenchPanelRt.Visibility = test == "rt" ? Visibility.Visible : Visibility.Collapsed;
        BenchPanelSim.Visibility = test == "sim" ? Visibility.Visible : Visibility.Collapsed;
        BenchPanelMem.Visibility = test == "mem" ? Visibility.Visible : Visibility.Collapsed;
        BenchPanelDisk.Visibility = test == "disk" ? Visibility.Visible : Visibility.Collapsed;
        if (test == "disk") InitDiskBench();
        BenchSub.Text = test switch
        {
            "gpu" => "Cena de jogo em Direct3D 11 para medir a placa de vídeo, no estilo 3DMark / Unigine",
            "rt" => "Ray tracing por hardware (DXR 1.1, Direct3D 12): usa os núcleos de RT da placa, com 3 passadas e nota pela mediana",
            "sim" => "Quanto FPS a CPU aguenta numa simulação de jogo com milhares de unidades",
            "mem" => "Latência e banda da RAM e velocidade de carregamento do disco",
            "disk" => "Velocidade de leitura e escrita do disco: sequencial e acessos aleatórios de 4 KB, no estilo CrystalDiskMark",
            _ => "Benchmark de CPU por renderização 3D (path tracing), no mesmo princípio do Cinebench",
        };
        UpdateGameBenchHistory();
    }

    private void SaveBenchRecord(string test, double score, string summary)
    {
        AddEvent("", "Benchmark concluído", summary, "");
        _settings.BenchRecords.Insert(0, new BenchRecord(DateTime.Now, test, score, summary));
        // Mantém os 10 mais recentes de cada teste.
        _settings.BenchRecords = _settings.BenchRecords.GroupBy(r => r.Test).SelectMany(g => g.Take(10))
                                          .OrderByDescending(r => r.Date).ToList();
        _settings.Save();
        UpdateGameBenchHistory();
    }

    private void UpdateGameBenchHistory()
    {
        List<MeterItem> For(string test)
        {
            var list = _settings.BenchRecords.Where(r => r.Test == test).ToList();
            double best = list.Count > 0 ? list.Max(r => r.Score) : 1;
            return list.Select(r => new MeterItem($"{r.Date:dd/MM HH:mm}", r.Summary, best > 0 ? r.Score / best : 0)).ToList();
        }
        GbHistory.ItemsSource = For("gpu");
        RtHistory.ItemsSource = RtHistoryItems();
        SimHistory.ItemsSource = For("sim");
        MemHistory.ItemsSource = For("mem");
        DiskHistory.ItemsSource = For("disk");
    }

    private void SetSecondaryBenchRunning(Button btn, bool running)
    {
        _secondaryBenchRunning = running;
        btn.Content = running ? "CANCELAR" : "INICIAR";
        btn.Style = (Style)FindResource(running ? "OutlineButton" : "PrimaryButton");
        BenchTestPanel.IsEnabled = !running;
    }

    // ----- Cena de jogo (GPU) -----

    private async void BtnGpuBench_Click(object sender, RoutedEventArgs e)
    {
        if (_secondaryBenchRunning) { _gpuBench?.Cancel(); return; }
        if (AnyBenchRunning || TestRunning) { GbStatus.Text = "Termine o outro teste antes."; return; }

        var size = (CheckedTag(GbResPanel) ?? "1920x1080").Split('x').Select(int.Parse).ToArray();
        int secs = int.Parse(CheckedTag(GbDurPanel) ?? "60");
        _gpuBench = new GpuGameBench
        {
            Width = size[0], Height = size[1], Duration = TimeSpan.FromSeconds(secs),
            AdapterName = _gpus.Count > 0 ? _gpus[0].Name : ""
        };
        SetSecondaryBenchRunning(BtnGpuBench, true);
        GbResult.ItemsSource = null;
        var bench = _gpuBench;
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(250), DispatcherPriority.Normal, (_, _) =>
        {
            GbFpsNow.Text = bench.CurrentFps > 0 ? $"{bench.CurrentFps:0}" : "—";
            GbProgress.ScaleX = bench.Progress;
            GbStatus.Text = $"Rodando · {size[0]}x{size[1]} · {bench.Progress:P0}";
        }, Dispatcher);
        timer.Start();
        _gpuBenchActive = true;
        GbGpuNow.Text = "sensores pausados";

        // Thread dedicada: a cena roda num loop próprio com prioridade alta.
        var r = await Task.Factory.StartNew(bench.Run, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        timer.Stop();
        _gpuBenchActive = false;
        GbGpuNow.Text = "—";
        SetSecondaryBenchRunning(BtnGpuBench, false);
        GbProgress.ScaleX = 0;
        GbFpsNow.Text = "—";

        if (r == null)
        {
            GbStatus.Text = bench.Cancelled ? "Cancelado" : $"Falhou: {bench.Error}";
            return;
        }
        GbStatus.Text = $"Concluído · {r.Adapter}";
        GbResult.ItemsSource = new[]
        {
            new KeyValuePair<string, string>("Nota", $"{r.Score:N0} pts"),
            new KeyValuePair<string, string>("FPS médio", $"{r.AvgFps:0.0}"),
            new KeyValuePair<string, string>("1% low", $"{r.Low1Fps:0.0}"),
            new KeyValuePair<string, string>("Resolução", $"{r.Width}x{r.Height}"),
            new KeyValuePair<string, string>("Frames", $"{r.Frames:N0}"),
        };
        SaveBenchRecord("gpu", r.Score, $"{r.Score:N0} pts · {r.AvgFps:0} FPS · {r.Height}p");
    }

    // ----- Simulação de jogo (CPU) -----

    private async void BtnSimBench_Click(object sender, RoutedEventArgs e)
    {
        if (_secondaryBenchRunning) { _secondaryBenchCts?.Cancel(); return; }
        if (AnyBenchRunning || TestRunning) { SimStatus.Text = "Termine o outro teste antes."; return; }

        int secs = int.Parse(CheckedTag(SimDurPanel) ?? "20");
        _simBitmap ??= new System.Windows.Media.Imaging.WriteableBitmap(SimView, SimView, 96, 96, PixelFormats.Bgra32, null);
        if (_simPixels.Length == 0) _simPixels = new int[SimView * SimView];
        SimImage.Source = _simBitmap;
        SimIdle.Visibility = Visibility.Collapsed;

        var sim = new GameSimBench();
        _secondaryBenchCts = new CancellationTokenSource();
        var ct = _secondaryBenchCts.Token;
        SetSecondaryBenchRunning(BtnSimBench, true);
        SimResult.ItemsSource = null;
        SimStatus.Text = "Aquecendo (2 s)…";
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Render, (_, _) =>
        {
            DrawSim(sim);
            SimProgress.ScaleX = sim.Progress;
            if (sim.FramesDone > 0) SimStatus.Text = $"Simulando · {sim.Progress:P0}";
            SimFpsNow.Text = sim.CurrentFps > 0 ? $"{sim.CurrentFps:0} FPS" : "";
        }, Dispatcher);
        timer.Start();

        var r = await Task.Run(() => sim.Run(TimeSpan.FromSeconds(secs), ct));
        timer.Stop();
        bool cancelled = ct.IsCancellationRequested;
        SetSecondaryBenchRunning(BtnSimBench, false);
        SimProgress.ScaleX = 0;
        SimFpsNow.Text = "";
        if (cancelled) { SimStatus.Text = "Cancelado"; return; }

        SimStatus.Text = $"Concluído · {r.Frames:N0} frames em {r.Seconds:0} s";
        SimResult.ItemsSource = new[]
        {
            new KeyValuePair<string, string>("FPS médio (nota)", $"{r.AvgFps:0}"),
            new KeyValuePair<string, string>("1% low", $"{r.Low1Fps:0}"),
            new KeyValuePair<string, string>("Thread principal", $"{r.MainThreadMs:0.00} ms/frame"),
            new KeyValuePair<string, string>("Física + IA (paralelo)", $"{r.ParallelMs:0.00} ms/frame"),
            new KeyValuePair<string, string>("Unidades", $"{GameSimBench.Entities:N0}"),
        };
        SaveBenchRecord("sim", r.Score, $"{r.AvgFps:0} FPS · 1% low {r.Low1Fps:0}");
    }

    /// <summary>Vista de cima da batalha: time A em branco, time B em cinza.</summary>
    private void DrawSim(GameSimBench sim)
    {
        Array.Fill(_simPixels, unchecked((int)0xFF050505));
        float[] xs = sim.ViewX, ys = sim.ViewY; byte[] team = sim.ViewTeam;
        float k = SimView / GameSimBench.World;
        for (int i = 0; i < GameSimBench.Entities; i++)
        {
            int px = (int)(xs[i] * k), py = (int)(ys[i] * k);
            if ((uint)px < SimView && (uint)py < SimView)
                _simPixels[py * SimView + px] = team[i] == 0 ? unchecked((int)0xFFF5F5F5) : unchecked((int)0xFF5A5A5A);
        }
        _simBitmap!.WritePixels(new Int32Rect(0, 0, SimView, SimView), _simPixels, SimView * 4, 0);
    }

    // ----- Memória e carregamento -----

    private async void BtnMemBench_Click(object sender, RoutedEventArgs e)
    {
        if (_secondaryBenchRunning) { _secondaryBenchCts?.Cancel(); return; }
        if (AnyBenchRunning || TestRunning) { MemStatus.Text = "Termine o outro teste antes."; return; }

        _secondaryBenchCts = new CancellationTokenSource();
        var ct = _secondaryBenchCts.Token;
        SetSecondaryBenchRunning(BtnMemBench, true);
        var rows = new List<MemRow>();
        void Show() => MemResult.ItemsSource = rows.ToList();
        (string Phase, double Frac) progress = ("", 0);
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Normal, (_, _) =>
        {
            MemStatus.Text = progress.Phase;
            MemProgress.ScaleX = progress.Frac;
        }, Dispatcher);
        timer.Start();

        try
        {
            progress = ("Medindo latência da RAM…", 0.05);
            double lat = await Task.Run(() => MemoryBench.MeasureLatencyNs(ct));
            ct.ThrowIfCancellationRequested();
            rows.Add(new MemRow("Latência da RAM", $"{lat:0.0} ns", "Acesso aleatório. Menor é melhor; pesa muito em jogos competitivos."));
            Show();

            progress = ("Medindo banda da RAM…", 0.2);
            var bw = await Task.Run(() => MemoryBench.MeasureBandwidth(ct));
            ct.ThrowIfCancellationRequested();
            rows.Add(new MemRow("Banda de leitura da RAM", $"{bw.Read:0.0} GB/s", "Todas as threads lendo em sequência."));
            rows.Add(new MemRow("Banda de cópia da RAM", $"{bw.Copy:0.0} GB/s", "Leitura + escrita."));
            Show();

            var load = await Task.Run(() => MemoryBench.MeasureLoading((phase, f) => progress = (phase, 0.35 + f * 0.6), ct));
            rows.Add(new MemRow($"SSD {load.Drive} · leitura sequencial", $"{load.SeqReadMBs:N0} MB/s", "Pacote de 512 MB, sem cache do Windows."));
            rows.Add(new MemRow("Assets pequenos", $"{load.FilesPerSec:N0} arq/s",
                                $"Mil arquivos de 64 KB com 8 threads ({load.SmallFilesMBs:N0} MB/s), como engines modernas."));
            rows.Add(new MemRow("Carregamento do mapa", $"{load.MapLoadSeconds:0.00} s", "Pacote + assets: quanto menor, mais rápido o jogo carrega."));
            Show();

            MemStatus.Text = "Concluído · arquivos de teste apagados";
            // Nota para a barra do histórico: mais rápido = maior (inverso da latência × velocidade de carga).
            SaveBenchRecord("mem", 1000 / lat + 10 / load.MapLoadSeconds,
                            $"{lat:0} ns · {bw.Read:0} GB/s · mapa {load.MapLoadSeconds:0.00} s");
        }
        catch (OperationCanceledException) { MemStatus.Text = "Cancelado (arquivos de teste apagados)"; }
        catch (Exception ex) { MemStatus.Text = $"Falhou: {ex.Message}"; }
        finally
        {
            timer.Stop();
            MemProgress.ScaleX = 0;
            SetSecondaryBenchRunning(BtnMemBench, false);
        }
    }

    // ---------------- Window message integration ----------------

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        System.Windows.Interop.HwndSource.FromHwnd(hwnd)?.AddHook(DeviceChangeHook);
        System.Windows.Interop.HwndSource.FromHwnd(hwnd)?.AddHook(MaximizeHook);
    }

    // ---- Maximizar sem cobrir a barra de tarefas (em qualquer lado da tela e em qualquer monitor) ----

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo { public NativePoint Reserved, MaxSize, MaxPosition, MinTrackSize, MaxTrackSize; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public int Flags; }

    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    /// <summary>
    /// Sem borda nativa, o Windows maximiza a janela para a tela inteira e a barra de tarefas (principalmente em cima ou
    /// nas laterais) cobre parte dela. Aqui a área máxima passa a ser a área útil do monitor onde a janela está.
    /// </summary>
    private IntPtr MaximizeHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != 0x0024) return IntPtr.Zero; // WM_GETMINMAXINFO
        try
        {
            var monitor = MonitorFromWindow(hwnd, 2); // MONITOR_DEFAULTTONEAREST
            var mi = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref mi))
            {
                var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
                info.MaxPosition = new NativePoint { X = mi.Work.Left - mi.Monitor.Left, Y = mi.Work.Top - mi.Monitor.Top };
                info.MaxSize = new NativePoint { X = mi.Work.Right - mi.Work.Left, Y = mi.Work.Bottom - mi.Work.Top };
                Marshal.StructureToPtr(info, lParam, true);
                handled = true;
            }
        }
        catch { }
        return IntPtr.Zero;
    }

    private IntPtr DeviceChangeHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == 0x0219) // WM_DEVICECHANGE
            OnDeviceChange(wParam.ToInt32(), lParam);
        return IntPtr.Zero;
    }

    // ---------------- Sensores ----------------

    private void AddChip(string label, string? category, bool isChecked = false)
    {
        var chip = new RadioButton
        {
            Content = label,
            GroupName = "Category",
            Style = (Style)FindResource("Chip"),
            IsChecked = isChecked
        };
        chip.Checked += (_, _) => { _category = category; ApplySensorFilter(); };
        CategoryChips.Children.Add(chip);
    }

    private void SensorSearch_Changed(object sender, TextChangedEventArgs e)
    {
        _search = SensorSearch.Text.Trim();
        ApplySensorFilter();
    }

    private void ApplySensorFilter()
    {
        if (_view == null) return;
        if (_category == null && _search.Length == 0) { _view.Filter = null; return; }
        _view.Filter = o =>
        {
            var r = (SensorRow)o;
            return (_category == null || r.Category == _category)
                && (_search.Length == 0
                    || r.Name.Contains(_search, StringComparison.OrdinalIgnoreCase)
                    || r.Hardware.Contains(_search, StringComparison.OrdinalIgnoreCase));
        };
    }

    // ---------------- Stress test ----------------

    private bool TestRunning => _stress.Running || _gpuBurnOn;

    private void BtnStress_Click(object sender, RoutedEventArgs e)
    {
        if (TestRunning) StopStress("Parado pelo usuário");
        else StartStress();
    }

    private void ChkRam_Click(object sender, RoutedEventArgs e)
    {
        if (RamFractionPanel != null) RamFractionPanel.IsEnabled = ChkRam.IsChecked == true;
    }

    private void ChkGpu_Click(object sender, RoutedEventArgs e)
    {
        if (GpuOptions != null) GpuOptions.IsEnabled = GpuTempLimit.IsEnabled = ChkGpu.IsChecked == true;
        if (PageStress != null) DrawChart(); // mostra/oculta a linha de limite da GPU
    }

    private static string? CheckedTag(Panel panel)
        => panel.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string;

    private void StartStress()
    {
        StressHint.Text = "";
        bool cpu = ChkCpu.IsChecked == true, ram = ChkRam.IsChecked == true, gpu = ChkGpu.IsChecked == true;
        if (!cpu && !ram && !gpu) { StressHint.Text = "Ative CPU, RAM ou GPU."; return; }

        if (!double.TryParse(TempLimit.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out _tempLimit)
            || _tempLimit < 40 || _tempLimit > 110)
        {
            StressHint.Text = "Limite da CPU deve estar entre 40 e 110 °C.";
            return;
        }
        if (gpu && (!double.TryParse(GpuTempLimit.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out _gpuTempLimit)
                    || _gpuTempLimit < 40 || _gpuTempLimit > 100))
        {
            StressHint.Text = "Limite da GPU deve estar entre 40 e 100 °C.";
            return;
        }
        if (!double.TryParse(TxtDuration.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double minutes) || minutes < 0)
        {
            StressHint.Text = "Duração inválida.";
            return;
        }
        if ((cpu || ram) && _monitor.CpuTemperature == null)
        {
            string why = SensorMonitor.IsPawnIOInstalled
                ? ""
                : "\n\nCausa provável: o driver PawnIO não está instalado. Instale com:\n" +
                  "winget install namazso.PawnIO\ne reabra o GEOLAB TI TOOLS.";
            var r = MessageBox.Show("Nenhum sensor de temperatura da CPU foi encontrado, então o corte automático " +
                                    "por temperatura da CPU NÃO funcionará. Continuar mesmo assim?" + why,
                                    "GEOLAB TI TOOLS", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
        }
        if (gpu && MainGpuTemp() == null)
        {
            var r = MessageBox.Show("Nenhum sensor de temperatura da GPU foi encontrado, então o corte automático " +
                                    "por temperatura da GPU NÃO funcionará. Continuar mesmo assim?",
                                    "GEOLAB TI TOOLS", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;
        }

        double fraction = double.Parse(CheckedTag(RamFractionPanel) ?? "0.6", CultureInfo.InvariantCulture);

        _stressDuration = TimeSpan.FromMinutes(minutes);
        _stressStart = DateTime.Now;
        _stressMaxTemp = 0;
        _gpuMaxTemp = 0;
        _tempHistory.Clear();
        _gpuTempHistory.Clear();

        if (cpu || ram) _stress.Start(cpu, ram, fraction);

        string gpuDesc = "não";
        if (gpu)
        {
            string res = CheckedTag(GpuResPanel) ?? "1920x1080";
            int msaa = int.Parse(CheckedTag(GpuMsaaPanel) ?? "4");
            var intensity = (CheckedTag(GpuIntensityPanel) ?? "128,48").Split(',').Select(int.Parse).ToArray();
            bool full = res == "full";
            var size = full ? new[] { 0, 0 } : res.Split('x').Select(int.Parse).ToArray();
            _burn.Start(new GpuBurnSettings
            {
                Fullscreen = full, Width = size[0], Height = size[1], Msaa = msaa,
                Shells = intensity[0], Iterations = intensity[1],
                AdapterName = _gpus.Count > 0 ? _gpus[0].Name : ""
            });
            _gpuBurnOn = true;
            gpuDesc = $"sim ({(full ? "tela cheia" : res)}, {(msaa > 1 ? $"MSAA {msaa}x" : "sem MSAA")}, " +
                      $"{GpuIntensityPanel.Children.OfType<RadioButton>().First(r => r.IsChecked == true).Content}, limite {_gpuTempLimit:0} °C)";
        }

        SetStressControls(running: true);
        SetStatus("Rodando", alert: false);
        UpdateStressHardware();
        Log($"Iniciado · CPU {(cpu ? $"sim (limite {_tempLimit:0} °C)" : "não")} · RAM {(ram ? $"sim ({fraction:P0} da livre)" : "não")} · " +
            $"GPU {gpuDesc} · {(minutes > 0 ? $"{minutes:0.##} min" : "sem limite de tempo")}");
    }

    private void StopStress(string reason)
    {
        if (TestRunning) AddEvent("", "Stress test", reason, reason.StartsWith("ABORTADO") ? "abortado" : "ok");
        if (!TestRunning) return;
        var errors = _stress.MemoryErrors;
        var passes = _stress.MemoryPasses;
        bool hadRam = _stress.MemoryAllocatedBytes > 0;
        bool hadCpu = _stress.Running;
        _stress.Stop();

        string gpuSummary = "";
        if (_gpuBurnOn)
        {
            _burn.Stop();
            _gpuBurnOn = false;
            // "Pontuação" no estilo FurMark: frames renderizados e FPS médio.
            gpuSummary = $" · GPU: {_burn.Frames:N0} frames, média {_burn.AverageFps:0} FPS, temp. máx. {_gpuMaxTemp:0.#} °C";
        }
        SetStressControls(running: false);

        bool aborted = reason.StartsWith("ABORTADO");
        SetStatus(aborted ? "Abortado" : "Parado", alert: aborted);
        Log($"{reason} · duração {DateTime.Now - _stressStart:hh\\:mm\\:ss}" +
            (hadCpu ? $" · CPU temp. máx. {_stressMaxTemp:0.#} °C" : "") +
            (hadRam ? $" · RAM {passes} passes, {errors} erros" : "") + gpuSummary);
    }

    private void SetStressControls(bool running)
    {
        BtnStress.Content = running ? "PARAR" : "INICIAR";
        BtnStress.Style = (Style)FindResource(running ? "OutlineButton" : "PrimaryButton");
        ChkCpu.IsEnabled = ChkRam.IsEnabled = ChkGpu.IsEnabled = !running;
        RamFractionPanel.IsEnabled = !running && ChkRam.IsChecked == true;
        GpuOptions.IsEnabled = GpuTempLimit.IsEnabled = !running && ChkGpu.IsChecked == true;
        TempLimit.IsEnabled = TxtDuration.IsEnabled = !running;
        SideStress.Text = running ? "rodando" : "parado";
    }

    /// <summary>Alerta = cartão invertido (branco com texto preto).</summary>
    private void SetStatus(string text, bool alert)
    {
        StStatus.Text = text;
        StatusCard.Background = alert ? Fg : Card;
        StStatus.Foreground = alert ? Ink : Fg;
        StStatusLabel.Foreground = alert ? Ink : Muted;
    }

    /// <summary>Temperatura do núcleo da GPU principal (dedicada).</summary>
    private float? MainGpuTemp()
    {
        if (_gpus.Count == 0) return null;
        var temps = _monitor.Rows.Where(r => r.Hardware == _gpus[0].Name && r.Type == SensorType.Temperature).ToList();
        return (temps.FirstOrDefault(r => r.Name == "GPU Core") ?? temps.FirstOrDefault())?.RawValue;
    }

    /// <summary>Consumo da GPU principal: NVML quando disponível, senão LibreHardwareMonitor.</summary>
    private float? MainGpuPower()
    {
        if (_gpus.Count == 0) return null;
        var g = _gpus[0];
        if (g.NvmlHandle != IntPtr.Zero && Nvml.PowerUsageMw(g.NvmlHandle) is uint mw) return mw / 1000f;
        return _monitor.Rows.FirstOrDefault(r => r.Hardware == g.Name && r.Type == SensorType.Power)?.RawValue;
    }

    private string MainGpuPerfCap()
    {
        if (_gpus.Count == 0) return "";
        return _gpus[0].LiveExtras().FirstOrDefault(kv => kv.Key.StartsWith("PerfCap")).Value ?? "";
    }

    private void UpdateStressPanel()
    {
        float? temp = _monitor.CpuTemperature;
        float? load = _monitor.CpuLoad;
        float? power = _monitor.CpuPower;
        float? gTemp = MainGpuTemp();
        float? gPower = MainGpuPower();

        if (temp is float t)
        {
            _tempHistory.Add(t);
            if (_tempHistory.Count > HistoryLength) _tempHistory.RemoveAt(0);
            if (_stress.Running && t > _stressMaxTemp) _stressMaxTemp = t;
        }
        if (gTemp is float gt)
        {
            _gpuTempHistory.Add(gt);
            if (_gpuTempHistory.Count > HistoryLength) _gpuTempHistory.RemoveAt(0);
            if (_gpuBurnOn && gt > _gpuMaxTemp) _gpuMaxTemp = gt;
        }

        StTemp.Text = SideTemp.Text = temp == null ? "—" : $"{temp:0.#} °C";
        StTempMax.Text = _stressMaxTemp > 0 ? $"máx. {_stressMaxTemp:0.#} °C" : "máx. —";
        SideLoad.Text = load == null ? "—" : $"{load:0} %";
        StLoad.Text = load == null ? "carga —" : $"carga {load:0} %";
        StPower.Text = power == null ? "—" : $"{power:0.#} W";

        StGpuTemp.Text = gTemp == null ? "—" : $"{gTemp:0.#} °C";
        StGpuTempMax.Text = _gpuMaxTemp > 0 ? $"máx. {_gpuMaxTemp:0.#} °C" : "máx. —";
        StGpuPower.Text = gPower == null ? "—" : $"{gPower:0.#} W";
        string cap = MainGpuPerfCap();
        StGpuCap.Text = cap.Length > 0 ? $"limitador: {cap}" : "—";
        StFps.Text = _gpuBurnOn && _burn.Fps > 0 ? $"{_burn.Fps:0}" : "—";
        StFpsAvg.Text = _gpuBurnOn && _burn.AverageFps > 0 ? $"média {_burn.AverageFps:0}" : "média —";

        if (TestRunning)
        {
            var elapsed = DateTime.Now - _stressStart;
            StElapsed.Text = elapsed.ToString(@"hh\:mm\:ss");
            StRam.Text = _stress.MemoryAllocatedBytes > 0
                ? $"{_stress.MemoryPasses} / {_stress.MemoryErrors}"
                : "—";

            if (_stress.Running && temp >= _tempLimit)
                StopStress($"ABORTADO: CPU a {temp:0.#} °C atingiu o limite de {_tempLimit:0} °C");
            else if (_gpuBurnOn && gTemp >= _gpuTempLimit)
                StopStress($"ABORTADO: GPU a {gTemp:0.#} °C atingiu o limite de {_gpuTempLimit:0} °C");
            else if (_stress.MemoryErrors > 0)
                StopStress($"ABORTADO: {_stress.MemoryErrors} erros de memória detectados");
            else if (_gpuBurnOn && !_burn.Running)
                StopStress(_burn.Error != null ? $"ABORTADO: falha na GPU ({_burn.Error})"
                                               : "Janela da GPU fechada pelo usuário");
            else if (_stressDuration > TimeSpan.Zero && elapsed >= _stressDuration)
                StopStress("Concluído (tempo esgotado)");
        }

        if (PageStress.IsVisible) DrawChart();
    }

    /// <summary>Linha com CPU, GPU e RAM da máquina no topo do registro, para constar em capturas de tela.</summary>
    private void UpdateStressHardware()
    {
        var (total, _) = Optimizer.MemoryStatus();
        string gpus = _gpus.Count > 0 ? string.Join(" + ", _gpus.Select(g => g.Name)) : "—";
        string ram = total > 0 ? $"{total / 1073741824.0:0.#} GB" : "—";
        StressHw.Text = $"CPU: {(_cpuName.Length > 0 ? _cpuName : "—")}  ·  GPU: {gpus}  ·  RAM: {ram}";
    }

    private void Log(string msg)
    {
        StressLog.Items.Add($"{DateTime.Now:HH:mm:ss}  {msg}");
        StressLog.ScrollIntoView(StressLog.Items[^1]);
    }

    private void TempChart_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart();

    /// <summary>Desenha o histórico de temperatura da CPU (branco) e da GPU (cinza) com as linhas de limite.</summary>
    private void DrawChart()
    {
        TempChart.Children.Clear();
        double w = TempChart.ActualWidth, h = TempChart.ActualHeight;
        if (w < 50 || h < 40) return;

        const double left = 36, top = 6, bottom = 6;
        double plotW = w - left, plotH = h - top - bottom;
        double yMin = 20, yMax = Math.Max(110, Math.Max(_tempLimit, _gpuTempLimit) + 10);
        double Y(double v) => top + plotH * (1 - (Math.Clamp(v, yMin, yMax) - yMin) / (yMax - yMin));

        var gridBrush = (Brush)FindResource("Line");
        for (double v = yMin; v <= yMax; v += 20)
        {
            TempChart.Children.Add(new Line { X1 = left, X2 = w, Y1 = Y(v), Y2 = Y(v), Stroke = gridBrush });
            var lbl = new TextBlock { Text = $"{v:0}°", FontSize = 10, Foreground = (Brush)FindResource("Faint"),
                                      FontFamily = (FontFamily)FindResource("Mono") };
            Canvas.SetLeft(lbl, 0); Canvas.SetTop(lbl, Y(v) - 7);
            TempChart.Children.Add(lbl);
        }

        void LimitLine(double v, Brush b) => TempChart.Children.Add(new Line
        {
            X1 = left, X2 = w, Y1 = Y(v), Y2 = Y(v),
            Stroke = b, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 }
        });
        LimitLine(_tempLimit, Muted);
        if (ChkGpu.IsChecked == true) LimitLine(_gpuTempLimit, (Brush)FindResource("Faint"));

        double step = plotW / (HistoryLength - 1);
        void Series(List<double> data, Brush stroke, bool fill)
        {
            if (data.Count < 2) return;
            int offset = HistoryLength - data.Count; // alinha à direita
            var line = new Polyline { Stroke = stroke, StrokeThickness = 1.6, StrokeLineJoin = PenLineJoin.Round };
            for (int i = 0; i < data.Count; i++)
                line.Points.Add(new Point(left + (offset + i) * step, Y(data[i])));
            if (fill)
            {
                var area = new Polygon { Fill = (Brush)FindResource("GraphArea") };
                foreach (var p in line.Points) area.Points.Add(p);
                area.Points.Add(new Point(line.Points[^1].X, top + plotH));
                area.Points.Add(new Point(line.Points[0].X, top + plotH));
                TempChart.Children.Add(area);
            }
            TempChart.Children.Add(line);
        }
        Series(_gpuTempHistory, Muted, fill: false);
        Series(_tempHistory, Fg, fill: true);
    }
}
