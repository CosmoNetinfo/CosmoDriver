using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CosmoDriver;

// Explicit diagnostic mode used by CI; never enabled in the normal user flow.
internal static class SmokeHarness
{
    public static async Task<int> RunAsync(MainWindow window, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        List<string> checks = [];
        void Check(string name, bool condition)
        {
            if (!condition) throw new InvalidOperationException(name);
            checks.Add(name);
        }
        try
        {
            await window.ScanAsync(false);
            Check("Real WMI inventory populated", int.TryParse(window.DriverCount.Text, out int count) && count > 0);
            Check("Inventory never marks updates as current", window.UpdateCount.Text == "—");
            Check("Report enabled after inventory", window.ExportButton.IsEnabled);
            window.UpdateLayout();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture(window, Path.Combine(outputDirectory, "cosmodriver-desktop.png"));
            window.SearchBox.Text = "NONEXISTENT-COSMODRIVER-DEVICE-73984";
            Check("Search with no matches", window.DriversGrid.Items.Count == 0 && window.ListHint.Text.StartsWith("0 di"));
            window.SearchBox.Text = "";
            Check("Search reset restores inventory", window.DriversGrid.Items.Count == count);
            window.SignatureFilter.SelectedIndex = 2;
            Check("Unsigned filter does not include signed drivers", window.DriversGrid.Items.Cast<Core.DriverInfo>().All(d => d.Signed == false));
            window.SignatureFilter.SelectedIndex = 0;
            window.Tabs.SelectedIndex = 1;
            Check("Unverified updates have an explicit state", window.ListHint.Text.Contains("non verificati"));
            Check("Signature filter disabled on updates tab", !window.SignatureFilter.IsEnabled);
            Check("Install requires an available selected offer", !window.InstallButton.IsEnabled);
            window.Tabs.SelectedIndex = 0;
            // Selection must not recurse into rebuilding the table.
            window.DriversGrid.SelectedIndex = 0;
            Check("Row selection works", window.DriversGrid.SelectedIndex == 0);
            Check("Unknown vendor support remains disabled", window.DriversGrid.SelectedItem is not Core.DriverInfo selected || Core.InstallPolicy.VendorSupport(selected.Provider) is not null || !window.SupportButton.IsEnabled);
            window.DriversGrid.SelectedIndex = -1;
            var scan = window.ScanAsync(false);
            window.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await scan;
            Check("Cancellation is explicit", window.StatusText.Text.Contains("annullato"));
            Check("Cancelled scan does not export incomplete data", !window.ExportButton.IsEnabled);
            await window.ScanAsync(false);
            window.Tabs.SelectedIndex = 2;
            Check("DriverPack tab has its own controls", window.FilterToolbar.Visibility == Visibility.Collapsed && !window.InstallPackButton.IsEnabled);
            window.UpdateLayout();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture(window, Path.Combine(outputDirectory, "cosmodriver-driverpacks.png"));
            window.Tabs.SelectedIndex = 3;
            Check("Restore point control is available without automatic mutation", window.CreateRestoreButton.IsEnabled);
            window.UpdateLayout();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture(window, Path.Combine(outputDirectory, "cosmodriver-restore.png"));
            // Scan a genuine Windows INF on real devices. This API path never installs or stages a driver.
            string fixtures = Path.Combine(Path.GetTempPath(), "CosmoDriver-INF-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(fixtures);
            try
            {
                string systemInf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "INF", "machine.inf");
                File.Copy(systemInf, Path.Combine(fixtures, "machine.inf"), overwrite: true);
                var candidates = await Task.Run(() => LocalDriverPacks.Scan(fixtures));
                Check("Real SetupAPI matches an inbox INF against present devices", candidates.Count > 0);
                Check("All local candidates carry confirmed hardware identity and file hashes", candidates.All(c => !string.IsNullOrWhiteSpace(c.DeviceId) && c.InfSha256.Length == 64));
                await window.AnalyzePacksAsync(fixtures);
                Check("DriverPack scan populates real candidate table", window.PacksGrid.Items.Count > 0);
            }
            finally { Directory.Delete(fixtures,true); }
            window.Tabs.SelectedIndex = 0;
            window.Width = 1000; window.Height = 720;
            window.UpdateLayout();
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Capture(window, Path.Combine(outputDirectory, "cosmodriver-compact.png"));
            Check("Minimum-size layout rendered", window.ActualWidth >= 1000);
            File.WriteAllText(Path.Combine(outputDirectory, "smoke-results.json"), JsonSerializer.Serialize(new { Passed = checks.Count, Checks = checks, Success = true }, Core.Protocol.JsonOptions));
            return 0;
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(outputDirectory, "smoke-results.json"), JsonSerializer.Serialize(new { Passed = checks.Count, Checks = checks, Success = false, Error = ex.ToString() }, Core.Protocol.JsonOptions));
            return 1;
        }
        finally { window.Close(); }
    }

    private static void Capture(Window window, string path)
    {
        var content = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}
