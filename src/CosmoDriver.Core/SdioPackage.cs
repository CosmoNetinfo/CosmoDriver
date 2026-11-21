using System.IO.Compression;
using System.Security.Cryptography;

namespace CosmoDriver.Core;

public static class SdioPackage
{
    public const string Version = "2.0.8.891";
    public const string ArchiveSha256 = "96F48D84D97EBDA6E60FD67C45EAF388AE7D3B3DB8E171D3C5D7E5381065BA89";
    public const string Executable = "SDIO_x64_R891.exe";
    public static readonly Uri DownloadUri = new("https://www.glenn.delahoy.com/downloads/sdio/SDIO_2.0.8.891.zip");

    public static void VerifyArchive(Stream stream)
    {
        if (stream.Length is <= 0 or > 128 * 1024 * 1024) throw new InvalidDataException("Dimensione pacchetto SDIO non valida.");
        stream.Position = 0;
        if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(ArchiveSha256, StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException("Il pacchetto SDIO non corrisponde alla versione verificata. Nessun programma eseguito.");
        stream.Position = 0;
    }

    public static void ExtractBounded(ZipArchive archive, string directory)
    {
        if (archive.Entries.Count > 5000) throw new InvalidDataException("Archivio con troppi file.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total = 0;
        foreach (var entry in archive.Entries)
        {
            string name = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            string path = PackPolicy.ContainedPath(directory, name);
            if (!names.Add(path) || (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000 || (total += entry.Length) > 256L * 1024 * 1024)
                throw new InvalidDataException("Archivio non sicuro: file duplicati, collegamenti o dimensioni eccessive.");
        }
        foreach (var entry in archive.Entries)
        {
            string path = PackPolicy.ContainedPath(directory, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) { Directory.CreateDirectory(path); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var input = entry.Open(); using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            byte[] buffer = new byte[81920]; long written = 0; int count;
            while ((count = input.Read(buffer)) > 0)
            {
                if ((written += count) > entry.Length) throw new InvalidDataException("File ZIP oltre la dimensione dichiarata.");
                output.Write(buffer, 0, count);
            }
            if (written != entry.Length) throw new InvalidDataException("File ZIP incompleto.");
        }
    }
}
