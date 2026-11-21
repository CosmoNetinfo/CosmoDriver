using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CosmoDriver.Core;
using Microsoft.Win32;

namespace CosmoDriver;

public partial class MainWindow : Window
{
    private List<DriverInfo> _drivers = [];
    private List<DriverUpdate> _updates = [];
    private DateTimeOffset? _inventoryAt, _updatesAt;
    private string _updateStatus = "Non verificato";
    private CancellationTokenSource? _scan;
    private readonly CancellationTokenSource _lifetime = new();
    private string? _pendingUpdate;
    private bool _restartAfterUpdate;
    private bool _installing;
    private bool _closeAfterScan;

    public MainWindow(bool automaticStartup = true)
    {
        InitializeComponent();
        BrandSymbol.Source = Brand.Symbol();
        DriversGrid.SelectionChanged += DeviceSelection_Changed;
        UpdatesGrid.SelectionChanged += DeviceSelection_Changed;
        if (automaticStartup)
        {
            Loaded += async (_, _) => await ScanAsync(false);
            Loaded += async (_, _) => await WatchUpdatesAsync();
        }
        Closing += OnClosing;
    }

    internal async Task ScanAsync(bool online)
    {
        if (_scan is not null || _installing) return;
        using var cancellation = new CancellationTokenSource();
        _scan = cancellation;
        SetBusy(true);
        try
        {
            _inventoryAt = null; _drivers = []; DriverCount.Text = "—";
            if (online) { _updatesAt = null; _updates = []; UpdateCount.Text = "—"; _updateStatus = "Non completato"; }
            ApplyFilter();
            StatusText.Text = "Lettura dei driver installati…";
            StatusDetail.Text = "Raccolta locale da Windows. Nessuna modifica ai dispositivi.";
            var inventory = await ProbeClient.RunAsync("inventory", cancellation.Token);
            _drivers = inventory.Drivers; _inventoryAt = DateTimeOffset.Now;
            DriverCount.Text = _drivers.Count.ToString(); ApplyFilter();
            if (online)
            {
                StatusText.Text = "Ricerca delle proposte di Windows Update…";
                StatusDetail.Text = "Il controllo può richiedere alcuni minuti. Puoi annullarlo in qualsiasi momento.";
                var updates = await ProbeClient.RunAsync("updates", cancellation.Token);
                _updates = updates.Updates; _updatesAt = DateTimeOffset.Now;
                _updateStatus = "Completato"; UpdateCount.Text = _updates.Count.ToString();
                StatusText.Text = _updates.Count == 0 ? "Windows Update non propone aggiornamenti driver" : $"Windows Update propone {_updates.Count} aggiornamenti driver";
                StatusDetail.Text = $"Controllo completato alle {_updatesAt:HH:mm}. Seleziona una proposta per leggere i termini e installare il driver.";
                if (_updates.Count > 0) Tabs.SelectedIndex = 1;
            }
            else
            {
                StatusText.Text = "Inventario locale pronto";
                StatusDetail.Text = $"Lettura completata alle {_inventoryAt:HH:mm}. Premi “Controlla aggiornamenti” per la verifica online.";
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Controllo annullato";
            StatusDetail.Text = "La verifica interrotta non consente di stabilire se ci sono aggiornamenti.";
            if (online) _updateStatus = "Annullato";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Controllo non completato";
            StatusDetail.Text = ex is InvalidOperationException or TimeoutException ? ex.Message : "Impossibile leggere i dati di Windows. Riprova o verifica i servizi di sistema.";
            if (online) _updateStatus = "Non riuscito";
        }
        finally { _scan = null; SetBusy(false); ApplyFilter(); if (_closeAfterScan) Close(); }
    }

    private void SetBusy(bool busy)
    {
        ScanButton.IsEnabled = RefreshButton.IsEnabled = !busy;
        ExportButton.IsEnabled = !busy && (_inventoryAt.HasValue || _updatesAt.HasValue);
        CancelButton.Visibility = BusyBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ChoosePackButton.IsEnabled = SdioButton.IsEnabled = ImportSdioButton.IsEnabled = CreateRestoreButton.IsEnabled = !busy;
        AnalyzePackButton.IsEnabled = !busy && _library is not null;
    }

    private void ApplyFilter()
    {
        if (DriversGrid is null || SignatureFilter is null || UpdatesGrid is null || ListHint is null || Tabs is null) return;
        string query = SearchBox.Text.Trim();
        var filtered = _drivers.Where(d => d.Matches(query) && (SignatureFilter.SelectedIndex switch
        { 1 => d.Signed == true, 2 => d.Signed == false, 3 => d.Signed is null, _ => true })).ToList();
        var updates = _updates.Where(u => u.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            u.Manufacturer.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        DriversGrid.ItemsSource = filtered; UpdatesGrid.ItemsSource = updates;
        FilterToolbar.Visibility = InventoryFooter.Visibility = Tabs.SelectedIndex < 2 ? Visibility.Visible : Visibility.Collapsed;
        if (Tabs.SelectedIndex == 0)
            ListHint.Text = _inventoryAt.HasValue ? $"{filtered.Count} di {_drivers.Count} driver · La firma non indica se il driver è aggiornato. La data può essere intenzionalmente vecchia." : "Inventario non disponibile. Premi Rileggi driver.";
        else
            ListHint.Text = _updatesAt.HasValue ? $"{updates.Count} di {_updates.Count} proposte · Verifica del {_updatesAt:dd/MM/yyyy HH:mm}. La ricerca e il filtro firma non associano le proposte ai dispositivi." : "Aggiornamenti non verificati. Avvia e completa il controllo online.";
        SignatureFilter.IsEnabled = Tabs.SelectedIndex == 0;
        UpdateSelection();
    }

    private async void Scan_Click(object sender, RoutedEventArgs e) => await ScanAsync(true);
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await ScanAsync(false);
    private void Cancel_Click(object sender, RoutedEventArgs e) => _scan?.Cancel();
    private void Filter_Changed(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void Signature_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilter();
    private void Tabs_Changed(object sender, SelectionChangedEventArgs e) { if (ReferenceEquals(e.Source, Tabs)) ApplyFilter(); }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_installing)
        {
            e.Cancel = true;
            MessageBox.Show(this, "L'operazione di Windows è in corso. Attendi il risultato prima di chiudere CosmoDriver.", "Operazione in corso", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_scan is not null) { e.Cancel = true; _closeAfterScan = true; _scan.Cancel(); return; }
        _lifetime.Cancel();
        if (_pendingUpdate is not null)
        {
            try { AppUpdater.Schedule(_pendingUpdate, _restartAfterUpdate); }
            catch { MessageBox.Show(this, "Aggiornamento non applicato. La versione attuale resta disponibile; il download verrà riprovato al prossimo avvio.", "CosmoDriver", MessageBoxButton.OK, MessageBoxImage.Information); }
        }
    }

    private async Task WatchUpdatesAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            try
            {
                AppUpdateStatus.Text = "Ricerca nuove versioni di CosmoDriver…";
                _pendingUpdate = await AppUpdater.DownloadAsync(_lifetime.Token);
                AppUpdateStatus.Text = _pendingUpdate is null ? "CosmoDriver: nessuna nuova release disponibile." : "Nuova versione verificata. Si installerà alla chiusura.";
                RestartUpdateButton.Visibility = _pendingUpdate is null ? Visibility.Collapsed : Visibility.Visible;
                if (_pendingUpdate is not null) return;
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return; }
            catch { AppUpdateStatus.Text = "Aggiornamenti app non verificati. Nuovo tentativo al prossimo controllo."; }
            try { await Task.Delay(TimeSpan.FromHours(6), _lifetime.Token); }
            catch (OperationCanceledException) { return; }
        }
    }

    private void RestartUpdate_Click(object sender, RoutedEventArgs e) { _restartAfterUpdate = true; Close(); }

    private void DeviceSelection_Changed(object sender, SelectionChangedEventArgs e) => UpdateSelection();
    private void UpdateSelection()
    {
        if (InstallButton is null || SupportButton is null || InstallPackButton is null) return;
        InstallButton.Visibility = Tabs.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        SupportButton.Visibility = Tabs.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        InstallButton.IsEnabled = !_installing && _scan is null && Tabs.SelectedIndex == 1 && _updatesAt.HasValue && UpdatesGrid.SelectedItem is DriverUpdate;
        InstallPackButton.IsEnabled = !_installing && _scan is null && PacksGrid.SelectedItem is PackCandidate { Signed: true };
        SupportButton.IsEnabled = Tabs.SelectedIndex == 0 && DriversGrid.SelectedItem is DriverInfo selected && InstallPolicy.VendorSupport(selected.Provider) is not null;
    }

    private void Support_Click(object sender, RoutedEventArgs e)
    {
        if (DriversGrid.SelectedItem is not DriverInfo selected || InstallPolicy.VendorSupport(selected.Provider) is not Uri uri) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch { MessageBox.Show(this, "Impossibile aprire il browser. Il sito ufficiale è: " + uri.AbsoluteUri, "Supporto produttore"); }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_scan is not null || _installing || UpdatesGrid.SelectedItem is not DriverUpdate selected || !DriverInstaller.Confirm(this, selected, out bool restore)) return;
        _installing = true; SetBusy(true); UpdateSelection();
        CancelButton.Visibility = Visibility.Collapsed;
        StatusText.Text = "Installazione del driver tramite Windows Update…";
        StatusDetail.Text = "Conferma l'autorizzazione di Windows. Dopo l'avvio non interrompere l'installazione.";
        try
        {
            var result = await DriverInstaller.InstallAsync(selected, restore);
            StatusText.Text = result.Success ? "Driver installato" : "Installazione non completata";
            StatusDetail.Text = result.Message + (result.RebootRequired ? " È necessario riavviare il PC manualmente." : "");
            // Invalidate the old offer list even on failure: WUA can have changed device state.
            _updatesAt = null; _updates = []; UpdateCount.Text = "—"; _updateStatus = "Da riverificare dopo installazione";
            _inventoryAt = null; _drivers = []; DriverCount.Text = "—";
            MessageBox.Show(this, StatusDetail.Text + "\n\nRipeti il controllo per leggere lo stato aggiornato.", StatusText.Text, MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        { StatusText.Text = "Autorizzazione annullata"; StatusDetail.Text = "Windows non ha avviato l'installazione del driver."; }
        catch
        { StatusText.Text = "Installazione non verificata"; StatusDetail.Text = "Impossibile ottenere il risultato. Controlla Windows Update prima di riprovare."; _updatesAt = null; _updateStatus = "Esito installazione sconosciuto"; }
        finally { _installing = false; SetBusy(false); ApplyFilter(); }
    }

    private void WindowsUpdate_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:windowsupdate-optionalupdates") { UseShellExecute = true }); }
        catch { MessageBox.Show(this, "Apri Impostazioni → Windows Update → Opzioni avanzate → Aggiornamenti facoltativi.", "Windows Update", MessageBoxButton.OK, MessageBoxImage.Information); }
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Title = "Esporta il rapporto CosmoDriver", Filter = "Rapporto JSON (*.json)|*.json", FileName = $"CosmoDriver-{DateTime.Now:yyyyMMdd-HHmm}.json", AddExtension = true, DefaultExt = ".json" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var report = new ScanReport(DateTimeOffset.Now, _inventoryAt, _updatesAt, _updateStatus, _drivers.ToArray(), _updates.ToArray(), _packCandidates.ToArray(), PackStatus.Text);
            await File.WriteAllTextAsync(dialog.FileName, JsonSerializer.Serialize(report, Protocol.JsonOptions));
            MessageBox.Show(this, "Rapporto salvato. Include l'intero inventario, anche con filtri attivi. Può contenere nomi dei dispositivi, ID hardware e percorsi locali: condividilo solo se lo desideri.", "Esportazione completata", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch { MessageBox.Show(this, "Impossibile salvare il rapporto. Verifica il percorso e i permessi della cartella.", "Esportazione non riuscita", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void About_Click(object sender, RoutedEventArgs e) => MessageBox.Show(this,
        "CosmoDriver 0.2.0 · by CosmoNet\n\nInventario locale tramite WMI. Verifica e installazione tramite Windows Update/WSUS configurato nel sistema, rispettando i criteri aziendali. L'installazione richiede selezione, conferma della licenza e autorizzazione amministrativa di Windows. Nessun riavvio automatico.\n\nNessuna telemetria, pubblicità o account. La modalità DriverPack verifica gli INF locali; il catalogo esteso usa SDIO in una finestra separata. Puoi creare un punto di ripristino: se richiesto e non verificato, nessuna installazione parte. Il controllo non modifica i driver. L'aggiornamento automatico di CosmoDriver contatta GitHub e verifica firma e hash dei pacchetti prima di applicarli alla chiusura.\n\n" + ScanReport.Scope + "\n\nI collegamenti ai produttori riconosciuti aprono siti ufficiali nel browser; non attestano la disponibilità di una nuova versione. Un driver non firmato o datato non è automaticamente pericoloso o obsoleto. Il rapporto viene salvato solo su tua richiesta.",
        "Informazioni e privacy", MessageBoxButton.OK, MessageBoxImage.Information);
}
