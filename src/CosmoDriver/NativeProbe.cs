using System.Globalization;
using System.Runtime.InteropServices;
using CosmoDriver.Core;

namespace CosmoDriver;

// Runs only in a disposable child process. COM objects never cross into the UI.
internal static class NativeProbe
{
    public static ProbeResult Run(string mode)
    {
        try
        {
            return mode switch
            {
                "inventory" => Inventory(),
                "updates" => Updates(),
                _ => new(false, [], [], "INVALID_MODE")
            };
        }
        catch (Exception ex) { return new(false, [], [], $"0x{ex.HResult:X8}"); }
    }

    private static ProbeResult Inventory()
    {
        dynamic locator = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator", true)!)!;
        dynamic service = locator.ConnectServer(".", "root\\cimv2");
        dynamic items = service.ExecQuery("SELECT DeviceName, DriverProviderName, DriverVersion, DriverDate, DeviceClass, IsSigned, InfName FROM Win32_PnPSignedDriver");
        List<DriverInfo> drivers = [];
        foreach (dynamic item in items)
        {
            string Get(string property)
            {
                string? value = Convert.ToString(item.Properties_.Item(property).Value, CultureInfo.InvariantCulture);
                return string.IsNullOrWhiteSpace(value) ? "Non disponibile" : value;
            }
            object? signature = item.Properties_.Item("IsSigned").Value;
            drivers.Add(new(Get("DeviceName"), Get("DriverProviderName"), Get("DriverVersion"),
                Protocol.DriverDate(Get("DriverDate")), Get("DeviceClass"),
                Protocol.SignatureValue(signature), Get("InfName")));
        }
        return new(true, drivers.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList(), [], null);
    }


    private static ProbeResult Updates()
    {
        dynamic session = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session", true)!)!;
        session.ClientApplicationID = "CosmoNet.CosmoDriver";
        dynamic searcher = session.CreateUpdateSearcher();
        // Respect the system's configured update source and enterprise policy.
        searcher.Online = true;
        dynamic result = searcher.Search("IsInstalled=0 and IsHidden=0 and Type='Driver'");
        if ((int)result.ResultCode != 2) return new(false, [], [], "PARTIAL");
        List<DriverUpdate> updates = [];
        for (int i = 0; i < (int)result.Updates.Count; i++)
        {
            dynamic update = result.Updates.Item(i);
            string date = ((DateTime)update.DriverVerDate).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            updates.Add(new((string)update.Title, (string?)update.DriverManufacturer ?? "Non disponibile",
                (string?)update.DriverHardwareID ?? "Non disponibile", date, (bool)update.BrowseOnly,
                (string)update.Identity.UpdateID, (int)update.Identity.RevisionNumber,
                (string?)update.EulaText ?? "", (bool)update.EulaAccepted));
        }
        return new(true, [], updates, null);
    }
}
