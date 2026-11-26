using System.Runtime.InteropServices;
using GeolabTITools.Application.Features.Windows.Contracts;

namespace GeolabTITools.Infrastructure.Windows;

public sealed class WindowsMouseSettingsService : IMouseSettingsService
{
    public void ApplyMouseParameters(int speed, int threshold1, int threshold2)
    {
        int[] parameters = { speed, threshold1, threshold2 };
        _ = SystemParametersInfo(0x0004 /* SPI_SETMOUSE */, 0, parameters, 0x01 | 0x02);
    }

    [DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(uint action, uint parameter, int[] data, uint updateFlags);
}
