using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TechLoungeOS;

internal static class Program
{
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint handle, int attribute, out int value, int size);
    [STAThread]
    public static void Main(string[] args)
    {
        if(args is ["--release-video-frames",var videoOutputWithImage,var liveCatalogueWithImage,var importedFile])
        {
            var videoApp=new App(false);videoApp.InitializeComponent();ReleaseVideoFrames.Run(videoOutputWithImage,liveCatalogueWithImage,importedFile);videoApp.Shutdown();return;
        }
        if(args is ["--release-video-frames",var videoOutput,var liveCatalogue])
        {
            var videoApp=new App(false);videoApp.InitializeComponent();ReleaseVideoFrames.Run(videoOutput,liveCatalogue);videoApp.Shutdown();return;
        }
        if(args is ["--app-search-native",var nativeSearchFolder])
        {
            var nativeSearchApp=new App(false);nativeSearchApp.InitializeComponent();AppSuggestionChecks.RunNative(nativeSearchFolder);nativeSearchApp.Shutdown();return;
        }
        if(args is ["--app-suggestion-checks"])
        {
            var suggestionsApp=new App(false);suggestionsApp.InitializeComponent();AppSuggestionChecks.Run();suggestionsApp.Shutdown();return;
        }
        if(args is ["--usability-checks",var usabilityFolder])
        {
            var usabilityApp=new App(false);usabilityApp.InitializeComponent();UsabilityChecks.Run(usabilityFolder);usabilityApp.Shutdown();return;
        }
        if(args is ["--output-flow-checks"])
        {
            var outputApp=new App(false);outputApp.InitializeComponent();OutputFlowChecks.Run();outputApp.Shutdown();return;
        }
        if(args is ["--app-update-feedback-checks",var feedbackFolder])
        {
            var feedbackApp=new App(false);feedbackApp.InitializeComponent();
            AppUpdateFeedbackChecks.Run(feedbackFolder);feedbackApp.Shutdown();return;
        }
        if(args is ["--iso-verification-native"]){Console.WriteLine("ISO_REPORT "+IsoExportVerificationCheck.Run(false).GetAwaiter().GetResult());return;}
        if(args is ["--iso-verification-accepted"]){Console.WriteLine("ISO_REPORT "+IsoExportVerificationCheck.Run(true).GetAwaiter().GetResult());return;}
        if(args is ["--pending-update-checks1296"])
        {
            new ProcessRunner(_=>{}).PowerShell("if((Get-CimInstance Win32_ComputerSystem).Manufacturer -ne 'QEMU' -or @(Get-Disk|Where-Object {([string]$_.SerialNumber).Trim() -eq 'TLOS-FRESH1263' -and $_.IsBoot}).Count -ne 1){throw 'Owned fresh1263 required'}").GetAwaiter().GetResult();
            var pendingApp=new App(false);pendingApp.InitializeComponent();
            var pendingWindow=new MainWindow(false);
            try{PendingUpdateChecks.Run(pendingWindow);}
            finally{pendingWindow.Close();pendingApp.Shutdown();}
            return;
        }
        if (args is ["--wallpaper-revision1250"]) { WallpaperRevision.Run().GetAwaiter().GetResult(); return; }
        if (args is ["--wallpaper-revision1250-resume"]) { WallpaperRevision.Run(true).GetAwaiter().GetResult(); return; }
        if (args is ["--failed-update-download1266"]) { UpdateDownloadCheck.Run().GetAwaiter().GetResult(); return; }
        if (args is ["--revision-usb1255"]) { RevisionUsb.Run().GetAwaiter().GetResult(); return; }
        if (args is ["--export-readback1257"]) { Export1257.Run().GetAwaiter().GetResult(); return; }
        if (args is ["--finish-export1258"]) { Export1257.Run(true).GetAwaiter().GetResult(); return; }
        if (args is ["--package-checks"]) { PackageChecks.Run(); return; }
        if (args is ["--copy-checks"])
        {
            var copyApp = new App(false); copyApp.InitializeComponent();
            CopyChecks.Run(new MainWindow(false)); return;
        }
        if(args is ["--windows-image-update-ui-checks"])
        {
            var updateApp=new App(false);updateApp.InitializeComponent();
            WindowsImageUpdateUiChecks.Run();CopyChecks.Run(new MainWindow(false));return;
        }
        if (args is ["--live-acceptance", var liveConfig]) { LiveAcceptance.Run(liveConfig).GetAwaiter().GetResult(); return; }
        if (args is ["--native-build", var config]) { NativeBuildCheck.Run(config); return; }
        if (args is ["--update-close-checks", var closeOutput]) { UpdateCloseChecks.Run(closeOutput); return; }
        if(args is ["--update-interruption-native"]){UpdateInterruptionCheck.Run();return;}
        if(args is ["--update-interrupt-child"]){UpdateInterruptionCheck.Interrupt();return;}
        if (args.Length == 1 && args[0] == "--icon-checks") { ArtworkChecks.Run(); return; }
        if(args is ["--app-selection-fixture",var fixturePath])
        {
            const string state="$state = Join-Path $env:LOCALAPPDATA 'TechLoungeOS'";
            var script=Provisioning.Script(TechLoungeOS.RecommendedApps.PresetIds,false,false);
            if(script.Split(state,StringSplitOptions.None).Length!=2)throw new IOException("Unexpected provisioning state declaration.");
            const string guard="if((Get-CimInstance Win32_ComputerSystem -ErrorAction Stop).Manufacturer -ne 'QEMU' -or @(Get-Disk -ErrorAction Stop | Where-Object {([string]$_.SerialNumber).Trim() -eq 'TLOS-FRESH832' -and $_.IsBoot}).Count -ne 1){throw 'Owned fresh832 installation required'}\n";
            File.WriteAllText(fixturePath,guard+script.Replace(state,"$state = Join-Path $env:LOCALAPPDATA 'TechLoungeOS-AppSelectionTest'",StringComparison.Ordinal));
            var fixtureFolder=Path.GetDirectoryName(Path.GetFullPath(fixturePath))!;
            File.WriteAllText(Path.Combine(fixtureFolder,"legacy-network.ps1"),guard+LegacyNetwork.Script);
            File.WriteAllText(Path.Combine(fixtureFolder,"proton-quiet.ps1"),guard+Provisioning.Script(["Proton.ProtonVPN"],false,false).Replace(state,"$state = Join-Path $env:LOCALAPPDATA 'TechLoungeOS-ProtonQuietTest'",StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(fixtureFolder,"chrome-quiet.ps1"),guard+Provisioning.Script(["Google.Chrome"],false,false).Replace(state,"$state = Join-Path $env:LOCALAPPDATA 'TechLoungeOS-ChromeQuietTest'",StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(fixtureFolder,"chatgpt-shortcut.ps1"),guard+Provisioning.Script([RecommendedApps.ChatGPT],false,false).Replace(state,"$state = Join-Path $env:LOCALAPPDATA 'TechLoungeOS-ChatGPTShortcutTest'",StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(fixtureFolder,"first-login-current.ps1"),guard+Provisioning.Script(["7zip.7zip","SumatraPDF.SumatraPDF",RecommendedApps.Discord],false,false,runtimePack:true,storeXbox:true,office:true,recommended:true,nvidia:true,amdIntelGraphics:true,edge:true,desktopDefaults:true,wallpaperPreset:"waterfall.ice",wallpaperLibrary:true,windowsRecovery:true,currentTweaks:true,legacyNetwork:true));
            File.WriteAllText(Path.Combine(fixtureFolder,"Office-worker.ps1"),OfficePack.Script);
            File.WriteAllText(Path.Combine(fixtureFolder,"tweaks-install.ps1"),guard+Provisioning.Script([],false,false,currentTweaks:true).Replace(state,"$state = Join-Path $env:LOCALAPPDATA 'TechLoungeOS-TweaksTest'",StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(fixtureFolder,"desktop-input.ps1"),guard+Provisioning.Script([],false,false,desktopDefaults:true).Replace(state,"$state = Join-Path $env:LOCALAPPDATA 'TechLoungeOS-DesktopTest'",StringComparison.Ordinal));
            Console.WriteLine("Production eight-app recipes written with isolated test receipts and a fresh832-only guard. No installer executed.");return;
        }
        if(!LiveAcceptance.OwnedBuilder("QEMU",["TLOS-SCRATCH114"]) || !LiveAcceptance.OwnedBuilder("qemu",["TLOS-SCRATCH114     _00000001."])
            || LiveAcceptance.OwnedBuilder("Other",["TLOS-SCRATCH114"]) || LiveAcceptance.OwnedBuilder("QEMU",["TLOS-SCRATCH114-other"])
            || LiveAcceptance.OwnedBuilder("QEMU",["TLOS-SCRATCH114","TLOS-SCRATCH114"]) || !LiveAcceptance.OwnedUsb("TLOS-USB-347")
            || LiveAcceptance.OwnedUsb("TLOS-USB-arbitrary"))throw new Exception("Native validation identity boundary failed.");
        Console.WriteLine("Owned VM identity accepts the observed NVMe namespace suffix; duplicate identities, unknown hosts and arbitrary USB serials are rejected.");
        var app = new App(false); app.InitializeComponent();
        var window = new MainWindow(false);
        CopyChecks.Run(window);
        var handle = new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
        if (DwmGetWindowAttribute(handle, 20, out var darkCaption, sizeof(int)) != 0 || darkCaption != (SystemParameters.HighContrast ? 0 : 1))
            throw new Exception("Native Windows caption dark-mode preference was not applied.");
        Console.WriteLine("Native DWM dark-caption flag verified on a hidden app window; caption pixels not captured.");
        var nativeTypeface = new Typeface((FontFamily)app.FindResource("BodyFont"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        if (!nativeTypeface.TryGetGlyphTypeface(out var nativeGlyph) || !nativeGlyph.FontUri.OriginalString.EndsWith("seguiVAR.ttf", StringComparison.OrdinalIgnoreCase))
            throw new Exception("Windows 11 Segoe UI Variable was not actually loaded.");
        Console.WriteLine($"Windows 11 UI typography resolved from {nativeGlyph.FontUri}.");

        var folder = args.Length >= 1 ? args[0] : Path.Combine(AppContext.BaseDirectory, "renders");
        Directory.CreateDirectory(folder);
        ExportReadback.CheckRejectedInputs(folder);
        PostAppReadback.CheckFixtures(folder);
        void Step(int index) => typeof(MainWindow).GetMethod("ShowStep", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [index]);
        double Luminance(string key)
        {
            var c = ((SolidColorBrush)app.FindResource(key)).Color;
            double Linear(byte b) { var v = b / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
            return .2126 * Linear(c.R) + .7152 * Linear(c.G) + .0722 * Linear(c.B);
        }
        foreach (var pair in new[] { ("Ink", "Surface"), ("Muted", "Surface"), ("Muted", "Canvas"), ("AccentText", "Accent"), ("Accent", "Tint") })
        {
            var a = Luminance(pair.Item1); var b = Luminance(pair.Item2);
            var ratio = (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
            if (ratio < 4.5) throw new Exception("Insufficient text contrast: " + pair);
            Console.WriteLine($"Contrast {pair.Item1}/{pair.Item2}: {ratio:F2}:1.");
        }
        var content = (FrameworkElement)window.Content;
        IntegrationDefaultsChecks.Run();
        WindowsImageUpdateUiChecks.Run();
        ActivityPowerChecks.Run(window);
        PendingUpdateChecks.Run(window);
        var pages = new[] { "SourcePage", "ComponentsPage", "AppsPage", "PersonalisationPage", "ExportPage" };
        var region=(ComboBox)window.FindName("SetupRegionChoice");
        var keyboard=(ComboBox)window.FindName("SetupKeyboardChoice");
        if(window.FindName("AnimatedPe")!=null || !region.IsEnabled || !keyboard.IsEnabled)throw new Exception("Mandatory WinPE has no opt-out; regional choices remain available.");
        region.SelectedItem=region.Items.Cast<SetupRegional.Choice>().Single(c=>c.Id=="en-GB");
        keyboard.SelectedItem=keyboard.Items.Cast<SetupRegional.Choice>().Single(c=>c.Id=="00000809");
        if(!region.IsEnabled || !keyboard.IsEnabled || (region.SelectedItem as SetupRegional.Choice)?.Id!="en-GB" || (keyboard.SelectedItem as SetupRegional.Choice)?.Id!="00000809")
            throw new Exception("Builder regional selectors did not retain the selected UK values.");
        region.SelectedIndex=0;keyboard.SelectedIndex=0;
        Console.WriteLine("Mandatory WinPE has no opt-out; regional selectors retain explicit choices and allow returning to ISO defaults.");
        void Layout(Size size)
        {
            window.Width = size.Width; window.Height = size.Height;
            content.Measure(size); content.Arrange(new Rect(size)); content.UpdateLayout();
            Pump(180); // Allow the framework's expander transitions to settle before measuring/rendering.
            content.Measure(size); content.Arrange(new Rect(size)); content.UpdateLayout();
            // Finish the short opacity transition for deterministic static previews.
            foreach (var name in pages)
            {
                var page = (FrameworkElement)window.FindName(name); page.BeginAnimation(UIElement.OpacityProperty, null);
                if (page.RenderTransform is TranslateTransform offset) offset.BeginAnimation(TranslateTransform.YProperty, null);
            }
        }
        void Snapshot(Size size, string name)
        {
            Layout(size);
            var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(folder, name)); encoder.Save(stream);
        }
        void Visible(FrameworkElement item, Size size)
        {
            var bounds = new Rect(item.TranslatePoint(new Point(), content), item.RenderSize);
            if (bounds.Left < 0 || bounds.Top < 0 || bounds.Right > size.Width || bounds.Bottom > size.Height || bounds.Height <= 0)
                throw new Exception(item.Name + " falls outside viewport.");
        }
        foreach (var size in new[] { new Size(1160, 860), new Size(800, 650), new Size(720, 500) })
        {
            for (var index = 0; index < pages.Length; index++)
            {
                Step(index); Layout(size);
                if (pages.Count(n => ((FrameworkElement)window.FindName(n)).Visibility == Visibility.Visible) != 1) throw new Exception("Step navigation must show exactly one page.");
                var build = (Button)window.FindName("BuildButton");
                var next=(Button)window.FindName("NextStepButton");
                if(build.Visibility!=(index==4?Visibility.Visible:Visibility.Collapsed) || next.Visibility!=(index<4?Visibility.Visible:Visibility.Collapsed))
                    throw new Exception("Only the final page may offer Build ISO; earlier pages must offer Next.");
                Visible(index==4?build:next,size);
                if(window.FindName("UsbButton")!=null || window.FindName("ExportUsbButton")!=null)
                    throw new Exception("USB creation must have one final action, without duplicate sidebar/post-export buttons.");
                if (build.ActualHeight > 34 || build.Style.BasedOn != app.FindResource("AccentButtonStyle")) throw new Exception("Build button lost its compact Microsoft Fluent style.");
                var input = (TextBox)window.FindName("AppQuery");
                if (input.Style.BasedOn != app.FindResource("DefaultTextBoxStyle")) throw new Exception("Text field fell back to a legacy template.");
                var navigation = (StackPanel)window.FindName("Navigation");
                foreach (RadioButton step in navigation.Children) Visible(step, size);
                Snapshot(size, $"builder-{index}-{size.Width}x{size.Height}.png");
            }
            ((TextBlock)window.FindName("Status")).Text = string.Concat(Enumerable.Repeat("Long native servicing error details remain available in logs. ", 80));
            Layout(size); Visible((Button)window.FindName("BuildButton"), size);
            var logs=(Expander)window.FindName("BuildActivity");
            var activityScroll=(ScrollViewer)window.FindName("ActivityScroll");
            ((TextBlock)window.FindName("Status")).Text="Found 6 Windows editions. Choose one to customise.";
            logs.IsExpanded=true;Layout(size);Visible((Button)window.FindName("BuildButton"),size);
            if(window.ActivityEntries[0].Title!="Windows image ready" || window.FindName("TechnicalLog")!=null || activityScroll.ViewportHeight<=0)
                throw new Exception("Activity must render readable entries without a nested technical expander.");
            logs.IsExpanded=false;((TextBlock)window.FindName("Status")).Text="Windows version checked. Your ISO is unchanged.";
            logs.IsExpanded=true;Layout(size);
            if(window.ActivityEntries[0].Title!="Windows version checked" || activityScroll.VerticalOffset>1)throw new Exception("Reopening activity must show the latest readable entry.");
            logs.IsExpanded=false;
            ((TextBlock)window.FindName("Status")).Text = "Import an ISO to begin.";
            Console.WriteLine($"Five pages, navigation, long errors and expanded logs checked at {size.Width}x{size.Height}.");
        }
        var showResults = typeof(MainWindow).GetMethod("ShowSearchResults", BindingFlags.Instance | BindingFlags.NonPublic)!;
        showResults.Invoke(window, [new CatalogApp[] { new("Firefox", "Mozilla.Firefox", "fixture/no-artwork"), new("Steam", "Valve.Steam", "fixture/no-artwork") }]);
        Step(2);
        var results = (StackPanel)window.FindName("SearchResults"); var selections = (Panel)window.FindName("Apps");
        ((Button)window.FindName("RecommendedPresetButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(250);
        Layout(new Size(1160,740));
        var appViewport=(ScrollViewer)window.FindName("SettingsScroll");
        var viewportTop=appViewport.TranslatePoint(new Point(),(UIElement)window.Content).Y;
        var firstSearchResult=(FrameworkElement)results.Children[0];
        var resultTop=firstSearchResult.TranslatePoint(new Point(),(UIElement)window.Content).Y;
        if(resultTop<viewportTop || resultTop+firstSearchResult.ActualHeight>viewportTop+appViewport.ActualHeight)
            throw new Exception("Search results must stay visible at1160x740 after selecting the nine recommended apps.");
        showResults.Invoke(window,[new CatalogApp[]{new("Discord","Discord.Discord","1"),TechLoungeOS.RecommendedApps.All[0],new("Discord PTB","Discord.Discord.PTB","1")}]);
        if(results.Children.Count!=1 || !((TextBlock)window.FindName("SearchStatus")).Text.StartsWith("1 result."))throw new Exception("Search count must describe the visible, deduplicated and supported choices.");
        typeof(MainWindow).GetMethod("ClearAppsClick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[window,new RoutedEventArgs()]);Pump(250);
        showResults.Invoke(window,[new CatalogApp[]{new("Firefox","Mozilla.Firefox","fixture/no-artwork"),new("Steam","Valve.Steam","fixture/no-artwork")}]);
        Layout(new Size(1160,860));
        Console.WriteLine("Search results remain visible with nine selections; the result count excludes blocked channels and duplicates.");
        var updateButton=(Button)window.FindName("CheckUpdatesButton");
        if(System.Windows.Automation.AutomationProperties.GetName(updateButton)!="Check for updates" || !System.Windows.Automation.AutomationProperties.GetHelpText(updateButton).Contains("not Windows image updates"))
            throw new Exception("Update action must retain its requested name and distinguish app updates from Windows image servicing.");
        var readyPath=Path.Combine(folder,"ready-for-usb.iso");
        File.WriteAllText(readyPath,"Inert path-selection fixture. Never mounted or written to USB.");
        var usbSource=typeof(MainWindow).GetMethod("UsbSource",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var sourceBox=(TextBlock)window.FindName("IsoPath");var oldSource=sourceBox.Text;
        var outputBox=(TextBox)window.FindName("OutputPath");var oldOutput=outputBox.Text;
        sourceBox.Text=readyPath;outputBox.Text=readyPath;
        if((string?)usbSource.Invoke(window,[false])!=readyPath)throw new Exception("USB creation must reuse the imported source instead of asking for it again.");
        sourceBox.Text=oldSource;outputBox.Text=oldOutput;
        typeof(MainWindow).GetMethod("ShowExportReady",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[readyPath]);
        if((string?)usbSource.Invoke(window,[true])!=readyPath || usbSource.Invoke(window,[false])!=null)throw new Exception("Only the explicit post-export action may prefill the completed ISO.");
        File.Delete(readyPath);
        var otherPath=Path.Combine(folder,"other-inert.iso");File.WriteAllText(otherPath,"Inert fixture, never mounted.");
        outputBox.Text=otherPath;
        if(usbSource.Invoke(window,[true])!=null || usbSource.Invoke(window,[false])!=null)throw new Exception("A missing exported ISO must not silently substitute another image in either USB flow.");
        outputBox.Text=oldOutput;File.Delete(otherPath);
        foreach(var size in new[]{new Size(1160,860),new Size(720,500)}){
            Layout(size);
            typeof(MainWindow).GetMethod("ShowExportReady",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[readyPath]);Layout(size);
            var scroll=(ScrollViewer)window.FindName("SettingsScroll");
            Visible((Button)window.FindName("BuildButton"),size);
            Snapshot(size,$"builder-usb-next-{size.Width}x{size.Height}.png");
        }
        if(((TextBlock)window.FindName("UsbOutputLabel")).Text!="ready-for-usb.iso" || ((FrameworkElement)window.FindName("UsbNextStep")).Visibility!=Visibility.Visible)
            throw new Exception("Successful export must show the exact new ISO and next-step USB action.");
        Snapshot(new Size(1160,860),"builder-usb-next-step.png");
        typeof(MainWindow).GetMethod("SetBusy",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[true]);
        if(((Button)window.FindName("BuildButton")).IsEnabled)throw new Exception("The single final action must stay disabled during a media operation.");
        Snapshot(new Size(1160,860),"builder-usb-busy.png");
        typeof(MainWindow).GetMethod("SetBusy",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[false]);
        ((FrameworkElement)window.FindName("UsbNextStep")).Visibility=Visibility.Collapsed;
        typeof(MainWindow).GetField("exportedIso",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,null); Step(2);
        Console.WriteLine("Single Finish output action, completed-file summary, app-update semantics and busy-state protection passed.");
        UpdateRecoveryUiChecks.Run(window);
        Step(0);
        if(((Button)window.FindName("NextStepButton")).IsEnabled)throw new Exception("The first step must require an imported edition before continuing.");
        ((Button)window.FindName("BuildButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(((FrameworkElement)window.FindName("ExportPage")).Visibility!=Visibility.Visible || !((TextBlock)window.FindName("Status")).Text.StartsWith("Review your build settings"))throw new Exception("An early build invocation must stop at review rather than start servicing.");
        Step(1);
        ((Button)window.FindName("NextStepButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(((FrameworkElement)window.FindName("AppsPage")).Visibility!=Visibility.Visible)throw new Exception("Next should navigate without starting a build.");
        ((Button)window.FindName("PreviousStepButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if(((FrameworkElement)window.FindName("ComponentsPage")).Visibility!=Visibility.Visible)throw new Exception("Back should retain the wizard flow.");
        Step(2);
        Layout(new Size(1160,860));
        if(((FrameworkElement)window.FindName("SearchResultsSection")).Visibility!=Visibility.Visible)throw new Exception("Search result section stayed hidden after a search.");
        var firstResult=(FrameworkElement)results.Children[0];
        var firstResultTop=firstResult.TransformToAncestor((Visual)window.Content).Transform(new Point()).Y;
        if(firstResultTop+firstResult.ActualHeight>700)throw new Exception("Search results require scrolling past setup options before the first app is visible.");
        Console.WriteLine("Search results appear directly in the first viewport before setup options.");
        void Toggle(ToggleButton choice) { ((System.Windows.Automation.Provider.IToggleProvider)new ToggleButtonAutomationPeer(choice)).Toggle(); Pump(220); }
        var first=(ToggleButton)results.Children[0];
        Toggle(first);
        if(selections.Children.Count!=1||first.IsChecked!=true||!first.IsEnabled)throw new Exception("Card selection failed.");
        Step(0);Step(2);
        if(selections.Children.Count!=1)throw new Exception("Navigation lost app choice.");
        Toggle(first);
        if(selections.Children.Count!=0||first.IsChecked!=false)throw new Exception("Click-again deselection failed.");
        Toggle(first);Toggle((ToggleButton)selections.Children[0]);
        if(selections.Children.Count!=0||first.IsChecked!=false)throw new Exception("Selected card did not deselect the matching catalog card.");
        showResults.Invoke(window,[new CatalogApp[]{new("Discord PTB","Discord.Discord.PTB","1"),new("Discord Canary","Discord.Discord.Canary","1"),new("Discord ARM64","Discord.Discord.arm64","1")}]);
        if(results.Children.Count!=0)throw new Exception("An alternate Discord edition remained selectable.");
        showResults.Invoke(window,[new CatalogApp[]{new("Tool ARM64","Vendor.Tool.arm64","1")}]);
        var arm=(ToggleButton)results.Children[0];
        if(arm.IsEnabled)throw new Exception("An explicit ARM edition remained selectable for x64.");
        arm.IsChecked=true;Pump(220);
        if(selections.Children.Count!=0||arm.IsChecked==true)throw new Exception("Disabled ARM choice bypassed the selection guard.");
        var recommendations=(Panel)window.FindName("RecommendedApps");
        if(recommendations.Children.Count!=TechLoungeOS.RecommendedApps.PresetIds.Length)throw new Exception("Recommended collection missing.");
        foreach(var bundled in new[]{new CatalogApp("Microsoft 365","Microsoft.Office","1"),new CatalogApp("Microsoft Edge","Microsoft.Edge","1"),new CatalogApp("HatchZip",TechLoungeOS.RecommendedApps.HatchZip,"1")})
        {
            showResults.Invoke(window,[new[]{bundled}]);
            if(results.Children.Count!=0||selections.Children.Count!=0||!((TextBlock)window.FindName("SearchStatus")).Text.StartsWith("Already included"))throw new Exception("Mandatory app must not appear as an optional selection.");
        }
        showResults.Invoke(window,[new CatalogApp[]{new("Discord","Discord.Discord","1")}]);
        var discord=(ToggleButton)results.Children[0];Toggle(discord);
        if(((CatalogApp)discord.Tag).Id!=TechLoungeOS.RecommendedApps.Discord||((CatalogApp)discord.Tag).Name!="Discord")throw new Exception("Discord search did not use the required recipe and normal name.");
        var recommendedDiscord=(ToggleButton)recommendations.Children[0];
        if(recommendedDiscord.IsChecked!=true||!recommendedDiscord.IsEnabled)throw new Exception("Discord cards lost their shared state.");
        Toggle(recommendedDiscord);
        if(selections.Children.Count!=0||discord.IsChecked!=false)throw new Exception("Discord deselection failed.");
        var preset=(Button)window.FindName("RecommendedPresetButton");
        preset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(240);
        if(selections.Children.Count!=TechLoungeOS.RecommendedApps.PresetIds.Length)throw new Exception("The requested recommendations were not selected.");
        Snapshot(new Size(1160,860),"builder-recommended-selected.png");
        preset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(240);
        if(selections.Children.Count!=TechLoungeOS.RecommendedApps.PresetIds.Length)throw new Exception("Recommended preset introduced duplicates.");
        var steam=selections.Children.OfType<ToggleButton>().Single(row=>((CatalogApp)row.Tag).Id=="Valve.Steam");
        Toggle(steam);
        if(selections.Children.Count!=TechLoungeOS.RecommendedApps.PresetIds.Length-1)throw new Exception("A recommended app could not be deselected individually.");
        // Rapid changes must settle to the last requested state without stale removal callbacks.
        for(int i=0;i<16;i++)recommendedDiscord.IsChecked=!recommendedDiscord.IsChecked;
        Pump(260);
        if(selections.Children.Count!=TechLoungeOS.RecommendedApps.PresetIds.Length-1||recommendedDiscord.IsChecked!=true)throw new Exception("Rapid card toggles lost final selection state.");
        Console.WriteLine("Whole-card toggles, shared highlights, alternate-Discord blocking, recommended-app preset, individual deselection and rapid input passed. No installers executed.");
        typeof(MainWindow).GetMethod("ClearAppsClick",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[window,new RoutedEventArgs()]);Pump(240);
        if(selections.Children.Count!=0)throw new Exception("Clear selection failed.");
        var editionBox = (ComboBox)window.FindName("Editions");
        editionBox.ItemsSource = new Edition[] { new(1, "Test Home", "9", "10.0.26200.1"), new(2, "Test Pro", "9", "10.0.26200.1") };
        editionBox.SelectedIndex = 1; Step(2); Step(0);
        if (editionBox.SelectedIndex != 1) throw new Exception("Edition selection lost across steps.");
        ((RadioButton)window.FindName("AppsStep")).IsChecked = true;
        if (((FrameworkElement)window.FindName("AppsPage")).Visibility != Visibility.Visible) throw new Exception("Checked-state navigation did not change page.");
        editionBox.ItemsSource = null;
        var options = (WrapPanel)window.FindName("Components");
        ((CheckBox)options.Children[0]).IsChecked = true; Step(1); Step(3); Step(1);
        if (((CheckBox)options.Children[0]).IsChecked != true || options.Children.Count != 11) throw new Exception("Navigation lost component choices.");
        Console.WriteLine("App add/remove/re-add/clear, duplicate prevention and navigation state retention passed. No installers executed.");
        void Pump(int milliseconds)
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
            timer.Start(); System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        var writeLog = typeof(MainWindow).GetMethod("Write", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var logProducer = Task.Run(() =>
        {
            for (var i = 0; i < 5000; i++) writeLog.Invoke(window, [$"fixture line {i:D4} " + new string('x', 240)]);
        });
        if (!logProducer.Wait(2000)) throw new Exception("Native log production waits for the UI dispatcher.");
        Pump(200);
        if (window.DiagnosticText.Length > 100_000 || !window.DiagnosticText.Contains("fixture line 4999")) throw new Exception("Batched log view lost its newest line or exceeded its bound.");
        Console.WriteLine("5000 background log lines finish without a UI pump; the bounded view retains the latest line.");
        var busyMethod = typeof(MainWindow).GetMethod("SetBusy", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var phaseMethod = typeof(MainWindow).GetMethod("ShowBuildPhase", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var cancellationField = typeof(MainWindow).GetField("cancellation", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var buildStatus = (TextBlock)window.FindName("Status");
        using (var phaseCancellation = new CancellationTokenSource())
        {
            cancellationField.SetValue(window, phaseCancellation); busyMethod.Invoke(window, [true]);
            phaseMethod.Invoke(window, ["Committing"]);
            if (!buildStatus.Text.Contains("Saving Windows changes")) throw new Exception("The main status hides the active saving phase.");
            ((Button)window.FindName("CancelButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var cancelledText = buildStatus.Text; phaseMethod.Invoke(window, ["Creating ISO"]);
            if (!phaseCancellation.IsCancellationRequested || buildStatus.Text != cancelledText) throw new Exception("Queued progress replaces cancellation status.");
            busyMethod.Invoke(window, [false]); cancellationField.SetValue(window, null);
            buildStatus.Text = "Finished test"; phaseMethod.Invoke(window, ["Servicing"]);
            if (buildStatus.Text != "Finished test") throw new Exception("Late progress replaces the idle result.");
        }
        Console.WriteLine("The native main status shows saving and retains cancellation/final results against late progress.");
        if (window.Icon == null) throw new Exception("Window/taskbar icon is missing.");
        var atmosphere = (RadialGradientBrush)window.FindName("AtmosphereBrush");
        var setMotion = typeof(MainWindow).GetMethod("SetAtmosphereMotion", BindingFlags.Instance | BindingFlags.NonPublic)!;
        setMotion.Invoke(window, [true]); Pump(200);
        if (!SystemParameters.HighContrast && (!atmosphere.HasAnimatedProperties || atmosphere.Center == new Point(.9, .05)))
            throw new Exception("Background drift did not advance.");
        setMotion.Invoke(window, [false]);
        if (atmosphere.HasAnimatedProperties) throw new Exception("Background clocks were not removed on stop.");
        typeof(MainWindow).GetMethod("RefreshMotion", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        if (atmosphere.HasAnimatedProperties) throw new Exception("A hidden inactive window should not animate.");
        Console.WriteLine("Window icon loaded; background clock advances, stops cleanly and stays stopped while hidden/inactive.");
        Layout(new Size(1160, 860)); Step(0); Step(1); Step(2); Step(3); Step(4);
        Pump(200);
        foreach (var name in pages)
        {
            var page = (FrameworkElement)window.FindName(name);
            if (Math.Abs(page.Opacity - 1) > .001 || page.RenderTransform is TranslateTransform t && Math.Abs(t.Y) > .001)
                throw new Exception("Rapid navigation leaves a stale animation offset or opacity.");
        }
        if (((FrameworkElement)window.FindName("ExportPage")).Visibility != Visibility.Visible) throw new Exception("Rapid navigation queued the wrong destination.");
        if (Math.Abs(((TextBlock)window.FindName("StepTitle")).Opacity - 1) > .001 ||
            Math.Abs(((TextBlock)window.FindName("StepDescription")).Opacity - 1) > .001) throw new Exception("Heading fades did not settle.");
        Console.WriteLine("Dispatcher animation clocks settle after rapid navigation; GPU frame pacing remains unmeasured.");
        Step(0); Snapshot(new Size(1160, 860), "builder-overview-preview.png");
        var sourceCard = (FrameworkElement)window.FindName("SourcePage");
        var sourceBitmap = new RenderTargetBitmap((int)Math.Ceiling(sourceCard.ActualWidth), (int)Math.Ceiling(sourceCard.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        sourceBitmap.Render(sourceCard);
        var sourceEncoder = new PngBitmapEncoder(); sourceEncoder.Frames.Add(BitmapFrame.Create(sourceBitmap));
        using (var stream = File.Create(Path.Combine(folder, "source-section-preview.png"))) sourceEncoder.Save(stream);

        if (args.Length == 2)
        {
            var captured = System.Text.Json.JsonSerializer.Deserialize<CatalogApp[]>(File.ReadAllText(args[1])) ?? [];
            showResults.Invoke(window, [captured]); ((TextBox)window.FindName("AppQuery")).Text = "VLC";
            // Wait for real optional artwork before capturing, without blocking its UI continuations.
            Pump(10000);
            ((TextBlock)window.FindName("SearchStatus")).Text = "Results from WinGet · check the app name and package ID before adding.";
            if (captured.Length > 0) ((Button)((DockPanel)results.Children[0]).Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Pump(300);
            if (captured.Length > 0 && captured[0].Id == "VideoLAN.VLC" &&
                ((Grid)((DockPanel)((ToggleButton)results.Children[0]).Content).Children[0]).Children[0] is not Image)
                throw new Exception("Loaded VLC artwork did not reach its search-result control.");
            Step(2); Snapshot(new Size(1160, 1000), "builder-app-search-preview.png");
        }
        var usbWindow=new BootDriveWindow(null);
        usbWindow.ShowActivated=false;usbWindow.ShowInTaskbar=false;usbWindow.WindowStartupLocation=WindowStartupLocation.Manual;
        usbWindow.Left=-10000;usbWindow.Top=-10000;usbWindow.Show();Pump(300);
        var usbContent=(FrameworkElement)usbWindow.Content;
        foreach(var size in new[]{new Size(720,700),new Size(620,520)})
        {
            usbWindow.Width=size.Width;usbWindow.Height=size.Height;
            usbContent.Measure(size);usbContent.Arrange(new Rect(size));usbContent.UpdateLayout();
            foreach(var name in new[]{"Start","CloseButton"})
            {
                var control=(FrameworkElement)usbWindow.FindName(name);
                var bounds=new Rect(control.TranslatePoint(new Point(),usbContent),control.RenderSize);
                if(bounds.Left<0 || bounds.Top<0 || bounds.Right>size.Width || bounds.Bottom>size.Height || bounds.Height<=0)
                    throw new Exception("USB working-folder and action controls must remain reachable at "+size+": "+name);
            }
        }
        usbWindow.Close();Console.WriteLine("USB working-folder and footer bounds fit at normal and minimum sizes. Native theme and picker require separate live checks.");
        WallpaperUiChecks.Run(window);
        Step(3);Snapshot(new Size(1160,860),"wallpaper-presets.png");
        window.Close(); app.Shutdown();
    }
}
