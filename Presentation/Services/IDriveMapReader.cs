namespace ResetExplode.Presentation.Services;

public interface IDriveMapReader
{
    Task<IReadOnlyList<DriveCard>> ReadAsync(CancellationToken cancellationToken = default);
}
