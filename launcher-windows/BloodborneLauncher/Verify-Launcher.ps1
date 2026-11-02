param([string]$PreviewDirectory, [string]$PackageCache)
$ErrorActionPreference = 'Stop'
# Keep the verification host outside the repository: Play can only reach
# synthetic fixtures, never the user's runtime, game dump or saved settings.
$verificationRoot = Join-Path ([IO.Path]::GetTempPath()) ('BloodborneLauncher-verify-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $verificationRoot | Out-Null
if (!$PreviewDirectory) { $PreviewDirectory = Join-Path $verificationRoot 'previews' }
$PreviewDirectory = [IO.Path]::GetFullPath($PreviewDirectory)
$projectReference = [Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'BloodborneLauncher.csproj'))
$project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0-windows</TargetFramework><UseWPF>true</UseWPF><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable></PropertyGroup><ItemGroup><ProjectReference Include="'+$projectReference+'"/></ItemGroup></Project>'
Set-Content -Encoding utf8 -LiteralPath (Join-Path $verificationRoot 'LauncherChecks.csproj') -Value $project
$program = @'
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BloodborneLauncher;

static class Program
{
    static MainWindow window = null!;
    static int assertions;
    static T Get<T>(string name) where T : class => (T)window.FindName(name);
    static object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, args);
    static object? Static(string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args);
    static void Check(bool value, string message) { if (!value) throw new Exception(message); assertions++; }
    static string Tag(string name) => ((ComboBoxItem)Get<ComboBox>(name).SelectedItem).Tag?.ToString() ?? "";
    static void Select(string name, string tag) {
        var box = Get<ComboBox>(name);
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().First(x => x.Tag?.ToString() == tag);
    }
    static void ClickNav(string page) => Call("Nav_Click", Get<Button>("Nav" + page), new RoutedEventArgs());
    static void Pump() => window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    static void Render(string file, double width=1500, double height=920, double dpi=96) {
        window.Width=width; window.Height=height;
        window.UpdateLayout(); Pump();
        var element = (FrameworkElement)window.Content;
        element.Measure(new Size(width-2,height-2)); element.Arrange(new Rect(0,0,width-2,height-2)); element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)((width-2)*dpi/96), (int)((height-2)*dpi/96), dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(file); encoder.Save(output);
    }
    static string[] IniLines(string root) => File.ReadAllLines(Path.Combine(root,"bbport.ini"));
    [STAThread] static int Main(string[] args) {
        try { Run(args[0]); return 0; } catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static void Run(string output) {
        Directory.CreateDirectory(output);
        var root = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        // settings.json goes to %APPDATA%\bbport-launcher as bbport-run.exe reads it: a fixture APPDATA here.
        string appData = Path.Combine(root,"appdata");
        if (Directory.Exists(appData)) Directory.Delete(appData,true);
        foreach (string key in Environment.GetEnvironmentVariables().Keys)
            if(key.StartsWith("BB_")) Environment.SetEnvironmentVariable(key,null);
        Environment.SetEnvironmentVariable("APPDATA",appData);
        string settingsFile = Path.Combine(appData,"bbport-launcher","settings.json");
        string game = Path.Combine(root, "CUSA03173");
        Directory.CreateDirectory(Path.Combine(game,"sce_sys"));
        if (File.Exists(Path.Combine(game,"eboot.bin"))) File.Delete(Path.Combine(game,"eboot.bin"));
        File.WriteAllText(Path.Combine(game,"sce_sys","param.sfo"),"01.09");
        foreach(var stale in new[]{"bin","fsr4_shaders","fsr4_411","logs"}) { var d=Path.Combine(root,stale); if(Directory.Exists(d)) Directory.Delete(d,true); }
        foreach(var stale in new[]{"launch-args.txt","launch-cwd.txt","launch-count.txt","VERSION"}) { var f=Path.Combine(root,stale); if(File.Exists(f)) File.Delete(f); }
        // Stands in for bbport-run.exe: records how Play starts it.
        string fakeRunner = Path.Combine(root,"fake-runner.bat");
        File.WriteAllText(fakeRunner,"@echo off\r\necho %* > \""+root+"\\launch-args.txt\"\r\ncd > \""+root+"\\launch-cwd.txt\"\r\necho session>>\""+root+"\\launch-count.txt\"\r\necho runner output line\r\nping 127.0.0.1 -n 3 > nul\r\n");
        File.WriteAllText(Path.Combine(root,"bbport.ini"),"# keep this comment\nunrelated_setting=keep\nupscaler=off\n");
        // A damaged settings.json falls back to its .bak; a saved folder elsewhere is replaced by the one next to the launcher.
        Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!);
        File.WriteAllText(settingsFile,"{damaged");
        File.WriteAllText(settingsFile+".bak","{\"game_dir\":\"C:/stale\",\"thd_online_id\":\"bak-fixture\"}");
        var app = new App();
        app.InitializeComponent();

        window = new MainWindow { ShowInTaskbar=false, ShowActivated=false, WindowStartupLocation=WindowStartupLocation.Manual, Left=-20000, Top=-20000 };
        window.Show(); Pump();
        Check(Store.PackageDir==root,"Package root without bbport-run.exe is the launcher's folder");
        Check(Store.ConfigFile==settingsFile,"settings.json under %APPDATA% as bbport-run.exe reads it");
        Check(!Get<Button>("PlayButton").IsEnabled, "Play must be disabled without a dump");
        Check(Get<TextBox>("PathBox").Text==game,"Default game folder next to the launcher");
        Check(Get<TextBlock>("RuntimeStatus").Text=="bbport-run.exe not found","Missing runtime status");
        Check(Get<TextBlock>("DlssStatus").Text=="Not in this build","Missing DLSS bridge status");
        Check(Get<TextBlock>("Fsr4Status").Text=="Assets not installed","Missing FSR4 status");
        Check(Get<Image>("HeroImage").Source is BitmapSource, "Embedded hero not loaded");
        Check(Tag("UpscalerCombo")=="off" && Tag("QuickUpscaler")=="off","bbport.ini values shown at start");
        foreach(var gone in new[]{"CameraFovCombo","RebirthCheck","ChangeAppearanceCheck","GraphicsAoCombo","DlssModelCombo","LiveResolutionCombo","WidescreenCheck","GameAaCheck","KeyBindingsGrid","PcControlsCheck","OnlineHostBox","DeveloperModeCheck"})
            Check(window.FindName(gone)==null,"Old control without a 0.7 setting is hidden: "+gone);
        Render(Path.Combine(output,"launcher-play-unconfigured.png"));

        File.WriteAllText(Path.Combine(game,"eboot.bin"),"launcher test fixture");
        Call("RefreshStatus");
        Check(Get<Button>("PlayButton").IsEnabled,"Valid fixture should enable Play");
        Check(Get<TextBlock>("GameVersionStatus").Text.Contains("1.09"),"1.09 detection");
        File.Move(Path.Combine(game,"eboot.bin"),Path.Combine(game,"eboot.hidden"));
        Call("RefreshStatus"); Check(!Get<Button>("PlayButton").IsEnabled,"Missing eboot disables Play");
        File.Move(Path.Combine(game,"eboot.hidden"),Path.Combine(game,"eboot.bin"));
        Directory.Move(Path.Combine(game,"sce_sys"),Path.Combine(game,"sce_hidden"));
        Call("RefreshStatus"); Check(!Get<Button>("PlayButton").IsEnabled,"Missing sce_sys disables Play");
        Directory.Move(Path.Combine(game,"sce_hidden"),Path.Combine(game,"sce_sys"));
        foreach(var version in new[]{"01.08","01.00","unknown"}) {
            File.WriteAllText(Path.Combine(game,"sce_sys","param.sfo"),version); Call("RefreshStatus");
            Check(Get<Button>("PlayButton").IsEnabled,"Preserve dump validation for "+version);
            Check(!Get<TextBlock>("GameVersionStatus").Text.Contains("1.09 ✓"),"No false 1.09 for "+version);
        }
        File.WriteAllText(Path.Combine(game,"sce_sys","param.sfo"),"01.09");
        // The 0.7 package's files: the runner, the DLSS bridge and DLL, the FSR 4 asset folders.
        Environment.SetEnvironmentVariable("BB_LAUNCHER_RUNNER",fakeRunner);
        Directory.CreateDirectory(Path.Combine(root,"bin"));
        File.WriteAllText(Path.Combine(root,"bin","bbport_dlss.dll"),"status fixture only"); Call("RefreshStatus");
        Check(Get<TextBlock>("DlssStatus").Text=="Model DLL not installed","DLSS bridge without the NVIDIA DLL");
        File.WriteAllText(Path.Combine(root,"bin","nvngx_dlss.dll"),"status fixture only");
        Directory.CreateDirectory(Path.Combine(root,"fsr4_shaders")); Call("RefreshStatus");
        Check(Get<TextBlock>("RuntimeStatus").Text=="Ready","Runtime existence detection");
        Check(Get<TextBlock>("DlssStatus").Text=="Available","DLSS existence detection");
        Check(Get<TextBlock>("Fsr4Status").Text=="v07 available","FSR4 legacy detection");
        Directory.CreateDirectory(Path.Combine(root,"fsr4_411")); Call("RefreshStatus");
        Check(Get<TextBlock>("Fsr4Status").Text=="4.1.1 available","FSR4 4.1.1 detection");
        Directory.Delete(Path.Combine(root,"bin"),true); Directory.Delete(Path.Combine(root,"fsr4_shaders")); Directory.Delete(Path.Combine(root,"fsr4_411"));

        // Quick settings and the detailed pages show one store value.
        foreach(var pair in new[] { ("UpscalerCombo","QuickUpscaler"),("PresetCombo","QuickPreset"),("FpsCombo","QuickFps"),("ResolutionCombo","QuickResolution") }) {
            var detailed=Get<ComboBox>(pair.Item1); var quick=Get<ComboBox>(pair.Item2);
            Check(detailed.Items.Count==quick.Items.Count && detailed.Items.Count>=4,pair.Item1+" lists the schema's choices");
            for(int i=0;i<detailed.Items.Count;i++) { detailed.SelectedIndex=i; Check(Tag(pair.Item2)==Tag(pair.Item1),pair.Item1+" to quick "+i); }
            for(int i=quick.Items.Count-1;i>=0;i--) { quick.SelectedIndex=i; Check(Tag(pair.Item1)==Tag(pair.Item2),pair.Item2+" to detailed "+i); }
        }
        Check(Get<ComboBox>("UpscalerCombo").Items.Cast<ComboBoxItem>().Select(i=>(string)i.Tag).SequenceEqual(new[]{"fsr4","fsr411","fsr3","fsr1","dlss","taa","off"}),"0.7's upscalers");
        Get<CheckBox>("FullscreenCheck").IsChecked=false; Check(Tag("QuickDisplay")=="0","Display sync to quick");
        Select("QuickDisplay","1"); Check(Get<CheckBox>("FullscreenCheck").IsChecked==true,"Display sync from quick");

        Select("UpscalerCombo","fsr411"); Select("PresetCombo","3"); Select("FpsCombo","90"); Select("LanguageCombo","3");
        Select("PresentCombo","Immediate"); Select("ModelLodCombo","-2"); Select("ResolutionCombo","2560x1440");
        foreach(var name in new[]{"SharpenCheck","ShowFpsCheck","SsrCheck","SkipIntroCheck","MouseInvertYCheck"}) Get<CheckBox>(name).IsChecked=true;
        foreach(var name in new[]{"ChromaticCheck","DofCheck","MotionBlurCheck","SsaoCheck","ShadowsCheck"}) Get<CheckBox>(name).IsChecked=false;
        Get<Slider>("MouseSensitivitySlider").Value=2.5;
        Call("Save_Click", Get<Button>("PlayButton"), new RoutedEventArgs());
        var ini=IniLines(root);
        foreach(var expected in new[]{"# keep this comment","unrelated_setting=keep","upscaler=fsr411","preset=3","output_res=2560x1440","model_lod=-2",
                                      "effect_chromatic_aberration=0","effect_dof=0","effect_motion_blur=0","effect_ssao=0","effect_dynamic_shadows=0","effect_ssr=1","skip_intro=1","mouse_invert_y=1","mouse_sensitivity=2.50"})
            Check(ini.Contains(expected),"bbport.ini "+expected);
        Check(ini.Count(l=>l.StartsWith("upscaler="))==1,"bbport.ini keys are replaced, not repeated");
        var json=JsonNode.Parse(File.ReadAllText(settingsFile))!.AsObject();
        Check((string?)json["game_dir"]==game,"settings.json game_dir");
        Check((string?)json["thd_online_id"]=="bak-fixture","Damaged settings.json loads the .bak");
        Check(File.Exists(settingsFile+".bak") && File.Exists(Path.Combine(root,"bbport.ini.bak")) && !File.Exists(settingsFile+".tmp"),"Safe saves keep a .bak");
        Check((string?)json["fps_mode"]=="90" && (string?)json["language"]=="3" && (string?)json["present_mode"]=="Immediate","settings.json fps_mode, language, present_mode");
        Check(json["fullscreen"]!.GetValue<bool>(),"settings.json fullscreen");
        Check(json["upscaler"]==null,"Game keys stay out of settings.json");
        Check((string?)json["ui_language"]=="en","English in-game menu by default (bbport-run.exe gives Russian when unset)");
        Check(Get<TextBlock>("FooterFps").Text.Contains("90 FPS"),"Footer frame rate");

        // The in-game menu edits bbport.ini: a reload shows its values.
        File.WriteAllLines(Path.Combine(root,"bbport.ini"),IniLines(root).Select(l=>l=="upscaler=fsr411"?"upscaler=fsr3":l));
        typeof(MainWindow).GetField("store",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!.GetType().GetMethod("Load")!
            .Invoke(typeof(MainWindow).GetField("store",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window),null);
        Call("RefreshStatus");
        Check(Tag("UpscalerCombo")=="fsr3" && Tag("QuickUpscaler")=="fsr3","bbport.ini changes reach the pages");

        Call("Play_Click", Get<Button>("PlayButton"),new RoutedEventArgs());
        Call("Play_Click", Get<Button>("PlayButton"),new RoutedEventArgs());
        var argsPath=Path.Combine(root,"launch-args.txt");
        for(int i=0;i<100 && !File.Exists(argsPath);i++) Thread.Sleep(50);
        Thread.Sleep(300);
        Check(File.ReadAllLines(Path.Combine(root,"launch-count.txt")).Length==1,"Play cannot start duplicate sessions");
        Check(File.ReadAllText(argsPath).Trim()=="--play","bbport-run.exe --play");
        Check(File.ReadAllText(Path.Combine(root,"launch-cwd.txt")).Trim().TrimEnd('\\')==root,"Runner starts in the package folder");
        for(int i=0;i<100 && window.Visibility!=Visibility.Visible;i++) { Thread.Sleep(50); Pump(); }
        Check(window.Visibility==Visibility.Visible,"Window returns after the game");
        var logs=Directory.GetFiles(Path.Combine(root,"logs"),"launcher-*.log");
        Check(logs.Length==1 && File.ReadAllText(logs[0]).Contains("runner output line"),"Runner output in the session log");
        Render(Path.Combine(output,"launcher-play.png"));

        var extras = new Dictionary<string,string>{["General"]="GeneralExtra",["Graphics"]="GraphicsExtra",["Performance"]="PerformanceExtra",["Controller"]="ControllerExtra",["Online"]="OnlineItems",["Mods"]="ModsItems",["Advanced"]="AdvancedItems",["Trophies"]="Items"};
        foreach(var page in new[]{"General","Graphics","Performance","Controller","Online","Mods","Advanced","Trophies"}) {
            ClickNav(page); Pump();
            Check(Get<FrameworkElement>(page+"Page").Visibility==Visibility.Visible,"Navigation "+page);
            Check(Get<FrameworkElement>("HomePage").Visibility==Visibility.Collapsed,"Home hidden "+page);
            Check(Get<Panel>(extras[page]).Children.Count>0,"0.7 settings on "+page);
            Render(Path.Combine(output,"launcher-"+page.ToLowerInvariant()+".png"));
            ((ScrollViewer)Get<FrameworkElement>(page+"Page")).ScrollToBottom(); Pump();
            Render(Path.Combine(output,"launcher-"+page.ToLowerInvariant()+"-bottom.png"));
            ((ScrollViewer)Get<FrameworkElement>(page+"Page")).ScrollToTop(); Pump();
        }
        // Generated rows leave out what an old control shows, and include the new settings.
        ClickNav("Graphics");
        string GeneratedText(string panel) => string.Join("\n", Descendants(Get<Panel>(panel)).OfType<TextBlock>().Select(t=>t.Text));
        string graphics=GeneratedText("GraphicsExtra");
        foreach(var title in new[]{"Frame generation","Anti-aliasing","Texture quality"}) Check(graphics.Contains(title),"New 0.7 setting on Graphics: "+title);
        ClickNav("Performance");
        Check(GeneratedText("PerformanceExtra").Contains("FPS limit"),"FPS limit on Performance");
        // A generated row and an old control follow each other through the store.
        var storeObj=typeof(MainWindow).GetField("store",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        storeObj.GetType().GetMethod("Set")!.Invoke(storeObj,new object[]{"upscaler","dlss"});
        Check(Tag("UpscalerCombo")=="dlss" && Tag("QuickUpscaler")=="dlss","Store changes reach the old controls");

        ClickNav("Graphics");
        var upscaler = Get<ComboBox>("UpscalerCombo");
        window.Activate(); Pump();
        upscaler.IsDropDownOpen = true; Pump(); window.UpdateLayout();
        var popup = (System.Windows.Controls.Primitives.Popup)upscaler.Template.FindName("PART_Popup",upscaler);
        if (!popup.IsOpen) { popup.StaysOpen = true; popup.IsOpen = true; Pump(); window.UpdateLayout(); }
        Check(popup.IsOpen && popup.Child != null,"Styled dropdown opens");
        var menu = (FrameworkElement)popup.Child!;
        var menuBitmap = new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth),(int)Math.Ceiling(menu.ActualHeight),96,96,PixelFormats.Pbgra32);
        menuBitmap.Render(menu);
        var menuEncoder = new PngBitmapEncoder(); menuEncoder.Frames.Add(BitmapFrame.Create(menuBitmap));
        using(var menuOutput=File.Create(Path.Combine(output,"launcher-dropdown.png"))) menuEncoder.Save(menuOutput);
        upscaler.IsDropDownOpen=false; popup.IsOpen=false; Pump();
        ClickNav("Home"); Render(Path.Combine(output,"launcher-compact.png"),1000,680);
        Get<ScrollViewer>("HomePage").ScrollToBottom(); Pump();
        Render(Path.Combine(output,"launcher-compact-scrolled.png"),1000,680);
        Check(Get<ScrollViewer>("HomePage").VerticalOffset>0,"Compact home scrolls to quick controls");
        Get<ScrollViewer>("HomePage").ScrollToTop(); Pump();
        ClickNav("Graphics"); Render(Path.Combine(output,"launcher-graphics-compact.png"),1000,680);
        ClickNav("Home"); Render(Path.Combine(output,"launcher-150-percent.png"),1100,720,144);
        window.MinWidth=900; window.MinHeight=520;
        Render(Path.Combine(output,"launcher-200-percent.png"),900,520,192);
        Get<ScrollViewer>("SidebarNavigation").ScrollToBottom(); Pump();
        Check(Get<ScrollViewer>("SidebarNavigation").VerticalOffset>0,"Short-screen sidebar remains scrollable");

        // Updater: VERSION is the installed version; only BloodborneLauncher.exe beside bbport-run.exe updates.
        Check(Get<Button>("UpdateButton").Visibility==Visibility.Collapsed,"Updater stays offline outside a portable install");
        Check((bool)typeof(MainWindow).GetProperty("IsPortableInstall",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)! == false,"Test host is not a portable install");
        // Trophies without a decrypted TROP.XML (no trophy key): the fork's own list names all 40.
        string trophyUser = Path.Combine(Store.DataDir, "user", "trophy", "NPWR05818_00");
        Directory.CreateDirectory(trophyUser);
        File.WriteAllText(Path.Combine(trophyUser, "unlocked.txt"), "21\n");
        var loaded = Call("LoadTrophies")!;
        var trophyList = (System.Collections.IList)loaded.GetType().GetField("Item1")!.GetValue(loaded)!;
        object cleric = trophyList[21]!;
        object? Prop(string name) => cleric.GetType().GetProperty(name)!.GetValue(cleric);
        Check(trophyList.Count==40 && (bool)loaded.GetType().GetField("Item2")!.GetValue(loaded)!, "Trophies named without a key: all 40");
        Check((string?)Prop("Name")=="Cleric Beast" && (bool)Prop("Unlocked")! && Equals(Prop("Grade"), 'B'), "Built-in list: Cleric Beast, bronze, earned");
        Directory.Delete(Path.Combine(Store.DataDir, "user", "trophy"), true);
        Check((string?)Static("InstalledVersion")=="","No VERSION: unknown install");
        File.WriteAllText(Path.Combine(root,"VERSION"),"0.7.1\n");
        Check((string?)Static("InstalledVersion")=="0.7.1","VERSION is the installed version");
        File.Delete(Path.Combine(root,"VERSION"));
        // Mods as scripts/mods.py content_root takes them: a modified eboot.bin or patch XML files alone make one.
        string modsRoot = Path.Combine(root, "mod-fixtures");
        if (Directory.Exists(modsRoot)) Directory.Delete(modsRoot, true);
        Directory.CreateDirectory(Path.Combine(modsRoot, "Eboot")); File.WriteAllText(Path.Combine(modsRoot, "Eboot", "eboot.bin"), "modified");
        Directory.CreateDirectory(Path.Combine(modsRoot, "Xml", "patches")); File.WriteAllText(Path.Combine(modsRoot, "Xml", "patches", "fov.xml"), "<Patch/>");
        Directory.CreateDirectory(Path.Combine(modsRoot, "Empty"));
        Check((bool)Static("IsMod", Path.Combine(modsRoot, "Eboot"))! && (bool)Static("IsMod", Path.Combine(modsRoot, "Xml"))!
              && !(bool)Static("IsMod", Path.Combine(modsRoot, "Empty"))!, "eboot.bin and patch XML mods are recognised, an empty folder is not");
        string sums="aa11  other.zip\r\nBB22 *Pkg-Windows-x64.zip\n";
        Check((string?)Static("ExpectedSha256",sums,"Pkg-Windows-x64.zip")=="BB22","Checksum line found (binary-mode '*', CRLF)");
        Check(Static("ExpectedSha256",sums,"missing.zip")==null,"Missing checksum refuses the update");
        foreach(var (latest,installed,want) in new[]{("v0.10","v0.9",true),("v0.9","v0.10",false),("v0.8","v0.8",false),("v0.8","",true),("v0.7.2","0.7.1",true),("v0.7.1","0.7.1",false),("garbage","v0.8",false),("v1","v0.9",true)})
            Check((bool)Static("IsNewerRelease",latest,installed)! == want,$"IsNewerRelease({latest},{installed})=={want}");
        string zipDir=Path.Combine(root,"zip-fixtures");
        if (Directory.Exists(zipDir)) Directory.Delete(zipDir,true);
        string Extracted(string name,params string[] entries){
            string zip=Path.Combine(zipDir,name+".zip"), dest=Path.Combine(zipDir,name,"root");
            Directory.CreateDirectory(dest);
            using(var a=System.IO.Compression.ZipFile.Open(zip,System.IO.Compression.ZipArchiveMode.Create))
                foreach(var entry in entries){ using var w=new StreamWriter(a.CreateEntry(entry).Open()); w.Write(entry); }
            using(var a=System.IO.Compression.ZipFile.OpenRead(zip)) Static("ExtractPackage",a,dest);
            return dest;
        }
        string nested=Extracted("nested","Bloodborne-PC/BloodborneLauncher.exe","Bloodborne-PC/VERSION");
        Check(File.Exists(Path.Combine(nested,"BloodborneLauncher.exe"))&&File.ReadAllText(Path.Combine(nested,"VERSION"))=="Bloodborne-PC/VERSION"&&!Directory.Exists(Path.Combine(nested,"Bloodborne-PC")),"Package's Bloodborne-PC/ folder is dropped");
        string flat=Extracted("flat","a.txt","out/b.txt");
        Check(File.Exists(Path.Combine(flat,"a.txt"))&&File.Exists(Path.Combine(flat,"out","b.txt")),"Flat package extracts unchanged");
        try { Extracted("evil","../evil.txt"); Check(false,"Zip-slip entry is rejected"); }
        catch(TargetInvocationException ex) when (ex.InnerException is IOException) { Check(!File.Exists(Path.Combine(zipDir,"evil","evil.txt")),"Zip-slip entry is rejected and writes nothing outside root"); }
        Console.WriteLine($"PASS: {assertions} assertions; 0.7 settings files, navigation, quick options, bbport-run.exe launch, updater and artwork.");
        window.Close(); app.Shutdown();
    }

    static IEnumerable<DependencyObject> Descendants(DependencyObject parent) {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>()) {
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }
}



'@
Set-Content -Encoding utf8 -LiteralPath (Join-Path $verificationRoot 'Program.cs') -Value $program
$arguments = @('run','--project',(Join-Path $verificationRoot 'LauncherChecks.csproj'),'--configuration','Release')
if ($PackageCache) { $arguments += "--property:RestorePackagesPath=$([IO.Path]::GetFullPath($PackageCache))" }
& dotnet @arguments -- $PreviewDirectory
if ($LASTEXITCODE -ne 0) { throw 'Launcher verification failed.' }
Write-Host "Preview images: $PreviewDirectory"
Write-Host "Isolated test fixtures: $verificationRoot"
