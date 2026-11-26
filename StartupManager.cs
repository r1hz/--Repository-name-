using System.IO;
using GeolabTITools.Application.Features.Windows.Contracts;
using GeolabTITools.Application.Features.Windows.DTOs;

namespace ResetExplode;

/// <summary>Programa que abre junto com o Windows (chave Run do registro ou atalho na pasta Inicializar).</summary>
public sealed class StartupEntry
{
    public string Name { get; init; } = "";
    public string Command { get; init; } = "";
    public string Source { get; init; } = "";          // ex.: "Registro · usuário"
    public bool Machine { get; init; }                 // HKLM / pasta comum: precisa de administrador
    public bool Enabled { get; set; }
    public string Detail { get; init; } = "";          // descrição · fabricante
    public bool FileMissing { get; init; }

    internal string Kind { get; init; } = "";          // Run | Run32 | Folder
    internal string? FilePath { get; init; }           // só para Folder

    public string State => FileMissing ? "ARQUIVO NÃO EXISTE" : Enabled ? "ATIVADO" : "DESATIVADO";
}

/// <summary>Item removido, guardado para "Restaurar removidos".</summary>
public sealed class StartupBackup
{
    public string Kind { get; set; } = "";
    public bool Machine { get; set; }
    public string Name { get; set; } = "";
    public string? Command { get; set; }   // registro
    public string? Backup { get; set; }    // pasta: cópia do atalho em %AppData%\ResetExplode\Startup
    public string? Original { get; set; }  // pasta: caminho original
}

/// <summary>
/// Lista, ativa/desativa e remove programas de inicialização. Desativar usa as mesmas chaves StartupApproved do
/// Gerenciador de Tarefas (a entrada continua lá e pode ser religada); remover apaga a entrada, com cópia para desfazer.
/// </summary>
public sealed class StartupManager(
    IRegistryAccess registry,
    IFileSystemService fileSystem,
    IShortcutReader shortcutReader,
    IFileMetadataService fileMetadata)
{
    private readonly IRegistryAccess _registry = registry;
    private readonly IFileSystemService _fileSystem = fileSystem;
    private readonly IShortcutReader _shortcutReader = shortcutReader;
    private readonly IFileMetadataService _fileMetadata = fileMetadata;
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";
    public static string BackupFolder => Path.Combine(AppSettings.Folder, "Startup");

    public List<StartupEntry> List()
    {
        var list = new List<StartupEntry>();
        ReadRun(list, RegistryRoot.CurrentUser, RegistryViewMode.Default, "Run", false, "Registro · usuário");
        ReadRun(list, RegistryRoot.LocalMachine, RegistryViewMode.Registry64, "Run", true, "Registro · todos os usuários");
        ReadRun(list, RegistryRoot.LocalMachine, RegistryViewMode.Registry32, "Run32", true, "Registro · todos os usuários (32 bits)");
        ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), false, "Pasta Inicializar · usuário");
        ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), true, "Pasta Inicializar · todos os usuários");
        return list.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private void ReadRun(List<StartupEntry> list, RegistryRoot root, RegistryViewMode view, string kind, bool machine, string source)
    {
        try
        {
            foreach (var value in _registry.EnumerateValues(new RegistryKeyAddress(root, RunPath, view)))
            {
                var name = value.Name;
                if (string.IsNullOrEmpty(name)) continue;
                // Match RegistryKey.GetValue's original environment-variable expansion behavior for Run entries.
                string cmd = _registry.ReadValue(new RegistryAddress(root, RunPath, name, view))?.ToString() ?? "";
                string? exe = ExeFromCommand(cmd);
                list.Add(new StartupEntry
                {
                    Name = name, Command = cmd, Source = source, Machine = machine, Kind = kind,
                    Enabled = IsApproved(machine, kind, name),
                    Detail = Describe(exe), FileMissing = exe != null && !_fileSystem.FileExists(exe),
                });
            }
        }
        catch { }
    }

    private void ReadFolder(List<StartupEntry> list, string folder, bool machine, string source)
    {
        try
        {
            if (string.IsNullOrEmpty(folder) || !_fileSystem.DirectoryExists(folder)) return;
            foreach (var file in _fileSystem.EnumerateFiles(folder, "*"))
            {
                string fn = Path.GetFileName(file);
                if (fn.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                var shortcut = file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? _shortcutReader.Read(file) : null;
                string? target = shortcut == null ? null : string.IsNullOrEmpty(shortcut.Arguments)
                    ? shortcut.TargetPath
                    : $"\"{shortcut.TargetPath}\" {shortcut.Arguments}";
                string? exe = target != null ? ExeFromCommand(target) : file;
                list.Add(new StartupEntry
                {
                    Name = Path.GetFileNameWithoutExtension(fn), Command = target ?? file, Source = source, Machine = machine,
                    Kind = "Folder", FilePath = file,
                    Enabled = IsApproved(machine, "StartupFolder", fn),
                    Detail = Describe(exe), FileMissing = target != null && exe != null && !_fileSystem.FileExists(exe),
                });
            }
        }
        catch { }
    }

    // ----- ativar / desativar (StartupApproved, como o Gerenciador de Tarefas) -----

    private static string ApprovedKind(StartupEntry e) => e.Kind == "Folder" ? "StartupFolder" : e.Kind;
    private static string ApprovedName(StartupEntry e) => e.Kind == "Folder" ? Path.GetFileName(e.FilePath!) : e.Name;

    private bool IsApproved(bool machine, string kind, string name)
    {
        try
        {
            // 02/06 = ativado, 03/07 = desativado; sem valor = ativado
            return _registry.ReadValue(Address(machine, ApprovedPath + kind, name)) is not byte[] { Length: > 0 } b || (b[0] & 1) == 0;
        }
        catch { return true; }
    }

    public void SetEnabled(StartupEntry e, bool enabled)
    {
        var data = new byte[12];
        data[0] = enabled ? (byte)2 : (byte)3;
        if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
        var keyAddress = new RegistryKeyAddress(e.Machine ? RegistryRoot.LocalMachine : RegistryRoot.CurrentUser, ApprovedPath + ApprovedKind(e));
        _registry.CreateKey(keyAddress);
        _registry.WriteValue(Address(e.Machine, ApprovedPath + ApprovedKind(e), ApprovedName(e)), data, RegistryDataKind.Binary);
        e.Enabled = enabled;
    }

    // ----- remover / restaurar -----

    public StartupBackup Remove(StartupEntry e)
    {
        StartupBackup b;
        if (e.Kind == "Folder")
        {
            _fileSystem.CreateDirectory(BackupFolder);
            string copy = Path.Combine(BackupFolder, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Path.GetFileName(e.FilePath!)}");
            _fileSystem.MoveFile(e.FilePath!, copy);
            b = new StartupBackup { Kind = e.Kind, Machine = e.Machine, Name = e.Name, Backup = copy, Original = e.FilePath };
        }
        else
        {
            var address = new RegistryAddress(e.Machine ? RegistryRoot.LocalMachine : RegistryRoot.CurrentUser,
                RunPath, e.Name, e.Kind == "Run32" ? RegistryViewMode.Registry32 : e.Machine ? RegistryViewMode.Registry64 : RegistryViewMode.Default);
            if (!_registry.DeleteValue(address)) throw new InvalidOperationException("Chave Run ou valor não encontrado.");
            b = new StartupBackup { Kind = e.Kind, Machine = e.Machine, Name = e.Name, Command = e.Command };
        }
        try
        {
            _registry.DeleteValue(Address(e.Machine, ApprovedPath + ApprovedKind(e), ApprovedName(e)));
        }
        catch { }
        return b;
    }

    public void Restore(StartupBackup b)
    {
        if (b.Kind == "Folder")
        {
            if (b.Backup == null || b.Original == null || !_fileSystem.FileExists(b.Backup)) return;
            _fileSystem.CreateDirectory(Path.GetDirectoryName(b.Original)!);
            _fileSystem.MoveFile(b.Backup, b.Original, overwrite: true);
            return;
        }
        var address = new RegistryAddress(b.Machine ? RegistryRoot.LocalMachine : RegistryRoot.CurrentUser,
            RunPath, b.Name, b.Kind == "Run32" ? RegistryViewMode.Registry32 : b.Machine ? RegistryViewMode.Registry64 : RegistryViewMode.Default);
        _registry.WriteValue(address, b.Command ?? "", RegistryDataKind.String);
    }

    // ----- utilidades -----

    /// <summary>Extrai o executável da linha de comando ("C:\x\a.exe" -min  →  C:\x\a.exe).</summary>
    public string? ExeFromCommand(string cmd)
    {
        cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
        if (cmd.Length == 0) return null;
        string path;
        if (cmd[0] == '"')
        {
            int end = cmd.IndexOf('"', 1);
            if (end <= 1) return null;
            path = cmd[1..end];
        }
        else
        {
            int exe = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            int sp = cmd.IndexOf(' ');
            path = exe > 0 ? cmd[..(exe + 4)] : sp > 0 ? cmd[..sp] : cmd;
        }
        if (Path.IsPathRooted(path)) return path;
        // "rundll32.exe ..." e afins: procura no System32; se não achar, não dá para saber onde está
        string sys = Path.Combine(Environment.SystemDirectory, path);
        return _fileSystem.FileExists(sys) ? sys : null;
    }

    private string Describe(string? exe)
    {
        try
        {
            if (exe == null || !_fileSystem.FileExists(exe)) return "";
            var metadata = _fileMetadata.Read(exe);
            return string.Join(" · ", new[] { metadata.Description, metadata.CompanyName }
                .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!.Trim()).Distinct());
        }
        catch { return ""; }
    }

    private static RegistryAddress Address(bool machine, string path, string name) =>
        new(machine ? RegistryRoot.LocalMachine : RegistryRoot.CurrentUser, path, name);
}
