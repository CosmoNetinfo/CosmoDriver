using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CosmoDriver.Core;
using Microsoft.Win32;

namespace CosmoDriver;

public partial class MainWindow
{
    private string? _library;
    private List<PackCandidate> _packCandidates = [];

    private void ChoosePack_Click(object sender, RoutedEventArgs e) => ChooseLibrary();
    private bool ChooseLibrary()
    {
        if (_scan is not null || _installing) return false;
        var dialog = new OpenFolderDialog { Title = "Scegli la cartella dei pacchetti driver o una libreria su USB", Multiselect = false };
        if (dialog.ShowDialog(this) != true) return false;
        try
        {
            _library = PackPolicy.LocalDirectory(dialog.FolderName); _packCandidates = []; PacksGrid.ItemsSource = _packCandidates;
            PackStatus.Text = _library; AnalyzePackButton.IsEnabled = true;
            return true;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Cartella non disponibile"); return false; }
    }

    private async void AnalyzePack_Click(object sender, RoutedEventArgs e)
    {
        if (_library is not null) await AnalyzePacksAsync(_library);
    }

    internal async Task AnalyzePacksAsync(string directory)
    {
        if (_scan is not null || _installing) return;
        using var cancellation = new CancellationTokenSource(); _scan = cancellation; SetBusy(true);
        _packCandidates = []; PacksGrid.ItemsSource = _packCandidates; UpdateSelection();
        StatusText.Text = "Ricerca nei pacchetti locali…";
        StatusDetail.Text = "Windows verifica corrispondenza hardware, sistema e architettura. Nessun driver viene installato.";
        try
        {
            _library = PackPolicy.LocalDirectory(directory);
            _packCandidates = await Task.Run(() => LocalDriverPacks.Scan(directory, cancellation.Token), cancellation.Token);
            PacksGrid.ItemsSource = _packCandidates;
            PackStatus.Text = $"{_packCandidates.Count} candidati compatibili · {_library}";
            StatusText.Text = "Analisi dei pacchetti completata";
            StatusDetail.Text = "I candidati con firma non verificata sono bloccati. Windows sceglierà se un pacchetto è migliore di quello installato.";
        }
        catch (OperationCanceledException) { StatusText.Text = "Analisi pacchetti annullata"; PackStatus.Text = "Analisi non completata: riprova."; }
        catch (Exception ex) { StatusText.Text = "Analisi pacchetti non completata"; PackStatus.Text = ex.Message; StatusDetail.Text = "Nessuna modifica ai driver."; }
        finally { _scan = null; SetBusy(false); UpdateSelection(); if (_closeAfterScan) Close(); }
    }

    private async void InstallPack_Click(object sender, RoutedEventArgs e)
    {
        if (_scan is not null || _installing || PacksGrid.SelectedItem is not PackCandidate selected || !selected.Signed ||
            !DriverInstaller.ConfirmPackage(this, selected, out bool restore)) return;
        await RunMaintenanceAsync(async () =>
        {
            string source = Path.GetDirectoryName(selected.InfPath)!;
            var files = await Task.Run(() => PackPolicy.Snapshot(source));
            var request = new PackInstallRequest(selected.DeviceId, selected.InfPath, source, files, true, selected.InfSha256);
            PackPolicy.Validate(request);
            return await DriverInstaller.RunAdminAsync(new("pack", Package: request, CreateRestorePoint: restore));
        }, "Verifica del pacchetto e installazione…", invalidatesDrivers: true);
    }

    private async void CreateRestore_Click(object sender, RoutedEventArgs e) =>
        await RunMaintenanceAsync(() => DriverInstaller.RunAdminAsync(new("restore")), "Creazione del punto di ripristino…", invalidatesDrivers: false);

    private async Task<InstallResult?> RunMaintenanceAsync(Func<Task<InstallResult>> action, string title, bool invalidatesDrivers)
    {
        if (_scan is not null || _installing) return null;
        _installing = true; SetBusy(true); UpdateSelection(); CancelButton.Visibility = Visibility.Collapsed;
        StatusText.Text = title; StatusDetail.Text = "Conferma l'autorizzazione di Windows e attendi il risultato.";
        try
        {
            var result = await action();
            StatusText.Text = result.Success ? "Operazione completata" : "Operazione non completata";
            StatusDetail.Text = RestoreStatus.Text = result.Message + (result.RebootRequired ? " È necessario riavviare il PC manualmente." : "");
            MessageBox.Show(this, StatusDetail.Text, StatusText.Text, MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
            return result;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { StatusText.Text = "Autorizzazione annullata"; StatusDetail.Text = "L'operazione non è stata avviata."; return null; }
        catch (Exception ex)
        { StatusText.Text = "Operazione non verificata"; StatusDetail.Text = ex is InvalidDataException or System.Security.Cryptography.CryptographicException ? ex.Message : "Non è stato possibile ottenere il risultato. Verifica lo stato in Windows prima di riprovare."; RestoreStatus.Text = StatusDetail.Text; return null; }
        finally
        {
            if (invalidatesDrivers)
            {
                _inventoryAt = _updatesAt = null; _drivers = []; _updates = []; _packCandidates = [];
                DriverCount.Text = UpdateCount.Text = "—"; _updateStatus = "Da riverificare dopo operazione pacchetto";
                PacksGrid.ItemsSource = _packCandidates; PackStatus.Text = "Ripeti la scansione per leggere lo stato attuale.";
            }
            _installing = false; SetBusy(false); ApplyFilter();
        }
    }

    private void Protection_Click(object sender, RoutedEventArgs e) => OpenWindowsTool("SystemPropertiesProtection.exe");
    private void Recovery_Click(object sender, RoutedEventArgs e) => OpenWindowsTool("rstrui.exe");
    private void OpenWindowsTool(string name)
    {
        try { Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), name)) { UseShellExecute = true })?.Dispose(); }
        catch { MessageBox.Show(this, "Apri le impostazioni di Protezione sistema da Windows.", "Ripristino Windows"); }
    }

    private async void Sdio_Click(object sender, RoutedEventArgs e) => await OpenSdioAsync(null);
    private async void ImportSdio_Click(object sender, RoutedEventArgs e)
    {
        if (_scan is not null || _installing) return;
        var dialog = new OpenFileDialog { Title = "Importa il pacchetto ufficiale SDIO " + SdioPackage.Version, Filter = "Pacchetto SDIO (*.zip)|*.zip", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) await OpenSdioAsync(dialog.FileName);
    }

    private async Task OpenSdioAsync(string? archive)
    {
        if (_scan is not null || _installing || (_library is null && !ChooseLibrary())) return;
        using var cancellation = new CancellationTokenSource(); _scan = cancellation; SetBusy(true); UpdateSelection();
        StatusText.Text = "Preparazione del motore SDIO verificato…";
        StatusDetail.Text = "Il programma originale gestirà indici e DriverPack nella sua finestra. Nessuna installazione automatica.";
        string? executable = null;
        try { executable = await SdioEngine.PrepareAsync(_library!, archive, cancellation.Token); }
        catch (OperationCanceledException) { StatusText.Text = "Preparazione SDIO annullata"; }
        catch (Exception ex)
        {
            StatusText.Text = "SDIO non avviato";
            StatusDetail.Text = ex is System.Security.Cryptography.CryptographicException or InvalidDataException ? ex.Message :
                "Download ufficiale non disponibile. Puoi importare lo ZIP ufficiale SDIO 2.0.8.891; firma di provenienza tramite hash verificato, nessun mirror sconosciuto.";
            PackStatus.Text = StatusDetail.Text;
        }
        finally { _scan = null; SetBusy(false); UpdateSelection(); }
        if (_closeAfterScan) { Close(); return; }
        if (executable is null) return;
        var answer = MessageBox.Show(this, "Vuoi creare un punto di ripristino prima di aprire SDIO?\n\nSì: se Windows non lo crea, SDIO non viene avviato.\nNo: apri senza creare un nuovo punto.\n\nSDIO può scaricare grandi pacchetti e usare BitTorrent; scegli lì gli indici e i driver necessari.", "Catalogo DriverPack · SDIO", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Yes);
        if (answer == MessageBoxResult.Cancel) return;
        if (answer == MessageBoxResult.Yes)
        {
            var result = await RunMaintenanceAsync(() => DriverInstaller.RunAdminAsync(new("restore")), "Creazione del punto di ripristino…", false);
            if (result?.Success != true) return;
        }
        try
        {
            using var process = SdioEngine.Launch(executable, _library!);
            StatusText.Text = "Catalogo DriverPack aperto in SDIO";
            StatusDetail.Text = "Seleziona indici e driver nella finestra originale SDIO. Al termine ripeti il controllo in CosmoDriver.";
        }
        catch { StatusText.Text = "SDIO non avviato"; StatusDetail.Text = "Verifica l'autorizzazione di Windows e la cartella selezionata."; }
    }
}
