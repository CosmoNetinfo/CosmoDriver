namespace CosmoDriver.Core;

public sealed record RestorePointRecord(long Sequence, string Description);

public static class RestorePolicy
{
    public static RestorePointResult Evaluate(uint status, IReadOnlyList<RestorePointRecord> before, IReadOnlyList<RestorePointRecord> after, string description)
    {
        if (status != 0) return new(false, null, false, $"Windows non ha creato il punto di ripristino (codice {status}). Verifica Protezione sistema e spazio disponibile.");
        var created = after.FirstOrDefault(p => p.Description == description && !before.Any(old => old.Sequence == p.Sequence));
        if (created is not null) return new(true, created.Sequence, true, $"Punto di ripristino creato e verificato: {created.Sequence}. Non è un backup dei file personali.");
        return new(false, null, false, after.Any(p => before.Any(b => b.Sequence == p.Sequence)) ?
            "Windows non ha creato un nuovo punto: può essere attivo il limite di frequenza (normalmente 24 ore). Apri Protezione sistema per verificarlo. Nessuna installazione avviata." :
            "Windows ha risposto senza rendere verificabile il punto di ripristino. Nessuna installazione avviata.");
    }

    public static InstallResult BeforeInstall(bool required, Func<RestorePointResult> create, Func<InstallResult> install)
    {
        if (required)
        {
            var result = create();
            if (!result.Success || !result.Created || result.Sequence is null) return new(false, false, result.Message);
        }
        return install();
    }
}
