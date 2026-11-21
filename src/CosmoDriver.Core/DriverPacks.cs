using System.Security.Cryptography;
using System.Text;

namespace CosmoDriver.Core;

public sealed record PackCandidate(string DeviceId, string DeviceName, string HardwareId, string Description,
    string Provider, string Version, string Date, string InfPath, uint Rank, bool Signed, string Signer, string InfSha256)
{
    public string Signature => Signed ? "Verificata" : "Non verificata · bloccato";
    public bool Matches(string query) => new[] { DeviceName, HardwareId, Description, Provider, Version }
        .Any(s => s.Contains(query, StringComparison.OrdinalIgnoreCase));
}
public sealed record PackFile(string RelativePath, long Size, string Sha256);
public sealed record PackInstallRequest(string DeviceId, string InfPath, string SourceDirectory,
    List<PackFile> Files, bool UserConfirmed, string ExpectedInfSha256);
public sealed record AdminRequest(string Operation, InstallRequest? WindowsUpdate = null,
    PackInstallRequest? Package = null, bool CreateRestorePoint = true);
public sealed record RestorePointResult(bool Success, long? Sequence, bool Created, string Message);

public static class PackPolicy
{
    public const int MaxFiles = 30000;
    public const long MaxBytes = 2L * 1024 * 1024 * 1024;

    public static string LocalDirectory(string path)
    {
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (full.StartsWith("\\\\", StringComparison.Ordinal) || full.Length < 3 || full[1] != ':' || !Directory.Exists(full))
            throw new InvalidDataException("Seleziona una cartella su un disco locale o USB.");
        for (DirectoryInfo? current = new(full); current is not null; current = current.Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Collegamenti simbolici e junction non sono accettati per i pacchetti.");
        return full;
    }

    public static string ContainedPath(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':'))
            throw new InvalidDataException("Percorso del pacchetto non valido.");
        string full = Path.GetFullPath(Path.Combine(root, relative));
        string canonicalRoot = Path.GetFullPath(root);
        string prefix = Path.EndsInDirectorySeparator(canonicalRoot) ? canonicalRoot : canonicalRoot + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Il file esce dalla cartella del pacchetto.");
        return full;
    }

    public static IEnumerable<string> EnumerateFiles(string directory, CancellationToken token = default)
    {
        var pending = new Stack<(string Path, int Depth)>(); pending.Push((LocalDirectory(directory), 0));
        int count = 0;
        while (pending.Count != 0)
        {
            var current = pending.Pop();
            if (current.Depth > 40) throw new InvalidDataException("Pacchetto con troppe sottocartelle.");
            foreach (string entry in Directory.EnumerateFileSystemEntries(current.Path))
            {
                token.ThrowIfCancellationRequested();
                var attrs = File.GetAttributes(entry);
                if ((attrs & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Il pacchetto contiene un collegamento simbolico o junction.");
                if (++count > MaxFiles) throw new InvalidDataException("Troppi file nel pacchetto. Seleziona una cartella più specifica.");
                if ((attrs & FileAttributes.Directory) != 0) pending.Push((entry, current.Depth + 1));
                else yield return entry;
            }
        }
    }

    public static List<PackFile> Snapshot(string directory, CancellationToken token = default)
    {
        string root = LocalDirectory(directory); List<PackFile> result = []; long total = 0;
        foreach (string file in EnumerateFiles(root, token).Order(StringComparer.OrdinalIgnoreCase))
        {
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            if ((total += stream.Length) > MaxBytes) throw new InvalidDataException("Il pacchetto supera 2 GB. Seleziona la cartella di un singolo driver.");
            result.Add(new(Path.GetRelativePath(root, file), stream.Length, Convert.ToHexString(SHA256.HashData(stream))));
        }
        return result;
    }

    public static void Validate(PackInstallRequest request)
    {
        string root = LocalDirectory(request.SourceDirectory);
        if (!request.UserConfirmed || string.IsNullOrWhiteSpace(request.DeviceId) || request.DeviceId.Length > 4096 ||
            request.Files is null || request.Files.Count is 0 or > MaxFiles ||
            !Path.GetExtension(request.InfPath).Equals(".inf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Selezione del pacchetto non valida o non confermata.");
        string relativeInf = Path.GetRelativePath(root, Path.GetFullPath(request.InfPath));
        _ = ContainedPath(root, relativeInf);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var file in request.Files)
        {
            if (file is null || !names.Add(file.RelativePath) || file.Size is < 0 or > MaxBytes || (total += file.Size) > MaxBytes ||
                file.Sha256?.Length != 64 || !file.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException("Inventario pacchetto non valido.");
            _ = ContainedPath(root, file.RelativePath);
        }
        if (!names.Contains(relativeInf)) throw new InvalidDataException("INF assente dall'inventario confermato.");
        if (!request.Files.Single(f => f.RelativePath.Equals(relativeInf, StringComparison.OrdinalIgnoreCase)).Sha256.Equals(request.ExpectedInfSha256, StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException("Il file INF è cambiato dalla scansione. Ripeti l'analisi.");
    }

    public static void CopyVerified(PackInstallRequest request, string destination)
    {
        Validate(request);
        foreach (var file in request.Files)
        {
            string source = ContainedPath(request.SourceDirectory, file.RelativePath);
            LocalDirectory(Path.GetDirectoryName(source)!);
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("File sostituito con un collegamento.");
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (input.Length != file.Size || !Convert.ToHexString(SHA256.HashData(input)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new CryptographicException("Il pacchetto è cambiato dopo la conferma. Ripeti la scansione.");
            input.Position = 0;
            string target = ContainedPath(destination, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }
    }
}
