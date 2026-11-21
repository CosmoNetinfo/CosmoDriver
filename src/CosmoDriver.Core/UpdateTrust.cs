using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace CosmoDriver.Core;

public sealed record ReleaseManifest(string Version, string FileName, string Sha256, long Size);

public static class UpdateTrust
{
    public const string Repository = "CosmoNetinfo/CosmoDriver";
    public static Version CurrentVersion => new(0, 1, 0);

    public static ReleaseManifest VerifyManifest(byte[] data, byte[] signature)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CosmoDriver.Core.update-public.pem")
            ?? throw new CryptographicException("Chiave pubblica mancante.");
        using var reader = new StreamReader(stream);
        return VerifyManifest(data, signature, reader.ReadToEnd());
    }

    public static ReleaseManifest VerifyManifest(byte[] data, byte[] signature, string publicKey)
    {
        if (data.Length > 16384 || signature.Length > 1024) throw new CryptographicException("Firma o manifest troppo grande.");
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKey);
        if (!rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new CryptographicException("Firma aggiornamento non valida.");
        var manifest = JsonSerializer.Deserialize<ReleaseManifest>(data)
            ?? throw new InvalidDataException("Manifest non valido.");
        if (!Version.TryParse(manifest.Version, out var version) || version.Build < 0 || version.Revision >= 0 ||
            manifest.Version != version.ToString(3) || manifest.FileName != "CosmoDriver.exe" ||
            manifest.Sha256?.Length != 64 || !manifest.Sha256.All(Uri.IsHexDigit) ||
            manifest.Size is < 1024 or > 512L * 1024 * 1024)
            throw new InvalidDataException("Metadati aggiornamento non validi.");
        return manifest;
    }

    public static Uri AssetUri(string version, string asset)
    {
        if (!Version.TryParse(version, out var parsed) || parsed.Build < 0 || parsed.Revision >= 0 || version != parsed.ToString(3) ||
            asset is not ("CosmoDriver.exe" or "update-manifest.json" or "update-manifest.sig"))
            throw new InvalidDataException("Indirizzo release non valido.");
        return new Uri($"https://github.com/{Repository}/releases/download/v{version}/{asset}");
    }

    public static void VerifyPayload(Stream stream, ReleaseManifest manifest)
    {
        if (stream.Length != manifest.Size) throw new CryptographicException("Dimensione aggiornamento non valida.");
        stream.Position = 0;
        string actual = Convert.ToHexString(SHA256.HashData(stream));
        if (!actual.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException("Hash aggiornamento non valido.");
        stream.Position = 0;
    }
}
