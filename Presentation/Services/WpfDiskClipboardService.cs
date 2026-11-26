using System.Windows;
using GeolabTITools.Application.Features.Disks.Contracts;

namespace ResetExplode.Presentation.Services;

public sealed class WpfDiskClipboardService : IDiskClipboardService
{
    public void CopyText(string text) => Clipboard.SetText(text);
}
