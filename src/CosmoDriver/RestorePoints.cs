using System.Globalization;
using CosmoDriver.Core;

namespace CosmoDriver;

internal static class RestorePoints
{
    public static RestorePointResult Create()
    {
        try
        {
            dynamic locator = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator", true)!)!;
            dynamic service = locator.ConnectServer(".", "root\\default");
            service.Security_.ImpersonationLevel = 3;
            dynamic systemRestore = service.Get("SystemRestore");
            List<RestorePointRecord> before = ReadPoints(service);
            string description = $"CosmoDriver {DateTime.Now:yyyy-MM-dd HH:mm:ss} {Guid.NewGuid().ToString("N")[..6]}";
            uint status = Convert.ToUInt32(systemRestore.CreateRestorePoint(description, 12, 100), CultureInfo.InvariantCulture);
            if (status != 0) return RestorePolicy.Evaluate(status, before, [], description);
            uint ended = Convert.ToUInt32(systemRestore.CreateRestorePoint(description, 12, 101), CultureInfo.InvariantCulture);
            if (ended != 0) return new(false, null, false, $"Windows non ha completato il punto di ripristino (codice {ended}). Nessuna installazione avviata.");
            List<RestorePointRecord> after = ReadPoints(service);
            return RestorePolicy.Evaluate(status, before, after, description);
        }
        catch (Exception ex) { return new(false, null, false, $"Punto di ripristino non disponibile (0x{ex.HResult:X8}). Apri Protezione sistema per abilitarlo o verificare le regole del PC."); }
    }

    private static List<RestorePointRecord> ReadPoints(dynamic service)
    {
        List<RestorePointRecord> result = [];
        foreach (dynamic item in service.ExecQuery("SELECT SequenceNumber, Description FROM SystemRestore"))
            result.Add(new(Convert.ToInt64(item.SequenceNumber, CultureInfo.InvariantCulture), (string?)item.Description ?? ""));
        return result;
    }
}
