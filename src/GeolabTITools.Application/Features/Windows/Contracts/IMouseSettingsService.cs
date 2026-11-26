namespace GeolabTITools.Application.Features.Windows.Contracts;

/// <summary>Applies the three Windows mouse acceleration parameters immediately for the current user session.</summary>
public interface IMouseSettingsService
{
    void ApplyMouseParameters(int speed, int threshold1, int threshold2);
}
