using System.Windows;
using System.Windows.Controls;

namespace ResetExplode.DiskAnalyzer;

public partial class DiskAnalyzerView : UserControl
{
    public DiskAnalyzerView()
    {
        InitializeComponent();
        DataContext = new DiskAnalyzerViewModel(new DiskInfoService(), new DiskScannerService());
        Loaded += async (_, _) =>
        {
            try
            {
                await ((DiskAnalyzerViewModel)DataContext).RefreshDrivesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Não foi possível carregar as unidades: {ex.Message}",
                    "Analisador de Disco", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
    }

    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is DiskAnalyzerViewModel viewModel)
            viewModel.SelectFolder(e.NewValue as FolderEntry);
    }

    private void TreemapView_FolderActivated(object? sender, TreemapFolderEventArgs e)
    {
        if (DataContext is DiskAnalyzerViewModel viewModel)
            viewModel.EnterTreemapFolder(e.Folder);
    }
}
