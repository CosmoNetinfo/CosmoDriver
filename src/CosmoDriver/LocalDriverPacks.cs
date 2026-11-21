using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using CosmoDriver.Core;

namespace CosmoDriver;

// Windows performs the final INF/OS/architecture/hardware-ID matching, not a handwritten INF parser.
internal static class LocalDriverPacks
{
    public static List<PackCandidate> Scan(string directory, CancellationToken token = default)
    {
        string root = PackPolicy.LocalDirectory(directory);
        var infs = PackPolicy.EnumerateFiles(root, token).Where(f => Path.GetExtension(f).Equals(".inf", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (infs.Length > 3000) throw new InvalidDataException("Troppi INF per una singola analisi: seleziona una sottocartella del DriverPack.");
        long textBytes = 0;
        var texts = infs.Select(inf =>
        {
            token.ThrowIfCancellationRequested();
            long size = new FileInfo(inf).Length;
            if (size > 8 * 1024 * 1024 || (textBytes += size) > 128 * 1024 * 1024) throw new InvalidDataException("INF troppo grandi: seleziona una sottocartella.");
            using var input = new StreamReader(inf, Encoding.Latin1, detectEncodingFromByteOrderMarks: true);
            return (Path: inf, Text: input.ReadToEnd());
        }).ToArray();
        List<PackCandidate> candidates = [];
        nint set = Native.SetupDiGetClassDevsW(0, null, 0, 6); // PRESENT | ALLCLASSES
        if (set == -1) throw new Win32Exception();
        try
        {
            for (uint i = 0; ; i++)
            {
                token.ThrowIfCancellationRequested();
                var device = DeviceData.Create();
                if (!Native.SetupDiEnumDeviceInfo(set, i, ref device))
                { if (Marshal.GetLastWin32Error() != 259) throw new Win32Exception(); break; }
                string instance = InstanceId(set, ref device);
                string name = Property(set, ref device, 12).FirstOrDefault() ?? Property(set, ref device, 0).FirstOrDefault() ?? instance;
                string[] ids = Property(set, ref device, 1).Concat(Property(set, ref device, 2)).Where(id => id.Length > 2).ToArray();
                // This text search only reduces work. SetupAPI below independently verifies every candidate.
                foreach (var inf in texts.Where(inf => ids.Any(id => inf.Text.Contains(id, StringComparison.OrdinalIgnoreCase))))
                {
                    token.ThrowIfCancellationRequested();
                    candidates.AddRange(Match(set, ref device, instance, name, ids.FirstOrDefault() ?? "", inf.Path));
                    if (candidates.Count > 10000) throw new InvalidDataException("Troppi candidati. Seleziona una cartella più specifica.");
                }
            }
        }
        finally { Native.SetupDiDestroyDeviceInfoList(set); }
        return candidates.DistinctBy(c => (c.DeviceId, c.InfPath, c.Version)).OrderBy(c => c.DeviceName).ThenBy(c => c.Rank).ToList();
    }

    private static List<PackCandidate> Match(nint set, ref DeviceData device, string instance, string name, string hardwareId, string inf)
    {
        using var infLock = new FileStream(inf, FileMode.Open, FileAccess.Read, FileShare.Read);
        string infHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(infLock));
        if (inf.Length >= 260) throw new InvalidDataException("Percorso INF troppo lungo: copia il pacchetto in una cartella più breve.");
        var settings = InstallParameters.Create();
        if (!Native.SetupDiGetDeviceInstallParamsW(set, ref device, ref settings)) throw new Win32Exception();
        var original = settings;
        settings.Flags |= 0x10000; settings.FlagsEx |= 0x800; settings.DriverPath = inf;
        if (!Native.SetupDiSetDeviceInstallParamsW(set, ref device, ref settings)) throw new Win32Exception();
        List<PackCandidate> matches = [];
        try
        {
            if (!Native.SetupDiBuildDriverInfoList(set, ref device, 2)) throw new Win32Exception();
            var signer = SignerInfo.Create();
            bool signed = Native.SetupVerifyInfFileW(inf, 0, ref signer);
            if (!signed && unchecked((uint)Marshal.GetLastWin32Error()) == 0xE0000241) signed = true; // trusted Authenticode publisher
            for (uint index = 0; ; index++)
            {
                var driver = DriverData.Create();
                if (!Native.SetupDiEnumDriverInfoW(set, ref device, 2, index, ref driver))
                { if (Marshal.GetLastWin32Error() != 259) throw new Win32Exception(); break; }
                var parameters = DriverParameters.Create();
                if (!Native.SetupDiGetDriverInstallParamsW(set, ref device, ref driver, ref parameters)) throw new Win32Exception();
                if ((parameters.Flags & 0x800) != 0) continue; // DNF_BAD_DRIVER
                string version = $"{driver.Version >> 48}.{(driver.Version >> 32) & 0xffff}.{(driver.Version >> 16) & 0xffff}.{driver.Version & 0xffff}";
                string date = DateTime.FromFileTimeUtc(driver.Date).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                matches.Add(new(instance, name, hardwareId, driver.Description, driver.Provider, version, date, inf, parameters.Rank, signed, signed ? signer.DigitalSigner : "Non verificato", infHash));
            }
        }
        finally
        {
            Native.SetupDiDestroyDriverInfoList(set, ref device, 2);
            Native.SetupDiSetDeviceInstallParamsW(set, ref device, ref original);
        }
        return matches;
    }

    public static InstallResult Install(PackInstallRequest request, bool restore)
    {
        PackPolicy.Validate(request);
        string stage = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CosmoDriver-Install-" + Guid.NewGuid().ToString("N"));
        var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
        var admins = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        security.SetOwner(admins);
        foreach (var sid in new[] { admins, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            security.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(stage).Create(security);
        try
        {
            PackPolicy.CopyVerified(request, stage);
            string inf = PackPolicy.ContainedPath(stage, Path.GetRelativePath(request.SourceDirectory, request.InfPath));
            nint set = Native.SetupDiGetClassDevsW(0, null, 0, 6);
            if (set == -1) throw new Win32Exception();
            List<PackCandidate> matches;
            try
            {
                var device = DeviceData.Create();
                if (!Native.SetupDiOpenDeviceInfoW(set, request.DeviceId, 0, 0, ref device)) throw new Win32Exception();
                matches = Match(set, ref device, request.DeviceId, "", "", inf);
            }
            finally { Native.SetupDiDestroyDeviceInfoList(set); }
            if (!matches.Any(m => m.Signed)) return new(false, false, "Pacchetto non firmato, non attendibile o non più compatibile. Nessuna installazione avviata.");
            return RestorePolicy.BeforeInstall(restore, RestorePoints.Create, () =>
            {
                if (!Native.DiInstallDriverW(0, inf, 0, out bool reboot)) throw new Win32Exception();
                return new(true, reboot, "Pacchetto verificato e aggiunto da Windows. Il driver viene applicato ai dispositivi compatibili soltanto se Windows lo considera migliore: nessuna sostituzione forzata.");
            });
        }
        finally { try { Directory.Delete(stage, recursive: true); } catch { /* protected staging may be locked temporarily by Windows */ } }
    }

    private static string InstanceId(nint set, ref DeviceData device)
    {
        var value = new StringBuilder(4096);
        if (!Native.SetupDiGetDeviceInstanceIdW(set, ref device, value, value.Capacity, out _)) throw new Win32Exception();
        return value.ToString();
    }
    private static string[] Property(nint set, ref DeviceData device, uint property)
    {
        var buffer = new byte[32768];
        if (!Native.SetupDiGetDeviceRegistryPropertyW(set, ref device, property, out _, buffer, (uint)buffer.Length, out uint required))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == 13) return []; // property absent
            throw new Win32Exception(error);
        }
        return Encoding.Unicode.GetString(buffer, 0, (int)required).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceData { public uint Size; public Guid ClassGuid; public uint DevInst; public nuint Reserved; public static DeviceData Create() => new() { Size = (uint)Marshal.SizeOf<DeviceData>() }; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct InstallParameters
    {
        public uint Size, Flags, FlagsEx; public nint Parent, Handler, Context, Queue; public nuint ClassReserved; public uint Reserved;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DriverPath;
        public static InstallParameters Create() => new() { Size = (uint)Marshal.SizeOf<InstallParameters>(), DriverPath = "" };
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DriverData
    {
        public uint Size, Type; public nuint Reserved;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Description;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Manufacturer;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Provider;
        public long Date; public ulong Version;
        public static DriverData Create() => new() { Size = (uint)Marshal.SizeOf<DriverData>(), Description = "", Manufacturer = "", Provider = "" };
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct DriverParameters { public uint Size, Rank, Flags; public nuint PrivateData; public uint Reserved; public static DriverParameters Create() => new() { Size = (uint)Marshal.SizeOf<DriverParameters>() }; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SignerInfo
    {
        public uint Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Catalog;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string DigitalSigner;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string SignerVersion;
        public uint Score;
        public static SignerInfo Create() => new() { Size = (uint)Marshal.SizeOf<SignerInfo>(), Catalog = "", DigitalSigner = "", SignerVersion = "" };
    }
    private static class Native
    {
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern nint SetupDiGetClassDevsW(nint guid, string? enumerator, nint parent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetupDiEnumDeviceInfo(nint set, uint index, ref DeviceData device);
        [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetupDiDestroyDeviceInfoList(nint set);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetupDiOpenDeviceInfoW(nint set, string id, nint parent, uint flags, ref DeviceData device);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetupDiGetDeviceInstanceIdW(nint set, ref DeviceData device, StringBuilder id, int size, out int required);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetupDiGetDeviceRegistryPropertyW(nint set, ref DeviceData device, uint property, out uint type, byte[] value, uint size, out uint required);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetupDiGetDeviceInstallParamsW(nint set, ref DeviceData device, ref InstallParameters settings);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetupDiSetDeviceInstallParamsW(nint set, ref DeviceData device, ref InstallParameters settings);
        [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetupDiBuildDriverInfoList(nint set, ref DeviceData device, uint type);
        [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetupDiDestroyDriverInfoList(nint set, ref DeviceData device, uint type);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetupDiEnumDriverInfoW(nint set, ref DeviceData device, uint type, uint index, ref DriverData driver);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetupDiGetDriverInstallParamsW(nint set, ref DeviceData device, ref DriverData driver, ref DriverParameters settings);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool SetupVerifyInfFileW(string inf, nint alternate, ref SignerInfo signer);
        [DllImport("newdev.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool DiInstallDriverW(nint parent, string inf, uint flags, [MarshalAs(UnmanagedType.Bool)] out bool reboot);
    }
}
