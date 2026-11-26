using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GeolabTITools.Application.Features.Disks.DTOs;

namespace ResetExplode.DiskAnalyzer;

/// <summary>Renderiza os blocos diretamente em uma única superfície WPF para limitar o custo visual.</summary>
public sealed class TreemapView : FrameworkElement
{
    private static readonly string[] Palette =
    {
        "#145B8C", "#126B63", "#5C4A94", "#8A4A15", "#3F6A2A",
        "#8A355A", "#4B6072", "#6B5B20", "#3C5B87", "#7A423B",
    };

    private readonly Dictionary<string, Brush> _brushCache = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<TreemapLayoutItem> _layout = Array.Empty<TreemapLayoutItem>();
    private Dictionary<string, string> _categoryMap = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<LegendItem> _legend = Array.Empty<LegendItem>();
    private Size _layoutSize;
    private bool _layoutDirty = true;
    private TreemapTile? _hoveredTile;

    public static readonly DependencyProperty TilesProperty = DependencyProperty.Register(
        nameof(Tiles), typeof(IReadOnlyList<TreemapTile>), typeof(TreemapView),
        new FrameworkPropertyMetadata(Array.Empty<TreemapTile>(), OnTilesChanged));

    public static readonly DependencyProperty SelectedFileProperty = DependencyProperty.Register(
        nameof(SelectedFile), typeof(FileEntry), typeof(TreemapView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnRenderPropertyChanged));

    public static readonly DependencyProperty SurfaceBrushProperty = BrushProperty(nameof(SurfaceBrush));
    public static readonly DependencyProperty TileBorderBrushProperty = BrushProperty(nameof(TileBorderBrush));
    public static readonly DependencyProperty TileTextBrushProperty = BrushProperty(nameof(TileTextBrush));
    public static readonly DependencyProperty LegendTextBrushProperty = BrushProperty(nameof(LegendTextBrush));
    public static readonly DependencyProperty AccentBrushProperty = BrushProperty(nameof(AccentBrush));
    public static readonly DependencyProperty MutedBrushProperty = BrushProperty(nameof(MutedBrush));

    public event EventHandler<TreemapFolderEventArgs>? FolderActivated;

    public TreemapView()
    {
        Focusable = true;
        ClipToBounds = true;
        ToolTipService.SetInitialShowDelay(this, 350);
        ToolTipService.SetShowDuration(this, 30000);
    }

    public IReadOnlyList<TreemapTile> Tiles
    {
        get => (IReadOnlyList<TreemapTile>)GetValue(TilesProperty);
        set => SetValue(TilesProperty, value);
    }

    public FileEntry? SelectedFile
    {
        get => (FileEntry?)GetValue(SelectedFileProperty);
        set => SetValue(SelectedFileProperty, value);
    }

    public Brush? SurfaceBrush { get => (Brush?)GetValue(SurfaceBrushProperty); set => SetValue(SurfaceBrushProperty, value); }
    public Brush? TileBorderBrush { get => (Brush?)GetValue(TileBorderBrushProperty); set => SetValue(TileBorderBrushProperty, value); }
    public Brush? TileTextBrush { get => (Brush?)GetValue(TileTextBrushProperty); set => SetValue(TileTextBrushProperty, value); }
    public Brush? LegendTextBrush { get => (Brush?)GetValue(LegendTextBrushProperty); set => SetValue(LegendTextBrushProperty, value); }
    public Brush? AccentBrush { get => (Brush?)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public Brush? MutedBrush { get => (Brush?)GetValue(MutedBrushProperty); set => SetValue(MutedBrushProperty, value); }

    private static DependencyProperty BrushProperty(string name)
        => DependencyProperty.Register(name, typeof(Brush), typeof(TreemapView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private static void OnRenderPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((TreemapView)d).InvalidateVisual();

    private static void OnTilesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var view = (TreemapView)d;
        view._layoutDirty = true;
        view.RebuildLegend();
        view.InvalidateVisual();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        _layoutDirty = true;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var viewport = new Rect(0, 0, ActualWidth, ActualHeight);
        drawingContext.DrawRectangle(SurfaceBrush ?? Brushes.Transparent, null, viewport);
        if (ActualWidth <= 1 || ActualHeight <= 1) return;

        double legendHeight = _legend.Count > 0 ? 34 : 0;
        var treemapBounds = new Rect(0, 0, ActualWidth, Math.Max(0, ActualHeight - legendHeight));
        if (_layoutDirty || _layoutSize != treemapBounds.Size)
        {
            _layout = SquarifiedTreemapLayout.Arrange(Tiles, treemapBounds);
            _layoutSize = treemapBounds.Size;
            _layoutDirty = false;
        }

        if (_layout.Count == 0)
        {
            DrawLabel(drawingContext, "Sem arquivos com espaço ocupado nesta pasta.",
                new Point(14, Math.Max(12, treemapBounds.Height / 2)), 13, LegendTextBrush ?? Brushes.Gray);
        }
        else
        {
            foreach (var item in _layout) DrawTile(drawingContext, item);
        }

        if (_legend.Count > 0) DrawLegend(drawingContext, ActualHeight - legendHeight + 7);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var tile = FindTile(e.GetPosition(this))?.Tile;
        if (ReferenceEquals(tile, _hoveredTile)) return;
        _hoveredTile = tile;
        ToolTip = tile is null ? null : BuildToolTip(tile);
        Cursor = tile is null || tile.IsAggregate ? Cursors.Arrow : Cursors.Hand;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hoveredTile = null;
        ToolTip = null;
        Cursor = Cursors.Arrow;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var tile = FindTile(e.GetPosition(this))?.Tile;
        if (tile?.File is { } file)
        {
            SetCurrentValue(SelectedFileProperty, file);
            Focus();
            e.Handled = true;
        }
        else if (tile?.Folder is { } folder)
        {
            FolderActivated?.Invoke(this, new TreemapFolderEventArgs(folder));
            e.Handled = true;
        }
    }

    private void DrawTile(DrawingContext context, TreemapLayoutItem item)
    {
        Rect rect = item.Bounds;
        if (rect.Width < 0.5 || rect.Height < 0.5) return;

        string category = GetVisibleCategory(item.Tile.CategoryKey);
        Brush fill = GetCategoryBrush(category);
        bool selected = item.Tile.File is not null && ReferenceEquals(item.Tile.File, SelectedFile);
        bool hovered = ReferenceEquals(item.Tile, _hoveredTile);
        Brush border = selected || hovered ? AccentBrush ?? Brushes.White : TileBorderBrush ?? Brushes.Black;
        context.DrawRectangle(fill, new Pen(border, selected || hovered ? 2 : 0.8), rect);

        if (rect.Width >= 58 && rect.Height >= 23)
        {
            context.PushClip(new RectangleGeometry(rect));
            DrawLabel(context, item.Tile.Label, new Point(rect.X + 5, rect.Y + 3), 11, TileTextBrush ?? Brushes.White);
            if (rect.Width >= 100 && rect.Height >= 39)
                DrawLabel(context, item.Tile.SizeText, new Point(rect.X + 5, rect.Y + 18), 10, TileTextBrush ?? Brushes.White);
            context.Pop();
        }
    }

    private void DrawLegend(DrawingContext context, double y)
    {
        double x = 8;
        foreach (var item in _legend)
        {
            var swatch = new Rect(x, y + 2, 12, 12);
            context.DrawRectangle(GetCategoryBrush(item.Key), null, swatch);
            var text = CreateText(item.Label, 10, LegendTextBrush ?? Brushes.Gray);
            context.DrawText(text, new Point(x + 17, y));
            x += 25 + text.Width + 14;
            if (x > ActualWidth - 60) break;
        }
    }

    private void DrawLabel(DrawingContext context, string label, Point origin, double fontSize, Brush brush)
        => context.DrawText(CreateText(label, fontSize, brush), origin);

    private FormattedText CreateText(string label, double fontSize, Brush brush)
        => new(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), fontSize, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private TreemapLayoutItem? FindTile(Point point)
    {
        for (int index = _layout.Count - 1; index >= 0; index--)
            if (_layout[index].Bounds.Contains(point)) return _layout[index];
        return null;
    }

    private string BuildToolTip(TreemapTile tile)
    {
        if (tile.File is { } file)
            return $"{file.Name}\nCaminho: {file.FullPath}\nExtensão: {file.Extension}\nTamanho: {file.SizeText}\nPasta: {file.FolderPath}";
        if (tile.Folder is { } folder)
            return $"Pasta: {folder.Name}\nCaminho: {folder.FullPath}\nTamanho: {folder.TotalSizeText}\nArquivos: {folder.FileCount:N0}\nSubpastas: {folder.SubfolderCount:N0}\nClique para entrar";
        return $"{tile.Label}\nTamanho agrupado: {tile.SizeText}\nItens agrupados: {tile.GroupedItemsCount:N0}\nUse a árvore de pastas para navegar em pastas agrupadas.";
    }

    private void RebuildLegend()
    {
        _brushCache.Clear();
        var categories = Tiles.Where(tile => tile.SizeBytes > 0)
            .GroupBy(tile => tile.CategoryKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => new { Key = group.Key, Bytes = group.Sum(tile => (double)tile.SizeBytes) })
            .OrderByDescending(group => group.Bytes)
            .ToList();
        var visible = categories.Take(7).ToList();
        _categoryMap = visible.ToDictionary(group => group.Key, group => group.Key, StringComparer.OrdinalIgnoreCase);
        if (categories.Count > visible.Count)
            foreach (var hidden in categories.Skip(visible.Count)) _categoryMap[hidden.Key] = "__outras_categorias";

        var legend = visible.Select(group => new LegendItem(group.Key, LegendLabel(group.Key))).ToList();
        if (categories.Count > visible.Count) legend.Add(new LegendItem("__outras_categorias", "Outras categorias"));
        _legend = legend;
    }

    private string GetVisibleCategory(string category)
        => _categoryMap.TryGetValue(category, out string? visible) ? visible : category;

    private Brush GetCategoryBrush(string category)
    {
        if (_brushCache.TryGetValue(category, out var brush)) return brush;
        int hash = unchecked((int)2166136261u);
        foreach (char character in category) hash = unchecked((hash ^ char.ToUpperInvariant(character)) * 16777619);
        string color = Palette[(hash & int.MaxValue) % Palette.Length];
        brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        _brushCache[category] = brush;
        return brush;
    }

    private static string LegendLabel(string category)
        => category.StartsWith('.') ? category.ToUpperInvariant() : category;

    private sealed record LegendItem(string Key, string Label);
}
