using System.ComponentModel;

namespace ResetExplode.DiskAnalyzer;

/// <summary>Opção selecionável de ordenação ou filtro da tela de resultados.</summary>
public sealed class DiskAnalyzerChoice(string key, string label, bool isSelected = false) : INotifyPropertyChanged
{
    private bool _isSelected = isSelected;

    public string Key { get; } = key;
    public string Label { get; } = label;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
