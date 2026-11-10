// SPDX-FileCopyrightText: Copyright 2026 bbport contributors
// SPDX-License-Identifier: GPL-2.0-or-later
// The settings of launcher/settings_schema.json that the old pages have no control of, as rows in
// the old launcher's panels: ported from bbport 0.7's launcher/windows/MainWindow.xaml.cs.
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace BloodborneLauncher;

public partial class MainWindow
{
    private static readonly Dictionary<string, (string Ru, string En)> Texts = new()
    {
        ["play"] = ("Играть", "Play"),
        ["stop"] = ("Остановить", "Stop"),
        ["log"] = ("Журнал", "Log"),
        ["log_description"] = ("Вывод последнего запуска. Полные журналы — в папке logs.",
                               "The output of the last run. Full logs are in the logs folder."),
        ["open_logs"] = ("Открыть папку журналов", "Open the logs folder"),
        ["copy_log"] = ("Скопировать", "Copy"),
        ["browse"] = ("Обзор…", "Browse…"),
        ["reset"] = ("По умолчанию", "Default"),
        ["auto"] = ("Авто", "Auto"),
        ["system"] = ("Как в системе", "As the system"),
        ["auto_monitor"] = ("Авто (как у монитора)", "Auto (the monitor's)"),
        ["auto_gpu"] = ("Авто (дискретная)", "Auto (the discrete one)"),
        ["default_user"] = ("По умолчанию: {0}", "Default: {0}"),
        ["files_dlss"] = ("DLSS: библиотеки NVIDIA", "DLSS: NVIDIA libraries"),
        ["files_dlss_description"] = ("nvngx_dlss.dll (апскейлер) и nvngx_dlssg.dll (генерация кадров, RTX 40/50). В сборке с DLSS они уже есть; свои (из любой игры с DLSS) копируются в user\\dlss и берутся раньше встроенных. Нужна GeForce RTX.",
                                      "nvngx_dlss.dll (upscaler) and nvngx_dlssg.dll (frame generation, RTX 40/50). A DLSS build brings them; your own (from any game with DLSS) are copied to user\\dlss and preferred. Needs a GeForce RTX."),
        ["files_fsr4"] = ("FSR 4: ассеты модели", "FSR 4: model assets"),
        ["files_fsr4_description"] = ("Модель FSR 4 v07 (INT8; MIT, собрана из исходников AMD) входит в сборку.",
                                      "The FSR 4 v07 model (INT8; MIT, built from AMD's source) comes with the build."),
        ["fsr4_found"] = ("Ассеты найдены.", "The assets are found."),
        ["fsr4_missing"] = ("Нет папки fsr4_shaders рядом с лаунчером: распакуйте архив порта целиком. Игра включит FSR 3.1.",
                            "No fsr4_shaders folder next to the launcher: unpack the whole archive. The game falls back to FSR 3.1."),
        ["files_fsr411"] = ("FSR 4.1.1: ассеты из своей DLL AMD", "FSR 4.1.1: assets from your AMD DLL"),
        ["files_fsr411_description"] = ("Порт не распространяет файлы AMD. «Собрать из DLL AMD…» делает набор из вашей amd_fidelityfx_upscaler_dx12.dll 4.1.x (с amdxcffx64.dll драйвера — ещё модели FSR 4.1.2); можно и выбрать готовую папку fsr4_411.",
                                        "The port ships nothing of AMD's. \"Build from an AMD DLL…\" makes the set from your amd_fidelityfx_upscaler_dx12.dll 4.1.x (with the driver's amdxcffx64.dll, FSR 4.1.2's models too); you can also choose a built fsr4_411 folder."),
        ["fsr411_found"] = ("Набор есть: {0}", "The set is there: {0}"),
        ["fsr411_missing"] = ("Набора нет: игра включит FSR 4 (или FSR 3.1).", "No set: the game falls back to FSR 4 (or FSR 3.1)."),
        ["fsr411_bad"] = ("В выбранной папке нет файлов набора FSR 4.1.1.", "The chosen folder has no FSR 4.1.1 set."),
        ["fsr411_build"] = ("Собрать из DLL AMD…", "Build from an AMD DLL…"),
        ["fsr411_build_title"] = ("amd_fidelityfx_upscaler_dx12.dll 4.1.x (и, для моделей FSR 4.1.2, amdxcffx64.dll драйвера)",
                                  "amd_fidelityfx_upscaler_dx12.dll 4.1.x (and, for FSR 4.1.2's models, the driver's amdxcffx64.dll)"),
        ["fsr411_building"] = ("Идёт сборка (минута-две): ход в журнале.", "Building (a minute or two): progress in the log."),
        ["fsr411_build_done"] = ("Набор FSR 4.1.1 собран.", "The FSR 4.1.1 set is built."),
        ["fsr411_build_failed"] = ("Сборка не удалась (код {0}): подробности в журнале.", "The build failed (code {0}): details in the log."),
        ["files_choose"] = ("Выбрать файлы…", "Choose files…"),
        ["files_choose_folder"] = ("Выбрать папку…", "Choose a folder…"),
        ["files_remove"] = ("Убрать выбранные", "Remove chosen"),
        ["files_chosen"] = ("выбранные", "chosen"),
        ["files_bundled"] = ("из сборки", "bundled"),
        ["dlss_none"] = ("Нет: DLSS недоступен (выберите nvngx_dlss.dll из игры с DLSS).",
                         "None: DLSS is unavailable (choose nvngx_dlss.dll from a game with DLSS)."),
        ["dlss_no_bridge"] = ("В этой сборке нет моста DLSS (bin\\bbport_dlss.dll): DLSS недоступен.",
                              "This build has no DLSS bridge (bin\\bbport_dlss.dll): DLSS is unavailable."),
        ["dlss_bad"] = ("Нужны файлы nvngx_dlss.dll и/или nvngx_dlssg.dll.",
                        "Choose nvngx_dlss.dll and/or nvngx_dlssg.dll."),
        ["no_game"] = ("Выберите папку игры (CUSA03173) в разделе «Игра».",
                       "Choose the game folder (CUSA03173) in the Game section."),
        ["bad_game"] = ("В папке игры нет eboot.bin.", "The game folder has no eboot.bin."),
        ["game_ok"] = ("Игра найдена.", "The game is found."),
        ["no_runner"] = ("Нет bbport-run.exe рядом с лаунчером: распакуйте архив порта целиком.",
                         "No bbport-run.exe next to the launcher: unpack the whole port archive."),
        ["fg_off"] = ("Выключена", "Off"),
        ["fg_fsr3"] = ("FSR 3.1 (экспериментально)", "FSR 3.1 (experimental)"),
        ["fg_dlss2"] = ("DLSS 2x (NVIDIA RTX 40/50)", "DLSS 2x (NVIDIA RTX 40/50)"),
        ["fg_dlss3"] = ("DLSS 3x (NVIDIA RTX 50)", "DLSS 3x (NVIDIA RTX 50)"),
        ["fg_dlss4"] = ("DLSS 4x (NVIDIA RTX 50)", "DLSS 4x (NVIDIA RTX 50)"),
        ["exited"] = ("Игра завершилась, код {0}.", "The game exited, code {0}."),
        ["primary"] = ("Основной", "Primary"),
        ["is_primary"] = (", основной", ", primary"),
        ["first_pad"] = ("Авто (первый подключённый)", "Auto (the first connected)"),
        ["not_connected"] = ("{0} (не подключён)", "{0} (not connected)"),
        ["update_available"] = ("Обновить до {0}", "Update to {0}"),
        ["update_title"] = ("Обновление", "Update"),
        ["update_close_game"] = ("Закройте игру перед обновлением.", "Close the game before updating."),
        ["update_confirm"] = ("Скачать и установить {0} с github.com/{1}?\n\nФайлы порта и лаунчера заменятся. Настройки, bbport.ini, моды, сохранения и игра останутся. Лаунчер перезапустится.",
                              "Download and install {0} from github.com/{1}?\n\nThe port and launcher files are replaced. Your settings, bbport.ini, mods, saves and the game are kept. The launcher restarts."),
        ["update_failed"] = ("Обновление не удалось: {0}\n\nЧасть файлов могла обновиться. Нажмите кнопку обновления ещё раз.",
                             "Update failed: {0}\n\nSome files may already be new. Press the update button again to finish."),
    };

    private string T(string key) => Translated(key, Texts[key].Ru, Texts[key].En);

    /// The old launcher is English; the tables keep their Russian for the Linux launcher's parity.
    private const string language = "en";

    private static string Translated(string key, string ru, string en) => en;

    /// The switches other settings follow (enabled_by) and the rows that follow them.
    private readonly Dictionary<string, CheckBox> switches = new();
    private readonly List<(FrameworkElement Card, string Key)> dependents = new();

    /// A schema text ({"ru": ..., "en": ...}) in the launcher language.
    private string S(JsonNode? texts) =>
        (string?)texts?[language] ?? (string?)texts?["en"] ?? (string?)texts?["ru"] ?? "";

    // --- the cards ------------------------------------------------------------------------------

    /// The page on screen (its panel, sections and the keys its own controls show): rebuilt when a
    /// choice that other items' visible_when names changes (the Online section's pool).
    private (Panel Target, string[] Sections, HashSet<string> Skip)? shownPage;

    /// An item's visible_when ({"key": ..., "value": ...}) holds now, or it has none.
    private bool Visible(JsonObject item) =>
        item["visible_when"] is not JsonObject when || store.Get((string)when["key"]!) == (string?)when["value"];

    /// The settings of the sections that no control of the page shows, one old-style panel per
    /// group (Upscaler, Image quality, ...) under its heading.
    private void BuildPage(Panel target, string[] sections, HashSet<string> skip)
    {
        shownPage = (target, sections, skip);
        target.Children.Clear();
        switches.Clear();
        dependents.Clear();
        foreach (var id in sections)
        {
            var section = store.Schema["sections"]!.AsArray().Select(s => s!.AsObject()).First(s => (string?)s["id"] == id);
            StackPanel? panel = null;
            string? group = null;
            foreach (var item in section["items"]!.AsArray().Select(i => i!.AsObject()).Where(Store.OnWindows).Where(Visible)
                         .Where(i => !skip.Contains((string)i["key"]!)))
            {
                var card = BuildItem(item);
                if (card is null)
                    continue;
                var title = S(item["group"]);
                if (panel is null || (title.Length > 0 && title != group))
                {
                    panel = new StackPanel();
                    panel.Children.Add(new TextBlock
                    {
                        Text = (title.Length > 0 ? title : S(section["title"])).ToUpperInvariant(),
                        Style = (Style)FindResource("SectionTitle"),
                    });
                    target.Children.Add(new Border { Style = (Style)FindResource("CardStyle"), Margin = new Thickness(0, 16, 0, 0), Child = panel });
                    group = title;
                }
                panel.Children.Add(card);
                if ((string?)item["enabled_by"] is { } by)
                    dependents.Add((card, by));
            }
        }
        UpdateDependents();
    }

    private void RebuildPage()
    {
        if (shownPage is var (target, sections, skip))
            BuildPage(target, sections, skip);
    }

    /// Rows follow their switch, a generated one or a control of the old page (store values).
    private void UpdateDependents()
    {
        foreach (var (card, key) in dependents)
            card.IsEnabled = switches.TryGetValue(key, out var box) ? box.IsChecked == true : store.Get(key) == "1";
    }

    /// A card: the title and description on the left, the control on the right; a note line under
    /// the description (the selected choice's) when there is one.
    private (Border Card, TextBlock Note) Card(JsonObject item, FrameworkElement control, bool wide = false)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
        text.Children.Add(new TextBlock { Text = S(item["title"]), FontSize = 14, TextWrapping = TextWrapping.Wrap });
        // Old launcher rows: the label, a muted description, the control at the right.
        var description = S(item["description"]);
        if (description.Length > 0)
        {
            var block = new TextBlock
            {
                Text = description, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("MutedBrush"),
                Margin = new Thickness(0, 2, 0, 0),
            };
            text.Children.Add(block);
        }
        var note = new TextBlock
        {
            FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
            Visibility = Visibility.Collapsed,
        };
        note.Foreground = new SolidColorBrush(Color.FromRgb(0xD9, 0xA4, 0x7A));
        text.Children.Add(note);

        var grid = new Grid();
        if (wide)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(control, 1);
            control.Margin = new Thickness(0, 8, 0, 0);
        }
        else
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(control, 1);
            control.VerticalAlignment = VerticalAlignment.Center;
        }
        grid.Children.Add(text);
        grid.Children.Add(control);
        var card = new Border
        {
            Child = grid, Padding = new Thickness(0, 10, 0, 10), BorderThickness = new Thickness(0, 0, 0, 1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x60, 0x51, 0x48, 0x3B)),
        };
        return (card, note);
    }

    private static void ShowNote(TextBlock note, string text)
    {
        note.Text = text;
        note.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private FrameworkElement? BuildItem(JsonObject item)
    {
        var key = (string)item["key"]!;
        switch ((string?)item["type"])
        {
            case "bool":
            {
                var box = new CheckBox { IsChecked = store.Get(key) == "1", MinWidth = 0 };
                box.Click += (_, _) =>
                {
                    store.Set(key, box.IsChecked == true ? "1" : "0");
                    UpdateDependents();
                };
                switches[key] = box;
                return Card(item, box).Card;
            }
            case "choice":
                return ChoiceCard(item, Choices(item), store.Get(key), value =>
                {
                    store.Set(key, value);
                    if (store.Items.Any(other => (string?)other["visible_when"]?["key"] == key))
                        Dispatcher.BeginInvoke(RebuildPage);
                });
            case "float":
            case "int":
                return NumberCard(item, key);
            case "text":
            {
                var box = new TextBox { Text = store.Get(key), MinWidth = 320 };
                box.TextChanged += (_, _) => store.Set(key, box.Text.Trim());
                return Card(item, box, wide: true).Card;
            }
            case "password":
            {
                var box = new PasswordBox { Password = store.Get(key), MinWidth = 320 };
                box.PasswordChanged += (_, _) => store.Set(key, box.Password);
                return Card(item, box, wide: true).Card;
            }
            case "special":
                return SpecialCard(item, key);
        }
        return null;
    }

    private List<(string Value, string Label, string Note)> Choices(JsonObject item)
    {
        var list = item["choices"] as JsonArray
                   ?? store.Schema["lists"]![(string)item["choices_ref"]!]!.AsArray();
        return list.Select(c => c!.AsObject()).Select(c =>
            ((string)c["value"]!, (string?)c[language] ?? (string?)c["en"] ?? (string)c["value"]!,
             S(c["description"]))).ToList();
    }

    private Border ChoiceCard(JsonObject item, List<(string Value, string Label, string Note)> choices,
                              string current, Action<string> changed,
                              Task<List<(string Value, string Label, string Note)>>? probed = null)
    {
        var combo = new ComboBox { MinWidth = 220 };
        var (card, note) = Card(item, combo);
        var filling = false;
        void Fill()
        {
            filling = true;
            combo.Items.Clear();
            foreach (var (value, label, _) in choices)
                combo.Items.Add(new ComboBoxItem { Content = label, Tag = value });
            var index = choices.FindIndex(c => c.Value == current);
            combo.SelectedIndex = index >= 0 ? index : 0;
            ShowNote(note, choices[combo.SelectedIndex].Note);
            filling = false;
        }
        Fill();
        combo.SelectionChanged += (_, _) =>
        {
            if (filling || combo.SelectedIndex < 0)
                return;
            ShowNote(note, choices[combo.SelectedIndex].Note);
            current = choices[combo.SelectedIndex].Value;
            changed(current);
        };
        // The choices a probe of the machine lists come when it returns: the page may be rebuilt by then.
        var gone = false;
        card.Unloaded += (_, _) => gone = true;
        async void FillProbed()
        {
            var listed = await probed!;
            if (gone)
                return;
            choices = listed;
            Fill();
        }
        if (probed is not null)
            FillProbed();
        return card;
    }

    private Border NumberCard(JsonObject item, string key)
    {
        var isFloat = (string?)item["type"] == "float";
        double min = (double?)item["min"] ?? 0, max = (double?)item["max"] ?? 100, step = (double?)item["step"] ?? 1;
        double.TryParse(store.Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var value);
        var format = isFloat ? "0.00" : "0";
        if (!isFloat)
        {
            // Whole numbers (the FPS limit): typed.
            var box = new TextBox { Text = value.ToString(format, CultureInfo.InvariantCulture), Width = 90 };
            box.TextChanged += (_, _) =>
            {
                if (int.TryParse(box.Text, out var number) && number >= min && number <= max)
                    store.Set(key, number.ToString(CultureInfo.InvariantCulture));
            };
            return Card(item, box).Card;
        }
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var shown = new TextBlock
        {
            Width = 44, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0),
        };
        var slider = new Slider
        {
            Minimum = min, Maximum = max, SmallChange = step, LargeChange = step * 4, TickFrequency = step,
            IsSnapToTickEnabled = true, Width = 200, Value = Math.Clamp(value, min, max),
            VerticalAlignment = VerticalAlignment.Center,
        };
        shown.Text = slider.Value.ToString(format, CultureInfo.CurrentCulture);
        slider.ValueChanged += (_, _) =>
        {
            shown.Text = slider.Value.ToString(format, CultureInfo.CurrentCulture);
            store.Set(key, slider.Value.ToString(format, CultureInfo.InvariantCulture));
        };
        panel.Children.Add(shown);
        panel.Children.Add(slider);
        return Card(item, panel).Card;
    }

    // --- the settings with their own controls ---------------------------------------------------

    private FrameworkElement? SpecialCard(JsonObject item, string key)
    {
        switch (key)
        {
            case "ui_language":
            {
                var choices = new List<(string, string, string)> { ("", T("system"), ""), ("ru", "Русский", ""), ("en", "English", ""),
                                                                   ("pt_BR", "Português (Brasil)", ""), ("zh_CN", "简体中文", ""), ("fr_FR", "Français", "") };
                return ChoiceCard(item, choices, store.Get(key), value =>
                {
                    store.Set(key, value); // the in-game menu's language (bbport_launcher_win.py)
                });
            }
            case "game_dir":
                return FolderCard(item, key, null);
            case "user_dir":
                return FolderCard(item, key, Path.Combine(Store.DataDir, "user"));
            case "aspect_ratio":
                return AspectCard(item);
            case "output_res":
                return OutputCard(item);
            case "frame_generation":
                return FrameGenerationCard(item);
            case "upscaler_files":
                return UpscalerFilesCard(item);
            case "gpu":
                return GpuCard(item);
            case "display":
                return DisplayCard(item);
            case "gamepad":
                return GamepadCard(item);
            case "thd_account":
                return ThdAccountCard(item); // Online.cs
            case "online_test":
                return OnlineTestCard(item);
            case "mods":
                return ModsCard(item); // Mods.cs
            case "patches":
                return PatchesCard(item);
        }
        return null;
    }

    private Border FolderCard(JsonObject item, string key, string? defaultPath)
    {
        var path = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.85 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var browse = new Button { Content = T("browse"), Padding = new Thickness(14, 5, 14, 5) };
        buttons.Children.Add(browse);
        Button? reset = null;
        if (defaultPath is not null)
        {
            reset = new Button { Content = T("reset"), Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(8, 0, 0, 0) };
            buttons.Children.Add(reset);
        }
        var panel = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        panel.Children.Add(buttons);
        path.VerticalAlignment = VerticalAlignment.Center;
        path.Margin = new Thickness(0, 0, 12, 0);
        panel.Children.Add(path);
        var (card, _) = Card(item, panel, wide: true);

        void Show()
        {
            var value = store.Get(key);
            path.Text = value.Length > 0 ? value
                : defaultPath is not null ? string.Format(T("default_user"), defaultPath) : "—";
            if (reset is not null)
                reset.IsEnabled = value.Length > 0;
            RefreshStatus();
        }
        browse.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog { Title = S(item["title"]) };
            var value = store.Get(key);
            if (value.Length > 0 && Directory.Exists(value))
                dialog.InitialDirectory = value;
            if (dialog.ShowDialog(this) == true)
            {
                store.Set(key, dialog.FolderName);
                Show();
            }
        };
        if (reset is not null)
            reset.Click += (_, _) =>
            {
                store.Set(key, "");
                Show();
            };
        Show();
        return card;
    }

    // The files the chosen upscaler needs, like the Linux launcher's library rows: DLSS, NVIDIA's
    // nvngx_dlss.dll / nvngx_dlssg.dll beside bb-probe (a build with DLSS_SDK_ROOT) or the player's
    // own in <user>\dlss, which the game prefers (vk_dlss.cpp, NgxDirectories); FSR 4, the v07
    // asset set the package brings (fsr4_shaders); FSR 4.1.1, the set built from the player's own
    // AMD DLL (fsr4_411 in the data folder, as run.sh looks it up). Hidden for the other upscalers.
    private Border UpscalerFilesCard(JsonObject item)
    {
        string[] ngx = { "nvngx_dlss.dll", "nvngx_dlssg.dll" };
        var bin = Path.Combine(AppContext.BaseDirectory, "bin");
        string UserDlss()
        {
            var user = store.Get("user_dir");
            return Path.Combine(user.Length > 0 ? user : Path.Combine(Store.DataDir, "user"), "dlss");
        }
        string Fsr411Dir() => Path.Combine(Store.DataDir, "fsr4_411");

        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.85 };
        var choose = new Button { Padding = new Thickness(14, 5, 14, 5) };
        var remove = new Button { Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(8, 0, 0, 0) };
        var build = new Button { Padding = new Thickness(14, 5, 14, 5), Margin = new Thickness(0, 0, 8, 0) };
        Process? building = null;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        buttons.Children.Add(build);
        buttons.Children.Add(choose);
        buttons.Children.Add(remove);
        var panel = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        panel.Children.Add(buttons);
        status.VerticalAlignment = VerticalAlignment.Center;
        status.Margin = new Thickness(0, 0, 12, 0);
        panel.Children.Add(status);
        var (card, _) = Card(item, panel, wide: true);
        var text = (StackPanel)((Grid)card.Child).Children[0];
        var title = (TextBlock)text.Children[0];
        var description = text.Children.Count > 1 && text.Children[1] is TextBlock block ? block : null;

        void Show()
        {
            var upscaler = store.Get("upscaler");
            card.Visibility = upscaler is "dlss" or "fsr4" or "fsr411" ? Visibility.Visible : Visibility.Collapsed;
            switch (upscaler)
            {
                case "dlss":
                {
                    title.Text = T("files_dlss");
                    if (description is not null)
                        description.Text = T("files_dlss_description");
                    var user = UserDlss();
                    var found = ngx.Select(name => File.Exists(Path.Combine(user, name)) ? $"{name} ({T("files_chosen")})"
                                                 : File.Exists(Path.Combine(bin, name)) ? $"{name} ({T("files_bundled")})" : null)
                                   .Where(entry => entry is not null).ToList();
                    status.Text = !File.Exists(Path.Combine(bin, "bbport_dlss.dll")) ? T("dlss_no_bridge")
                                : found.Count > 0 ? string.Join(", ", found) : T("dlss_none");
                    build.Visibility = Visibility.Collapsed;
                    choose.Content = T("files_choose");
                    choose.Visibility = Visibility.Visible;
                    remove.Content = T("files_remove");
                    remove.Visibility = Visibility.Visible;
                    remove.IsEnabled = ngx.Any(name => File.Exists(Path.Combine(user, name)));
                    break;
                }
                case "fsr4":
                {
                    title.Text = T("files_fsr4");
                    if (description is not null)
                        description.Text = T("files_fsr4_description");
                    var directory = Path.Combine(AppContext.BaseDirectory, "fsr4_shaders");
                    status.Text = Directory.Exists(directory) && Directory.EnumerateFiles(directory, "*.spv").Any()
                        ? T("fsr4_found") : T("fsr4_missing");
                    build.Visibility = Visibility.Collapsed;
                    choose.Visibility = Visibility.Collapsed;
                    remove.Visibility = Visibility.Collapsed;
                    break;
                }
                case "fsr411":
                {
                    title.Text = T("files_fsr411");
                    if (description is not null)
                        description.Text = T("files_fsr411_description");
                    var directory = Fsr411Dir();
                    status.Text = File.Exists(Path.Combine(directory, "manifest.json")) || (Directory.Exists(directory)
                                  && Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Any())
                        ? string.Format(T("fsr411_found"), directory) : T("fsr411_missing");
                    if (building is not null)
                        status.Text = T("fsr411_building");
                    build.Content = T("fsr411_build");
                    build.Visibility = Visibility.Visible;
                    build.IsEnabled = building is null;
                    choose.Content = T("files_choose_folder");
                    choose.Visibility = Visibility.Visible;
                    remove.Content = T("files_remove");
                    remove.Visibility = Visibility.Visible;
                    remove.IsEnabled = Directory.Exists(directory);
                    break;
                }
            }
        }

        static void CopyTree(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var file in Directory.EnumerateFiles(from))
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)), true);
            foreach (var directory in Directory.EnumerateDirectories(from))
                CopyTree(directory, Path.Combine(to, Path.GetFileName(directory)));
        }

        // FSR 4.1.1 (or FSR 4.1.2's driver models) from the player's own AMD DLLs: bbport-run.exe
        // --build-fsr411 (tools/fsr4cap/build_assets_win.py), its output in the log.
        build.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Title = T("fsr411_build_title"), Multiselect = true,
                                              Filter = "AMD FidelityFX / driver (*.dll)|*.dll" };
            if (dialog.ShowDialog(this) != true || dialog.FileNames.Length > 2)
                return;
            // The driver's module first (build_assets_win.py's second form), the upscaler after it.
            var files = dialog.FileNames.OrderBy(path => Path.GetFileName(path).ToLowerInvariant()
                                                     is "amdxcffx64.dll" or "amdvkffx64.dll" ? 0 : 1).ToList();
            var runner = Path.Combine(Store.PackageDir, "bbport-run.exe");
            if (!File.Exists(runner))
            {
                MessageBox.Show(this, T("no_runner"), "Bloodborne", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            var info = new ProcessStartInfo(runner)
            {
                WorkingDirectory = Store.PackageDir, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            info.ArgumentList.Add("--build-fsr411");
            foreach (var file in files)
                info.ArgumentList.Add(file);
            info.Environment["PYTHONIOENCODING"] = "utf-8";
            info.Environment["BB_DATA_DIR"] = Store.DataDir;
            building = new Process { StartInfo = info, EnableRaisingEvents = true };
            building.OutputDataReceived += (_, line) => AddLine(line.Data);
            building.ErrorDataReceived += (_, line) => AddLine(line.Data);
            building.Exited += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                var code = building?.ExitCode ?? -1;
                building?.Dispose();
                building = null;
                Show();
                if (code == 0)
                    MessageBox.Show(this, T("fsr411_build_done"), "Bloodborne", MessageBoxButton.OK, MessageBoxImage.Information);
                else
                    MessageBox.Show(this, string.Format(T("fsr411_build_failed"), code), "Bloodborne",
                                    MessageBoxButton.OK, MessageBoxImage.Warning);
            });
            try
            {
                building.Start();
            }
            catch (Exception error)
            {
                building = null;
                MessageBox.Show(this, error.Message, "Bloodborne", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            building.BeginOutputReadLine();
            building.BeginErrorReadLine();
            Show();
        };
        choose.Click += (_, _) =>
        {
            try
            {
                if (store.Get("upscaler") == "dlss")
                {
                    var dialog = new OpenFileDialog { Title = title.Text, Multiselect = true,
                                                      Filter = "NVIDIA DLSS (nvngx_dlss*.dll)|nvngx_dlss*.dll" };
                    if (dialog.ShowDialog(this) != true)
                        return;
                    var picked = dialog.FileNames.Where(path => ngx.Contains(Path.GetFileName(path).ToLowerInvariant())).ToList();
                    if (picked.Count == 0)
                    {
                        MessageBox.Show(this, T("dlss_bad"), "Bloodborne", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    Directory.CreateDirectory(UserDlss());
                    foreach (var path in picked)
                        File.Copy(path, Path.Combine(UserDlss(), Path.GetFileName(path).ToLowerInvariant()), true);
                }
                else
                {
                    // A set built by the Linux launcher (its data folder's fsr4_411, or the tree's).
                    var dialog = new OpenFolderDialog { Title = title.Text };
                    if (dialog.ShowDialog(this) != true)
                        return;
                    if (!Directory.EnumerateFiles(dialog.FolderName, "*", SearchOption.AllDirectories).Any())
                    {
                        MessageBox.Show(this, T("fsr411_bad"), "Bloodborne", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    var target = Fsr411Dir();
                    if (Path.GetFullPath(dialog.FolderName).TrimEnd('\\') != Path.GetFullPath(target).TrimEnd('\\'))
                    {
                        if (Directory.Exists(target))
                            Directory.Delete(target, true);
                        CopyTree(dialog.FolderName, target);
                    }
                }
            }
            catch (Exception error)
            {
                MessageBox.Show(this, error.Message, "Bloodborne", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Show();
        };
        remove.Click += (_, _) =>
        {
            try
            {
                if (store.Get("upscaler") == "dlss")
                {
                    foreach (var name in ngx)
                        File.Delete(Path.Combine(UserDlss(), name));
                }
                else if (Directory.Exists(Fsr411Dir()))
                {
                    Directory.Delete(Fsr411Dir(), true);
                }
            }
            catch (Exception error)
            {
                MessageBox.Show(this, error.Message, "Bloodborne", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Show();
        };
        void OnChanged(string key)
        {
            if (key is "upscaler" or "user_dir")
                Dispatcher.BeginInvoke(Show);
        }
        store.Changed += OnChanged;
        card.Unloaded += (_, _) => store.Changed -= OnChanged;
        Show();
        return card;
    }

    private Border AspectCard(JsonObject item)
    {
        var choices = store.Schema["lists"]!["ASPECTS"]!.AsArray().Select(a => (string)a!)
            .Select(a => (a, a == "auto" ? T("auto") : a, "")).ToList();
        return ChoiceCard(item, choices, store.Get("aspect_ratio"), value => store.Set("aspect_ratio", value));
    }

    private Border OutputCard(JsonObject item) =>
        ChoiceCard(item, OutputChoices(), store.Get("output_res"), value => store.Set("output_res", value));

    /// Auto (the monitor's size) and the aspect ratio's resolutions: the old pages' resolution lists too.
    private List<(string Value, string Label, string Note)> OutputChoices()
    {
        // The aspect ratio's resolutions (the monitor's group for Auto), and the monitor's own size.
        var outputs = store.Schema["lists"]!["OUTPUTS"]!.AsObject();
        var monitor = MonitorSize();
        var aspect = store.Get("aspect_ratio");
        if (outputs[aspect] is null)
        {
            var ratio = monitor.Width / (double)Math.Max(monitor.Height, 1);
            aspect = outputs.Select(pair => pair.Key).OrderBy(name =>
            {
                var parts = name.Split(':');
                var r = double.Parse(parts[0], CultureInfo.InvariantCulture) / double.Parse(parts[1], CultureInfo.InvariantCulture);
                return Math.Abs(Math.Log(ratio / r));
            }).First();
        }
        var sizes = outputs[aspect]!.AsArray().Select(s => (string)s!).ToList();
        var own = $"{monitor.Width}x{monitor.Height}";
        if (monitor.Width > 0 && !sizes.Contains(own))
            sizes.Add(own);
        var choices = new List<(string Value, string Label, string Note)> { ("auto", T("auto_monitor") + (monitor.Width > 0 ? $": {monitor.Width}×{monitor.Height}" : ""), "") };
        choices.AddRange(sizes.OrderBy(s => int.Parse(s.Split('x')[1])).ThenBy(s => int.Parse(s.Split('x')[0]))
            .Select(s => (s, s.Replace('x', '×'), "")));
        return choices;
    }

    private Border FrameGenerationCard(JsonObject item)
    {
        var current = store.Get("frame_generation") switch
        {
            "fsr3" => "fsr3",
            "dlss" => "dlss" + Math.Clamp(int.TryParse(store.GetIni("frame_generation_multiplier", "2"), out var m) ? m : 2, 2, 4),
            _ => "off",
        };
        var choices = new[] { "off", "fsr3", "dlss2", "dlss3", "dlss4" }.Select(v => (v, T("fg_" + v), "")).ToList();
        return ChoiceCard(item, choices, current, value =>
        {
            store.Set("frame_generation", value.StartsWith("dlss") ? "dlss" : value);
            if (value.StartsWith("dlss"))
                store.SetIni("frame_generation_multiplier", value[4..]);
        });
    }

    /// The list DetectGpu got at the start, or (it has none yet, or failed) this row's own probe.
    private Border GpuCard(JsonObject item) =>
        ChoiceCard(item, GpuChoices(), store.Get("gpu_id"), value => store.Set("gpu_id", value),
                   gpus is null ? ProbeGpus() : null);

    private List<(string Value, string Label, string Note)> GpuChoices()
    {
        var best = gpus?.FirstOrDefault(g => g.Best).Name;
        var choices = new List<(string Value, string Label, string Note)> { ("", T("auto_gpu") + (best is null ? "" : $": {best}"), "") };
        if (gpus is not null)
            choices.AddRange(gpus.Select(g => (g.Index, g.Name, "")));
        return choices;
    }

    private async Task<List<(string Value, string Label, string Note)>> ProbeGpus()
    {
        // bb-gpu-capabilities.exe --gpus: index, vendor, type, name, the port's own choice.
        var rows = (await ToolRows("--gpus"))?.Where(parts => parts.Length == 5)
            .Select(parts => (parts[0], parts[3], parts[4] == "1")).ToList();
        if (rows?.Count > 0)
            gpus = rows;
        return GpuChoices();
    }

    /// The lines of bin\bb-gpu-capabilities.exe ARG split at tabs (null when it fails or takes over 10 s).
    private static async Task<List<string[]>?> ToolRows(string argument)
    {
        try
        {
            var tool = Path.Combine(Store.PackageDir, "bin", "bb-gpu-capabilities.exe");
            using var process = Process.Start(new ProcessStartInfo(tool, argument)
            {
                RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            })!;
            // A hung probe is killed with its children (they hold the pipe too), which ends the read.
            using var timeout = new CancellationTokenSource(10000);
            using var kill = timeout.Token.Register(() =>
            {
                try
                {
                    process.Kill(true);
                }
                catch (Exception)
                {
                }
            });
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (!timeout.IsCancellationRequested && process.ExitCode == 0)
                return output.Split('\n').Select(line => line.TrimEnd('\r').Split('\t')).ToList();
        }
        catch (Exception)
        {
        }
        return null;
    }

    private List<string[]>? displays, gamepads;

    private Border DisplayCard(JsonObject item) =>
        ChoiceCard(item, DisplayChoices(), store.Get("display"), value => store.Set("display", value),
                   displays is null ? ProbeDisplays() : null);

    private async Task<List<(string Value, string Label, string Note)>> ProbeDisplays()
    {
        // A failed probe leaves the list unset, so the next visit asks again.
        displays = (await ToolRows("--displays"))?.Where(row => row.Length == 3).ToList() ?? displays;
        return DisplayChoices();
    }

    private List<(string Value, string Label, string Note)> DisplayChoices()
    {
        // --displays: name, WxH, primary. The value is the name, or the number when names repeat.
        var listed = displays ?? new();
        var names = listed.Select(row => row[0]).ToList();
        var choices = new List<(string Value, string Label, string Note)> { ("", T("primary"), "") };
        for (var i = 0; i < listed.Count; ++i)
        {
            var (name, size, primary) = (listed[i][0], listed[i][1], listed[i][2] == "1");
            var value = names.Count(n => n == name) == 1 ? name : (i + 1).ToString(CultureInfo.InvariantCulture);
            choices.Add((value, $"{i + 1}: {name} ({size.Replace('x', '×')})" + (primary ? T("is_primary") : ""), ""));
        }
        var current = store.Get("display");
        if (current.Length > 0 && choices.All(c => c.Value != current))
            choices.Add((current, string.Format(T("not_connected"), current), ""));
        return choices;
    }

    private Border GamepadCard(JsonObject item) =>
        ChoiceCard(item, GamepadChoices(), store.Get("gamepad"), SetGamepad, ProbeGamepads());

    /// The pads connected now (each visit of the page asks: one may be plugged in since).
    private async Task<List<(string Value, string Label, string Note)>> ProbeGamepads()
    {
        gamepads = (await ToolRows("--gamepads"))?.Where(row => row.Length == 2).ToList() ?? gamepads;
        return GamepadChoices();
    }

    /// --gamepads: SDL GUID, name; the saved one marked when it is not connected.
    private List<(string Value, string Label, string Note)> GamepadChoices()
    {
        var choices = new List<(string Value, string Label, string Note)> { ("", T("first_pad"), "") };
        if (gamepads is not null)
            choices.AddRange(gamepads.Select(row => (row[0], row[1], "")));
        var current = store.Get("gamepad");
        if (current.Length > 0 && choices.All(c => c.Value != current))
        {
            var name = store.Settings["gamepad_name"]?.ToString() ?? current;
            choices.Add((current, string.Format(T("not_connected"), name), ""));
        }
        return choices;
    }

    private void SetGamepad(string value)
    {
        store.Settings["gamepad_name"] = gamepads?.FirstOrDefault(row => row[0] == value)?[1] ?? "";
        store.Set("gamepad", value);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public short SpecVersion, DriverVersion, Size, DriverExtra;
        public int Fields, PositionX, PositionY, DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public short LogPixels;
        public int BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
        public int IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string? device, int mode, ref DevMode devMode);

    /// The primary monitor's resolution in pixels (whatever the scaling).
    private static (int Width, int Height) MonitorSize()
    {
        var mode = new DevMode { Size = (short)Marshal.SizeOf<DevMode>() };
        return EnumDisplaySettings(null, -1, ref mode) ? (mode.PelsWidth, mode.PelsHeight) : (0, 0);
    }
}
