using System.Diagnostics;
using System.IO;
using System.Windows;

namespace BloodborneLauncher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // `BloodborneLauncher.exe --play` (shortcuts): the game with the saved settings, no window.
        if (e.Args.Contains("--play"))
        {
            var runner = Path.Combine(Store.PackageDir, "bbport-run.exe");
            if (File.Exists(runner))
                Process.Start(new ProcessStartInfo(runner, "--play")
                {
                    WorkingDirectory = Store.PackageDir, UseShellExecute = false, CreateNoWindow = true,
                });
            Shutdown();
            return;
        }
        base.OnStartup(e);
    }
}
