using GeolabTITools.Application.Features.Windows.DTOs;

namespace GeolabTITools.Application.Features.Windows.Contracts;

public interface IShortcutReader
{
    ShortcutDetails? Read(string shortcutPath);
}
