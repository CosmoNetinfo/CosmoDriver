using System.Security.Cryptography;
using System.Text;

namespace CosmoDriver.Core;

public static class InstallPolicy
{
    public static string EulaHash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static void Validate(InstallRequest request)
    {
        if (!request.UserConfirmed || !Guid.TryParseExact(request.UpdateId, "D", out _) || request.Revision < 0 ||
            request.EulaSha256?.Length != 64 || !request.EulaSha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("Richiesta di installazione non valida o non confermata.");
    }

    public static Uri? VendorSupport(string provider)
    {
        // Exact, curated names only. Never build URLs from WMI-provided text.
        string? url = provider.Trim().ToUpperInvariant() switch
        {
            "INTEL" or "INTEL CORPORATION" or "INTEL(R) CORPORATION" => "https://www.intel.com/content/www/us/en/download-center/home.html",
            "NVIDIA" or "NVIDIA CORPORATION" => "https://www.nvidia.com/en-us/drivers/",
            "ADVANCED MICRO DEVICES, INC." or "ADVANCED MICRO DEVICES, INC" or "AMD" or "ATI TECHNOLOGIES INC." => "https://www.amd.com/en/support/download/drivers.html",
            "DELL" or "DELL INC." => "https://www.dell.com/support/home/en-us?app=drivers",
            "HP" or "HEWLETT-PACKARD" or "HP INC." => "https://support.hp.com/drivers",
            "LENOVO" => "https://pcsupport.lenovo.com/",
            "ACER" or "ACER INC." => "https://www.acer.com/support/drivers-and-manuals",
            "ASUSTEK COMPUTER INC." or "ASUS" => "https://www.asus.com/support/download-center/",
            _ => null
        };
        return url is null ? null : new Uri(url);
    }
}
