using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using CosmoDriver.Core;

namespace CosmoDriver;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length == 3 && e.Args[0] == "--export-brand")
        {
            Brand.Export(e.Args[1], e.Args[2]); Shutdown(); return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--install-worker")
        {
            Shutdown(DriverInstaller.RunWorker(e.Args[1]));
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--smoke")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var window = new MainWindow(automaticStartup: false);
            window.Loaded += async (_, _) => Shutdown(await SmokeHarness.RunAsync(window, e.Args[1]));
            window.Show();
            return;
        }
        if (e.Args.Length == 2 && e.Args[0] == "--apply-update")
        {
            Shutdown(AppUpdater.Apply(e.Args[1]));
            return;
        }
        if (e.Args.Length > 0 && e.Args[0] == "--probe")
        {
            var result = e.Args.Length == 2 ? NativeProbe.Run(e.Args[1])
                : new ProbeResult(false, [], [], "INVALID_MODE");
            using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
            output.Write(JsonSerializer.Serialize(result, Protocol.JsonOptions));
            output.Flush();
            Shutdown(result.Success ? 0 : 1);
            return;
        }
        if (e.Args.Length != 0) { Shutdown(2); return; }
        new MainWindow().Show();
    }
}
