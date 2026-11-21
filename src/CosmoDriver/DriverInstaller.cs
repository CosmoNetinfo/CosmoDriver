using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CosmoDriver.Core;

namespace CosmoDriver;

internal static class DriverInstaller
{
    public static bool Confirm(Window owner, DriverUpdate update, out bool restore) => ConfirmDetails(owner, update.Title,
        "Windows Update scaricherà e installerà il driver selezionato. Potrebbero verificarsi brevi interruzioni dei dispositivi. Salva il lavoro; il riavvio non sarà automatico.", update.Eula, out restore);

    public static bool ConfirmPackage(Window owner, PackCandidate candidate, out bool restore) => ConfirmDetails(owner,
        candidate.Description + " · " + candidate.Version,
        $"Dispositivo: {candidate.DeviceName}\nFirmatario: {candidate.Signer}\n\nWindows aggiungerà il pacchetto e lo applicherà ai dispositivi compatibili solo se migliore. Nessun downgrade forzato o riavvio automatico.",
        "Il driver e i file associati provengono dalla cartella che hai selezionato. Assicurati di averli ottenuti da una fonte attendibile e di accettarne la licenza del produttore.", out restore);

    private static bool ConfirmDetails(Window owner, string title, string details, string terms, out bool restore)
    {
        var dialog = new Window { Title = "Conferma aggiornamento driver", Owner = owner, Width = 620, Height = 550, MinWidth = 500, MinHeight = 450,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = System.Windows.Media.Brushes.White, FontFamily = owner.FontFamily };
        dialog.Resources = owner.Resources;
        var grid = new Grid { Margin = new Thickness(24) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new TextBlock { Text = "Installa questo aggiornamento?\n\n" + title + "\n\n" + details, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,0,0,16) };
        grid.Children.Add(heading);
        var eula = new TextBox { Text = string.IsNullOrWhiteSpace(terms) ? "Windows Update non ha fornito termini di licenza aggiuntivi per questa proposta." : terms,
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0,0,0,16), Padding = new Thickness(10) };
        Grid.SetRow(eula, 1); grid.Children.Add(eula);
        var consent = new CheckBox { Content = "Ho salvato il lavoro e accetto i termini qui riportati.", Margin = new Thickness(0,0,0,16) };
        var protection = new CheckBox { Content = "Crea prima un punto di ripristino (se fallisce, non installare).", IsChecked = true, Margin = new Thickness(0,0,0,12) };
        var consentPanel = new StackPanel(); consentPanel.Children.Add(protection); consentPanel.Children.Add(consent);
        Grid.SetRow(consentPanel, 2); grid.Children.Add(consentPanel);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Annulla", IsCancel = true, Padding = new Thickness(16,10,16,10), Margin = new Thickness(0,0,10,0) };
        var install = new Button { Content = "Autorizza e installa", IsEnabled = false, Padding = new Thickness(16,10,16,10) };
        consent.Checked += (_, _) => install.IsEnabled = true; consent.Unchecked += (_, _) => install.IsEnabled = false;
        install.Click += (_, _) => dialog.DialogResult = true;
        actions.Children.Add(cancel); actions.Children.Add(install); Grid.SetRow(actions, 3); grid.Children.Add(actions);
        dialog.Content = grid;
        bool confirmed = dialog.ShowDialog() == true;
        restore = protection.IsChecked == true;
        return confirmed;
    }

    public static Task<InstallResult> InstallAsync(DriverUpdate update, bool restore = true)
    {
        var request = new InstallRequest(update.UpdateId, update.Revision, InstallPolicy.EulaHash(update.Eula), true);
        InstallPolicy.Validate(request);
        return RunAdminAsync(new("wua", request, CreateRestorePoint: restore));
    }

    public static async Task<InstallResult> RunAdminAsync(AdminRequest request)
    {
        string requestJson = JsonSerializer.Serialize(request);
        if (requestJson.Length > 8 * 1024 * 1024) throw new InvalidDataException("Pacchetto con troppi metadati: seleziona una cartella più specifica.");
        string pipeName = "CosmoDriver.Install." + Guid.NewGuid().ToString("N");
        using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
        start.ArgumentList.Add("--install-worker"); start.ArgumentList.Add(pipeName);
        using var worker = Process.Start(start) ?? throw new InvalidOperationException("Impossibile avviare l'installazione.");
        using var connectTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var connection = pipe.WaitForConnectionAsync(connectTimeout.Token);
        var exit = worker.WaitForExitAsync(connectTimeout.Token);
        if (await Task.WhenAny(connection, exit) == exit && !pipe.IsConnected)
            throw new InvalidOperationException("Il processo autorizzato non ha avviato la connessione. Nessun driver è stato installato.");
        await connection;
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        await writer.WriteLineAsync(requestJson);
        // Once installation starts, never terminate the WUA installer on cancellation or UI close.
        string? line = await reader.ReadLineAsync();
        return line is null || line.Length > 65536 ? new(false, false, "Installazione interrotta: controlla lo stato in Windows Update.") :
            JsonSerializer.Deserialize<InstallResult>(line) ?? new(false, false, "Risposta di installazione non valida.");
    }

    public static int RunWorker(string pipeName)
    {
        const string prefix = "CosmoDriver.Install.";
        if (!pipeName.StartsWith(prefix, StringComparison.Ordinal) || !Guid.TryParseExact(pipeName[prefix.Length..], "N", out _)) return 2;
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return 3;
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
        try
        {
            pipe.Connect(45000);
            using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            string? line = ReadBoundedLine(reader, 8 * 1024 * 1024);
            if (line is null) return 4;
            InstallResult result;
            try
            {
                var request = JsonSerializer.Deserialize<AdminRequest>(line) ?? throw new InvalidDataException();
                result = Execute(request);
            }
            catch (Exception ex) { result = new(false, false, Protocol.FriendlyError($"0x{ex.HResult:X8}")); }
            writer.WriteLine(JsonSerializer.Serialize(result));
            return result.Success ? 0 : 1;
        }
        catch { return 5; }
    }

    private static string? ReadBoundedLine(StreamReader reader, int limit)
    {
        var text = new StringBuilder(); int next;
        while ((next = reader.Read()) != -1)
        {
            if (next == '\n') return text.ToString();
            if (text.Length >= limit) throw new InvalidDataException("Richiesta troppo grande.");
            text.Append((char)next);
        }
        return text.Length == 0 ? null : text.ToString();
    }

    private static InstallResult Execute(AdminRequest request)
    {
        if (request.Operation == "restore")
        {
            var point = RestorePoints.Create();
            return new(point.Success, false, point.Message);
        }
        if (request.Operation == "pack" && request.Package is not null)
            return LocalDriverPacks.Install(request.Package, request.CreateRestorePoint);
        if (request.Operation != "wua" || request.WindowsUpdate is null) throw new InvalidDataException("Operazione non valida.");
        InstallPolicy.Validate(request.WindowsUpdate);
        return InstallNative(request.WindowsUpdate, request.CreateRestorePoint);
    }

    private static InstallResult InstallNative(InstallRequest request, bool restore)
    {
        dynamic session = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session", true)!)!;
        session.ClientApplicationID = "CosmoNet.CosmoDriver";
        dynamic searcher = session.CreateUpdateSearcher();
        dynamic found = searcher.Search($"IsInstalled=0 and IsHidden=0 and Type='Driver' and UpdateID='{request.UpdateId}' and RevisionNumber={request.Revision}");
        if ((int)found.ResultCode != 2 || (int)found.Updates.Count != 1)
            return new(false, false, "La proposta non è più disponibile. Ripeti il controllo prima di installare.");
        dynamic update = found.Updates.Item(0);
        string eula = (string?)update.EulaText ?? "";
        if (!InstallPolicy.EulaHash(eula).Equals(request.EulaSha256, StringComparison.OrdinalIgnoreCase))
            return new(false, false, "I termini di licenza sono cambiati. Ripeti la scansione e conferma nuovamente.");
        if (restore)
        {
            var point = RestorePoints.Create();
            if (!point.Success) return new(false, false, point.Message);
        }
        if (!(bool)update.EulaAccepted) update.AcceptEula();
        dynamic collection = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.UpdateColl", true)!)!;
        collection.Add(update);
        dynamic downloader = session.CreateUpdateDownloader(); downloader.Updates = collection;
        dynamic download = downloader.Download();
        if ((int)download.ResultCode != 2 || !(bool)update.IsDownloaded)
            return new(false, false, "Download non completato. Nessun driver è stato installato da questa operazione.");
        dynamic installer = session.CreateUpdateInstaller(); installer.Updates = collection;
        if ((bool)installer.RebootRequiredBeforeInstallation)
            return new(false, true, "Windows richiede un riavvio prima dell'installazione. Salva il lavoro e riavvia manualmente.");
        installer.AllowSourcePrompts = false;
        dynamic result = installer.Install();
        bool success = (int)result.ResultCode == 2 && (int)result.GetUpdateResult(0).ResultCode == 2;
        bool reboot = (bool)result.RebootRequired;
        return new(success, reboot, success ? "Driver installato da Windows Update." : "Installazione non completata. Verifica i dettagli e la cronologia in Windows Update.");
    }
}
