using System.Runtime.InteropServices;
using GeolabTITools.Application.Features.Windows.Contracts;
using GeolabTITools.Application.Features.Windows.DTOs;

namespace GeolabTITools.Infrastructure.Windows.Updates;

/// <summary>Encapsulates the Windows Update Agent COM search and installation workflow.</summary>
public sealed class WindowsUpdateDriverService : IWindowsUpdateService, IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<string, object> _pendingUpdates = new(StringComparer.Ordinal);
    private bool _disposed;

    public Task<IReadOnlyList<WindowsUpdateDriverInfo>> SearchDriversAsync(CancellationToken cancellationToken = default) =>
        Task.Run<IReadOnlyList<WindowsUpdateDriverInfo>>(() => SearchDrivers(cancellationToken), cancellationToken);

    public Task<string> InstallDriversAsync(IReadOnlyList<WindowsUpdateDriverInfo> drivers, IProgress<string> progress,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => InstallDrivers(drivers, progress, cancellationToken), cancellationToken);

    private IReadOnlyList<WindowsUpdateDriverInfo> SearchDrivers(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            ReleasePendingUpdates();
            dynamic? session = null, searcher = null, result = null, updates = null;
            var found = new List<WindowsUpdateDriverInfo>();
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                session = CreateCom("Microsoft.Update.Session");
                searcher = session.CreateUpdateSearcher();
                result = searcher.Search("IsInstalled=0 and Type='Driver' and IsHidden=0");
                updates = result.Updates;
                int count = Convert.ToInt32(updates.Count);
                for (int index = 0; index < count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    object update = updates.Item(index);
                    try
                    {
                        dynamic item = update;
                        string token = Guid.NewGuid().ToString("N");
                        string title = Convert.ToString(item.Title) ?? "";
                        long size = Convert.ToInt64(item.MaxDownloadSize);
                        _pendingUpdates.Add(token, update);
                        found.Add(new WindowsUpdateDriverInfo(token, title, size));
                        update = null!; // ownership moved to the pending-selection map
                    }
                    finally { ReleaseComObject(update); }
                }
                return found;
            }
            catch
            {
                ReleasePendingUpdates();
                throw;
            }
            finally
            {
                ReleaseComObject(updates);
                ReleaseComObject(result);
                ReleaseComObject(searcher);
                ReleaseComObject(session);
            }
        }
    }

    private string InstallDrivers(IReadOnlyList<WindowsUpdateDriverInfo> drivers, IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            dynamic? session = null, collection = null, downloader = null, installer = null, result = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                session = CreateCom("Microsoft.Update.Session");
                collection = CreateCom("Microsoft.Update.UpdateColl");
                foreach (var driver in drivers)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!_pendingUpdates.TryGetValue(driver.SelectionToken, out object? update))
                        throw new InvalidOperationException("O resultado de busca do Windows Update expirou. Faça uma nova busca.");
                    dynamic selected = update;
                    try { selected.AcceptEula(); } catch { }
                    collection.Add(update);
                }

                cancellationToken.ThrowIfCancellationRequested();
                progress.Report("Baixando drivers do Windows Update…");
                downloader = session.CreateUpdateDownloader();
                downloader.Updates = collection;
                downloader.Download();

                cancellationToken.ThrowIfCancellationRequested();
                progress.Report("Instalando…");
                installer = session.CreateUpdateInstaller();
                installer.Updates = collection;
                result = installer.Install();
                int code = Convert.ToInt32(result.ResultCode); // 2 = sucesso, 3 = sucesso com erros
                bool reboot = Convert.ToBoolean(result.RebootRequired);
                string message = code is 2 or 3 ? "Instalação concluída." : $"Instalação terminou com código {code}.";
                return message + (reboot ? " Reinicie o PC para finalizar." : "");
            }
            finally
            {
                ReleaseComObject(result);
                ReleaseComObject(installer);
                ReleaseComObject(downloader);
                ReleaseComObject(collection);
                ReleaseComObject(session);
                ReleasePendingUpdates();
            }
        }
    }

    private static object CreateCom(string progId) =>
        Activator.CreateInstance(Type.GetTypeFromProgID(progId)
            ?? throw new InvalidOperationException("Serviço do Windows Update indisponível."))!;

    private void ReleasePendingUpdates()
    {
        foreach (object update in _pendingUpdates.Values) ReleaseComObject(update);
        _pendingUpdates.Clear();
    }

    private static void ReleaseComObject(object? value)
    {
        if (value != null && Marshal.IsComObject(value))
        {
            try { Marshal.FinalReleaseComObject(value); } catch { }
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            ReleasePendingUpdates();
            _disposed = true;
        }
    }
}
