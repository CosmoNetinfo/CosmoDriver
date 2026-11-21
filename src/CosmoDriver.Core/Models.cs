using System.Globalization;
using System.Text.Json;

namespace CosmoDriver.Core;

public sealed record DriverInfo(string Name, string Provider, string Version, string Date,
    string DeviceClass, bool? Signed, string InfName)
{
    public string Signature => Signed switch { true => "Firmato", false => "Non firmato", _ => "Non disponibile" };
    public bool Matches(string query) => new[] { Name, Provider, Version, DeviceClass, InfName }
        .Any(s => s.Contains(query, StringComparison.OrdinalIgnoreCase));
}

public sealed record DriverUpdate(string Title, string Manufacturer, string HardwareId, string Date, bool Optional,
    string UpdateId, int Revision, string Eula, bool EulaAccepted);
public sealed record InstallRequest(string UpdateId, int Revision, string EulaSha256, bool UserConfirmed);
public sealed record InstallResult(bool Success, bool RebootRequired, string Message);
public sealed record ProbeResult(bool Success, List<DriverInfo> Drivers, List<DriverUpdate> Updates, string? ErrorCode);
public sealed record ScanReport(DateTimeOffset CreatedAt, DateTimeOffset? InventoryAt, DateTimeOffset? UpdatesAt,
    string UpdateStatus, IReadOnlyList<DriverInfo> Drivers, IReadOnlyList<DriverUpdate> Updates)
{
    public const string Scope = "Fonte: Windows Update/WSUS configurato nel sistema. Nessun risultato non garantisce l'assenza di versioni sul sito del produttore. Le proposte non sono associate automaticamente ai dispositivi dell'inventario.";
    public string Coverage => Scope;
    public string Application => "CosmoDriver by CosmoNet";
}

public static class Protocol
{
    public static bool? SignatureValue(object? value) => value is null or DBNull ? null : Convert.ToBoolean(value, CultureInfo.InvariantCulture);
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = false };
    public static ProbeResult Parse(string json)
    {
        var result = JsonSerializer.Deserialize<ProbeResult>(json, JsonOptions)
            ?? throw new InvalidDataException("Risposta vuota dal servizio di scansione.");
        if (result.Drivers is null || result.Updates is null ||
            result.Drivers.Any(d => d is null || d.Name is null || d.Provider is null || d.Version is null || d.Date is null || d.DeviceClass is null || d.InfName is null) ||
            result.Updates.Any(u => u is null || u.Title is null || u.Manufacturer is null || u.HardwareId is null || u.Date is null ||
                u.Eula is null || !Guid.TryParseExact(u.UpdateId, "D", out _) || u.Revision < 0))
            throw new InvalidDataException("Risposta di scansione incompleta.");
        if (!result.Success) throw new InvalidOperationException(FriendlyError(result.ErrorCode));
        return result;
    }

    public static string FriendlyError(string? code) => code?.ToUpperInvariant() switch
    {
        "0X80070005" => "Accesso negato. Verifica le autorizzazioni e le regole aziendali di Windows Update.",
        "0X80070422" => "Il servizio Windows Update è disabilitato. Riattivalo nelle impostazioni di Windows.",
        "0X8024402C" or "0X80072EE7" or "0X80072EFD" => "Windows Update non è raggiungibile. Verifica connessione e proxy.",
        "PARTIAL" => "Windows Update ha restituito risultati parziali. Riprova: il controllo non è completo.",
        _ => $"Scansione non riuscita ({code ?? "errore sconosciuto"}). Riprova o consulta Windows Update."
    };

    public static string DriverDate(string? value)
    {
        if (value?.Length >= 8 && DateTime.TryParseExact(value[..8], "yyyyMMdd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var date)) return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return "Non disponibile";
    }
}
