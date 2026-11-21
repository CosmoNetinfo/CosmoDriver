using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using CosmoDriver.Core;

namespace CosmoDriver;

internal sealed record UpdatePlan(string Directory, string Target, int ParentId, long ParentStart, bool Restart);

internal static class AppUpdater
{
    private static readonly HttpClient Client = CreateClient();
    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("CosmoDriver/0.2.0");
        return client;
    }

    public static async Task<string?> DownloadAsync(CancellationToken token)
    {
        using var response = await Client.GetAsync($"https://api.github.com/repos/{UpdateTrust.Repository}/releases/latest", HttpCompletionOption.ResponseHeadersRead, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        byte[] latestBytes = await ReadLimitedAsync(response, 1024 * 1024, token);
        using var latest = JsonDocument.Parse(latestBytes);
        if (latest.RootElement.GetProperty("prerelease").GetBoolean() || latest.RootElement.GetProperty("draft").GetBoolean()) return null;
        string tag = latest.RootElement.GetProperty("tag_name").GetString() ?? "";
        if (!tag.StartsWith('v') || !Version.TryParse(tag[1..], out var offered) || offered <= UpdateTrust.CurrentVersion) return null;
        var manifestData = await GetLimitedAsync(UpdateTrust.AssetUri(tag[1..], "update-manifest.json"), 16384, token);
        var signature = await GetLimitedAsync(UpdateTrust.AssetUri(tag[1..], "update-manifest.sig"), 1024, token);
        var manifest = UpdateTrust.VerifyManifest(manifestData, signature);
        if (manifest.Version != tag[1..]) throw new InvalidDataException("La versione firmata non corrisponde alla release.");

        string directory = Path.Combine(Path.GetTempPath(), "CosmoDriver-updates", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(directory, "update-manifest.json"), manifestData, token);
            await File.WriteAllBytesAsync(Path.Combine(directory, "update-manifest.sig"), signature, token);
            using var payload = await Client.GetAsync(UpdateTrust.AssetUri(manifest.Version, manifest.FileName), HttpCompletionOption.ResponseHeadersRead, token);
            payload.EnsureSuccessStatusCode();
            if (payload.Content.Headers.ContentLength is long size && size != manifest.Size) throw new InvalidDataException("Dimensione release non valida.");
            using var source = await payload.Content.ReadAsStreamAsync(token);
            await using (var destination = new FileStream(Path.Combine(directory, "CosmoDriver.exe"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                byte[] buffer = new byte[81920];
                long written = 0;
                int count;
                while ((count = await source.ReadAsync(buffer, token)) != 0)
                {
                    written += count;
                    if (written > manifest.Size) throw new InvalidDataException("Download oltre la dimensione dichiarata.");
                    await destination.WriteAsync(buffer.AsMemory(0, count), token);
                }
                await destination.FlushAsync(token);
                UpdateTrust.VerifyPayload(destination, manifest);
            }
            return directory;
        }
        catch { System.IO.Directory.Delete(directory, true); throw; }
    }

    private static async Task<byte[]> GetLimitedAsync(Uri uri, int limit, CancellationToken token)
    {
        using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        return await ReadLimitedAsync(response, limit, token);
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpResponseMessage response, int limit, CancellationToken token)
    {
        using var stream = await response.Content.ReadAsStreamAsync(token);
        using var result = new MemoryStream();
        byte[] buffer = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(buffer, token)) != 0)
        {
            if (result.Length + count > limit) throw new InvalidDataException("Risposta aggiornamenti troppo grande.");
            result.Write(buffer, 0, count);
        }
        return result.ToArray();
    }

    public static void Schedule(string directory, bool restart)
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Percorso non disponibile.");
        // A copy of the current trusted executable acts as the updater, including the pinned public key.
        string helper = Path.Combine(directory, "CosmoDriver.Update.exe");
        File.Copy(executable, helper, overwrite: true);
        using var parent = Process.GetCurrentProcess();
        var plan = new UpdatePlan(directory, executable, parent.Id, parent.StartTime.ToUniversalTime().Ticks, restart);
        string planPath = Path.Combine(directory, "plan.json");
        File.WriteAllText(planPath, JsonSerializer.Serialize(plan));
        var start = new ProcessStartInfo(helper) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("--apply-update"); start.ArgumentList.Add(planPath);
        Process.Start(start)?.Dispose();
    }

    public static int Apply(string planPath)
    {
        string? staged = null;
        try
        {
            var plan = JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(planPath)) ?? throw new InvalidDataException();
            string directory = Path.GetFullPath(plan.Directory);
            if (!Path.GetFullPath(planPath).Equals(Path.Combine(directory, "plan.json"), StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(plan.Target).Equals("CosmoDriver.exe", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFullPath(plan.Target).Equals(Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
            var manifest = UpdateTrust.VerifyManifest(File.ReadAllBytes(Path.Combine(directory, "update-manifest.json")),
                File.ReadAllBytes(Path.Combine(directory, "update-manifest.sig")));
            if (Version.Parse(manifest.Version) <= UpdateTrust.CurrentVersion) throw new InvalidDataException();
            using (var payload = new FileStream(Path.Combine(directory, "CosmoDriver.exe"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                UpdateTrust.VerifyPayload(payload, manifest);
                staged = plan.Target + ".update-" + Guid.NewGuid().ToString("N");
                using var destination = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                payload.CopyTo(destination);
                destination.Flush(true);
            }
            try
            {
                using var parent = Process.GetProcessById(plan.ParentId);
                if (parent.StartTime.ToUniversalTime().Ticks == plan.ParentStart && !parent.WaitForExit(30000)) throw new TimeoutException();
            }
            catch (ArgumentException) { /* parent already exited */ }
            // Atomic replacement on the target volume; retains a previous-version recovery copy.
            File.Replace(staged, plan.Target, plan.Target + ".previous", ignoreMetadataErrors: true);
            staged = null;
            if (plan.Restart) Process.Start(new ProcessStartInfo(plan.Target) { UseShellExecute = true })?.Dispose();
            return 0;
        }
        catch { return 1; }
        finally { if (staged is not null && File.Exists(staged)) File.Delete(staged); }
    }
}
