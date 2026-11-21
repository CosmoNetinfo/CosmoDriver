using System.Diagnostics;
using System.IO;
using System.Text;
using CosmoDriver.Core;

namespace CosmoDriver;

internal static class ProbeClient
{
    public static async Task<ProbeResult> RunAsync(string mode, CancellationToken cancellationToken)
    {
        if (mode is not ("inventory" or "updates")) throw new ArgumentException("Modalità non valida.", nameof(mode));
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(mode == "updates" ? 5 : 1));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var start = new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("Percorso applicazione sconosciuto."))
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System)
        };
        start.ArgumentList.Add("--probe");
        start.ArgumentList.Add(mode);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Impossibile avviare la scansione.");
        try
        {
            var stdout = ReadBoundedAsync(process.StandardOutput, linked.Token);
            var stderr = ReadBoundedAsync(process.StandardError, linked.Token);
            await Task.WhenAll(stdout, stderr, process.WaitForExitAsync(linked.Token));
            var result = Protocol.Parse(await stdout);
            if (process.ExitCode != 0) throw new InvalidOperationException("Il processo di scansione è terminato in modo anomalo.");
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("Windows non ha risposto in tempo. Riprova o apri Windows Update."); }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken token)
    {
        var content = new StringBuilder();
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) != 0)
        {
            if (content.Length + count > 16 * 1024 * 1024) throw new InvalidDataException("Risposta di scansione troppo grande.");
            content.Append(buffer, 0, count);
        }
        return content.ToString();
    }
}
