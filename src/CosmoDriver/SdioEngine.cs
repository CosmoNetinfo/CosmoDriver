using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using CosmoDriver.Core;

namespace CosmoDriver;

internal static class SdioEngine
{
    public static async Task<string> PrepareAsync(string library, string? importedArchive, CancellationToken token)
    {
        library = PackPolicy.LocalDirectory(library);
        string archive = importedArchive ?? Path.Combine(library, "SDIO-" + SdioPackage.Version + ".zip");
        if (importedArchive is null && !File.Exists(archive))
        {
            string temporary = archive + "." + Guid.NewGuid().ToString("N") + ".download";
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                client.DefaultRequestHeaders.UserAgent.ParseAdd("CosmoDriver/0.2.0");
                using var response = await client.GetAsync(SdioPackage.DownloadUri, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                using var input = await response.Content.ReadAsStreamAsync(token);
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    byte[] buffer = new byte[81920]; long size = 0; int count;
                    while ((count = await input.ReadAsync(buffer, token)) > 0)
                    {
                        if ((size += count) > 128L * 1024 * 1024) throw new InvalidDataException("Download SDIO troppo grande.");
                        await output.WriteAsync(buffer.AsMemory(0, count), token);
                    }
                    await output.FlushAsync(token); SdioPackage.VerifyArchive(output);
                }
                File.Move(temporary, archive);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        using var stream = new FileStream(archive, FileMode.Open, FileAccess.Read, FileShare.Read);
        SdioPackage.VerifyArchive(stream);
        // Always extract verified bytes to a fresh folder; never execute a previously modified cached engine.
        string runtime = Path.Combine(library, "CosmoDriver-SDIO-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(runtime);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        SdioPackage.ExtractBounded(zip, runtime);
        string executable = Path.Combine(runtime, SdioPackage.Executable);
        if (!File.Exists(executable)) throw new InvalidDataException("Eseguibile SDIO atteso assente dal pacchetto.");
        return executable;
    }

    public static Process Launch(string executable, string library)
    {
        library = PackPolicy.LocalDirectory(library);
        string drivers = Path.Combine(library, "drivers"), indexes = Path.Combine(library, "indexes"), logs = Path.Combine(library, "logs");
        foreach (string directory in new[] { drivers, indexes, logs }) { Directory.CreateDirectory(directory); PackPolicy.LocalDirectory(directory); }
        var start = new ProcessStartInfo(executable) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
        start.ArgumentList.Add("-drp_dir:" + drivers);
        start.ArgumentList.Add("-index_dir:" + indexes);
        start.ArgumentList.Add("-log_dir:" + logs);
        // No autoinstall/scripting flags: the user chooses drivers in the original engine's UI.
        return Process.Start(start) ?? throw new InvalidOperationException("Impossibile avviare SDIO.");
    }
}
