using Microsoft.Win32;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace BloodborneLauncher;

// The old launcher's window over bbport 0.7's runtime: every setting is a key of
// launcher/settings_schema.json, kept by Store (settings.json in %APPDATA%\bbport-launcher for the
// launcher keys, bbport.ini for the game's), and Play runs bbport-run.exe --play, which reads them.
public partial class MainWindow : Window
{
    readonly Store store = new();
    static string root => Store.PackageDir;
    Process? game;
    bool gameRunning => game is not null;
    bool syncing;
    readonly StringBuilder log = new();
    StreamWriter? logFile;
    List<(string Index, string Name, bool Best)>? gpus;

    /// The old pages' controls and the schema key each one shows.
    readonly List<(Control Control, string Key)> bound = new();

    /// The keys an old control shows: the generated rows under it leave them out.
    static readonly HashSet<string> OldKeys = new()
    {
        "game_dir", "language", "output_res", "present_mode", "fullscreen", "show_fps", "upscaler", "preset", "sharpen",
        "effect_chromatic_aberration", "effect_dof", "effect_motion_blur", "effect_ssao", "effect_dynamic_shadows",
        "effect_ssr", "skip_intro", "fps_mode", "model_lod", "gamepad", "mouse_look", "mouse_sensitivity",
        "mouse_invert_x", "mouse_invert_y",
    };

    // shortcut: BB_LAUNCHER_RUNNER only lets Verify-Launcher.ps1 stand in a fake runner.
    static string Runner => Environment.GetEnvironmentVariable("BB_LAUNCHER_RUNNER") is { Length: > 0 } runner
        ? runner : Path.Combine(Store.PackageDir, "bbport-run.exe");

    public MainWindow()
    {
        InitializeComponent();
        // Portable layout: a dump next to the launcher is the game until another folder is chosen.
        if (store.Get("game_dir").Length == 0 && Directory.Exists(Path.Combine(root, "CUSA03173")))
            store.Set("game_dir", Path.Combine(root, "CUSA03173"));
        // This launcher is English; with no choice bbport-run.exe would give the in-game menu Russian.
        if (store.Settings["ui_language"] is null)
            store.Set("ui_language", "en");
        BindControls();
        store.Changed += _ => RefreshBound();
        InitializePresentation();
        LoadHeroImage();
        RefreshStatus();
        Closing += (_, _) => store.Save();
        CheckForUpdate();
    }

    void BindControls()
    {
        foreach (var (box, key) in new[] {
            (FullscreenCheck, "fullscreen"), (ShowFpsCheck, "show_fps"), (SharpenCheck, "sharpen"),
            (ChromaticCheck, "effect_chromatic_aberration"), (DofCheck, "effect_dof"), (MotionBlurCheck, "effect_motion_blur"),
            (SsaoCheck, "effect_ssao"), (ShadowsCheck, "effect_dynamic_shadows"), (SsrCheck, "effect_ssr"),
            (SkipIntroCheck, "skip_intro"), (MouseCameraCheck, "mouse_look"), (MouseInvertXCheck, "mouse_invert_x"),
            (MouseInvertYCheck, "mouse_invert_y") })
        {
            box.Checked += (_, _) => { if (!syncing) store.Set(key, "1"); };
            box.Unchecked += (_, _) => { if (!syncing) store.Set(key, "0"); };
            bound.Add((box, key));
        }
        foreach (var (box, key) in new[] {
            (UpscalerCombo, "upscaler"), (QuickUpscaler, "upscaler"), (PresetCombo, "preset"), (QuickPreset, "preset"),
            (FpsCombo, "fps_mode"), (QuickFps, "fps_mode"), (PresentCombo, "present_mode"), (LanguageCombo, "language"),
            (ModelLodCombo, "model_lod") })
            BindCombo(box, key, Choices(store.Item(key)!));
        var outputs = OutputChoices();
        BindCombo(ResolutionCombo, "output_res", outputs);
        BindCombo(QuickResolution, "output_res", outputs);
        BindCombo(QuickDisplay, "fullscreen", new() { ("1", "Borderless Fullscreen", ""), ("0", "Windowed", "") });
        // The gamepads are listed when the Controls page opens (bb-gpu-capabilities.exe --gamepads).
        BindCombo(ControllerCombo, "gamepad", new() { ("", T("first_pad"), "") });

        var sensitivity = store.Item("mouse_sensitivity")!;
        MouseSensitivitySlider.Minimum = (double?)sensitivity["min"] ?? 0;
        MouseSensitivitySlider.Maximum = (double?)sensitivity["max"] ?? 10;
        MouseSensitivitySlider.TickFrequency = MouseSensitivitySlider.SmallChange = (double?)sensitivity["step"] ?? 1;
        MouseSensitivitySlider.ValueChanged += (_, _) =>
        {
            if (!syncing) store.Set("mouse_sensitivity", MouseSensitivitySlider.Value.ToString("0.00", CultureInfo.InvariantCulture));
        };
        bound.Add((MouseSensitivitySlider, "mouse_sensitivity"));
        RefreshBound();
    }

    void BindCombo(ComboBox box, string key, List<(string Value, string Label, string Note)> choices)
    {
        FillCombo(box, choices);
        box.SelectionChanged += (_, _) =>
        {
            if (syncing || box.SelectedItem is not ComboBoxItem { Tag: string value } || value == store.Get(key)) return;
            if (key == "gamepad") SetGamepad(value);
            else store.Set(key, value);
        };
        bound.Add((box, key));
    }

    static void FillCombo(ComboBox box, List<(string Value, string Label, string Note)> choices)
    {
        box.Items.Clear();
        foreach (var (value, label, _) in choices)
            box.Items.Add(new ComboBoxItem { Tag = value, Content = label });
    }

    /// The old controls show the store's values (after a change anywhere, a reload, a game run).
    void RefreshBound()
    {
        if (syncing) return;
        syncing = true;
        try
        {
            foreach (var (control, key) in bound)
            {
                string value = store.Get(key);
                switch (control)
                {
                    case CheckBox box: box.IsChecked = value == "1"; break;
                    case Slider slider:
                        slider.Value = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : slider.Minimum;
                        break;
                    case ComboBox combo:
                        // A value the list does not have (set in the game's menu) stays selectable.
                        if (!combo.Items.Cast<ComboBoxItem>().Any(item => (string)item.Tag == value))
                            combo.Items.Add(new ComboBoxItem { Tag = value, Content = value });
                        SetComboTag(combo, value);
                        break;
                }
            }
            string fps = store.Get("fps_mode");
            FooterFps.Text = $"   |   {(fps == "uncap" ? "Unlimited" : fps + " FPS")}";
            UpdateDependents();
        }
        finally { syncing = false; }
    }

    void RefreshStatus()
    {
        string gameDir = store.Get("game_dir");
        PathBox.Text = gameDir;
        bool valid = gameDir.Length > 0 && File.Exists(Path.Combine(gameDir, "eboot.bin")) && Directory.Exists(Path.Combine(gameDir, "sce_sys"));
        string version = valid ? DetectGameVersion(gameDir) : "";

        GameVersionStatus.Text = valid ? version : "Not configured";
        GameVersionStatus.Foreground = valid && version.Contains("1.09") ? Green() : Red();
        PathValidationText.Text = valid ? $"Game dump detected · {version} · {gameDir}" : "Choose the CUSA03173 folder (eboot.bin and sce_sys) on the Play page.";
        PlayButton.IsEnabled = valid && !gameRunning;
        PlayHint.Text = valid ? "Ready. Settings are saved automatically when Play is pressed." : "Choose a valid Bloodborne dump (CUSA03173) before playing.";

        bool runtime = File.Exists(Runner);
        RuntimeStatus.Text = runtime ? "Ready" : "bbport-run.exe not found";
        RuntimeStatus.Foreground = runtime ? Green() : Red();

        // As the Graphics page's upscaler files row: the bridge, then the player's or the bundled DLLs.
        string bin = Path.Combine(root, "bin");
        string user = store.Get("user_dir") is { Length: > 0 } chosen ? chosen : Path.Combine(Store.DataDir, "user");
        bool bridge = File.Exists(Path.Combine(bin, "bbport_dlss.dll"));
        bool dlss = File.Exists(Path.Combine(user, "dlss", "nvngx_dlss.dll")) || File.Exists(Path.Combine(bin, "nvngx_dlss.dll"));
        DlssStatus.Text = !bridge ? "Not in this build" : dlss ? "Available" : "Model DLL not installed";
        DlssStatus.Foreground = bridge && dlss ? Green() : Muted();

        bool fsr411 = Directory.Exists(Path.Combine(Store.DataDir, "fsr4_411"));
        bool fsr4 = Directory.Exists(Path.Combine(root, "fsr4_shaders"));
        Fsr4Status.Text = fsr411 ? "4.1.1 available" : fsr4 ? "v07 available" : "Assets not installed";
        Fsr4Status.Foreground = (fsr411 || fsr4) ? Green() : Muted();

        DetectGpu();
        RefreshBound();
    }

    static System.Windows.Media.Brush Green() => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(117, 207, 121));
    static System.Windows.Media.Brush Red() => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(210, 107, 112));
    static System.Windows.Media.Brush Muted() => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(142, 150, 158));

    static string DetectGameVersion(string game)
    {
        try
        {
            string sfo = Path.Combine(game, "sce_sys", "param.sfo");
            if (!File.Exists(sfo)) return "Dump found · version unknown";
            string text = Encoding.UTF8.GetString(File.ReadAllBytes(sfo));
            if (text.Contains("01.09", StringComparison.Ordinal)) return "1.09 ✓";
            if (text.Contains("01.08", StringComparison.Ordinal)) return "1.08";
            if (text.Contains("01.00", StringComparison.Ordinal)) return "1.00";
            return "Dump found · verify 1.09";
        }
        catch { return "Dump found · version unknown"; }
    }

    bool gpuDetected;

    /// bin\bb-gpu-capabilities.exe --gpus (index, vendor, type, name, the port's choice), once a session.
    async void DetectGpu()
    {
        string caps = Path.Combine(root, "bin", "bb-gpu-capabilities.exe");
        if (!File.Exists(caps))
        {
            GpuStatus.Text = "GPU probe not found";
            FooterGpu.Text = "   |   GPU unknown";
            return;
        }
        if (gpuDetected) return;
        gpuDetected = true;
        try
        {
            using var p = Process.Start(new ProcessStartInfo(caps, "--gpus")
            {
                WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true,
            });
            if (p == null) return;
            string output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            gpus = output.Split('\n').Select(line => line.TrimEnd('\r').Split('\t')).Where(parts => parts.Length == 5)
                .Select(parts => (parts[0], parts[3], parts[4] == "1")).ToList();
            string gpu = gpus.FirstOrDefault(g => g.Best).Name ?? gpus.FirstOrDefault().Name ?? "No Vulkan GPU found";
            GpuStatus.Text = gpu;
            FooterGpu.Text = "   |   " + gpu;
        }
        catch
        {
            GpuStatus.Text = "GPU unknown";
            FooterGpu.Text = "   |   GPU unknown";
        }
    }

    void LoadHeroImage()
    {
        HeroImage.Source = new BitmapImage(new Uri("pack://application:,,,/BloodborneLauncher;component/Assets/hunter-background.jpg"));
        string game = store.Get("game_dir");
        if (game.Length == 0) return;
        string[] candidates =
        {
            Path.Combine(game, "sce_sys", "pic1.png"),
            Path.Combine(game, "sce_sys", "pic0.png"),
        };
        foreach (string path in candidates)
        {
            if (!File.Exists(path)) continue;
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                using var fs = File.OpenRead(path);
                bitmap.StreamSource = fs;
                bitmap.EndInit();
                bitmap.Freeze();
                HeroImage.Source = bitmap;
                return;
            }
            catch { }
        }
    }

    void BrowseGame_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "The game folder (CUSA03173: eboot.bin and sce_sys)" };
        if (Directory.Exists(store.Get("game_dir"))) dialog.InitialDirectory = store.Get("game_dir");
        if (dialog.ShowDialog(this) != true) return;
        store.Set("game_dir", dialog.FolderName);
        store.Save();
        RefreshStatus();
        LoadHeroImage();
    }

    void Play_Click(object sender, RoutedEventArgs e)
    {
        if (gameRunning) return;
        try { store.Save(); }
        catch (Exception error)
        {
            FooterMessage.Text = "Could not save settings: " + error.Message;
            return;
        }
        string gameDir = store.Get("game_dir");
        if (gameDir.Length == 0 || !File.Exists(Path.Combine(gameDir, "eboot.bin")))
        {
            MessageBox.Show(T("no_game"), "Bloodborne PC", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!File.Exists(Runner))
        {
            MessageBox.Show(T("no_runner"), "Bloodborne PC", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        try
        {
            string logsDir = Path.Combine(Store.DataDir, "logs");
            Directory.CreateDirectory(logsDir);
            string logPath = Path.Combine(logsDir, $"launcher-{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
            // As 0.7's launcher: bbport-run.exe --play reads settings.json and bbport.ini and runs run.py.
            var psi = new ProcessStartInfo(Runner, "--play")
            {
                WorkingDirectory = Store.PackageDir, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, line) => AddLine(line.Data);
            process.ErrorDataReceived += (_, line) => AddLine(line.Data);
            process.Exited += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                int code = process.ExitCode;
                process.Dispose();
                game = null;
                lock (log) { logFile?.Dispose(); logFile = null; }
                Show();
                WindowState = WindowState.Normal;
                store.Load(); // the in-game menu may have changed bbport.ini
                RefreshStatus();
                Activate();
                FooterMessage.Text = string.Format(T("exited"), code);
            });
            lock (log) { log.Clear(); logFile = new StreamWriter(logPath, false, new UTF8Encoding(false)) { AutoFlush = true }; }
            game = process;
            PlayButton.IsEnabled = false;
            if (!process.Start()) throw new InvalidOperationException("Could not start the game.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            FooterMessage.Text = $"Started · log: {Path.GetFileName(logPath)}";
            Hide();
        }
        catch (Exception ex)
        {
            game = null;
            lock (log) { logFile?.Dispose(); logFile = null; }
            RefreshStatus();
            MessageBox.Show(ex.Message, "Unable to start Bloodborne", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// The game's (or the FSR 4.1.1 build's) output: the session log file and the last lines kept.
    void AddLine(string? line)
    {
        if (line is null) return;
        lock (log)
        {
            if (log.Length > 1_000_000) log.Remove(0, log.Length - 600_000);
            log.Append(line).Append('\n');
            logFile?.WriteLine(line);
        }
    }

    void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            store.Save();
            FooterMessage.Text = "Settings saved";
        }
        catch (Exception error) { FooterMessage.Text = "Could not save settings: " + error.Message; }
        RefreshStatus();
    }

    void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        string logs = Path.Combine(Store.DataDir, "logs");
        Directory.CreateDirectory(logs);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{logs}\"") { UseShellExecute = true });
    }

    void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        string page = button.CommandParameter?.ToString() ?? "Home";
        foreach (var b in new[] { NavHome, NavGeneral, NavGraphics, NavPerformance, NavController, NavOnline, NavMods, NavAdvanced, NavTrophies }) b.Tag = null;
        button.Tag = "active";
        // The schema settings no old control shows, rebuilt on each visit (as 0.7's sections).
        switch (page)
        {
            case "Home": RefreshStatus(); break;
            case "General": BuildPage(GeneralExtra, new[] { "game" }, OldKeys); break;
            case "Graphics": BuildPage(GraphicsExtra, new[] { "display", "graphics", "effects" }, OldKeys); break;
            case "Performance": BuildPage(PerformanceExtra, new[] { "performance" }, OldKeys); break;
            case "Controller":
                syncing = true;
                FillCombo(ControllerCombo, GamepadChoices());
                syncing = false;
                RefreshBound();
                BuildPage(ControllerExtra, new[] { "controls" }, OldKeys);
                break;
            case "Online": BuildPage(OnlineItems, new[] { "online" }, OldKeys); break;
            case "Mods": BuildPage(ModsItems, new[] { "mods" }, OldKeys); break;
            case "Advanced": BuildPage(AdvancedItems, new[] { "developer" }, OldKeys); break;
            case "Trophies": BuildTrophies(); break;
        }
        HomePage.Visibility = page == "Home" ? Visibility.Visible : Visibility.Collapsed;
        SettingsScrim.Visibility = page == "Home" ? Visibility.Collapsed : Visibility.Visible;
        GeneralPage.Visibility = page == "General" ? Visibility.Visible : Visibility.Collapsed;
        GraphicsPage.Visibility = page == "Graphics" ? Visibility.Visible : Visibility.Collapsed;
        PerformancePage.Visibility = page == "Performance" ? Visibility.Visible : Visibility.Collapsed;
        ControllerPage.Visibility = page == "Controller" ? Visibility.Visible : Visibility.Collapsed;
        ModsPage.Visibility = page == "Mods" ? Visibility.Visible : Visibility.Collapsed;
        OnlinePage.Visibility = page == "Online" ? Visibility.Visible : Visibility.Collapsed;
        AdvancedPage.Visibility = page == "Advanced" ? Visibility.Visible : Visibility.Collapsed;
        TrophiesPage.Visibility = page == "Trophies" ? Visibility.Visible : Visibility.Collapsed;
    }

    static string ComboTag(ComboBox box, string fallback = "") =>
        (box.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;

    static void SetComboTag(ComboBox box, string tag)
    {
        foreach (var obj in box.Items)
            if (obj is ComboBoxItem item && (item.Tag?.ToString() ?? "") == tag) { box.SelectedItem = item; return; }
        if (box.Items.Count > 0) box.SelectedIndex = 0;
    }

    void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) Maximize_Click(sender, e);
        else DragMove();
    }
    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
