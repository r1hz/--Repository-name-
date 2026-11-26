namespace ResetExplode.Presentation.Services;

public sealed class DriveMapReader : IDriveMapReader
{
    public Task<IReadOnlyList<DriveCard>> ReadAsync(CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<DriveCard>>(DriveMap.Read, cancellationToken);
}
