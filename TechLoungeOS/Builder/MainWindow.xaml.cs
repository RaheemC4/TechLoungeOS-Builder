using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace TechLoungeOS;

public partial class MainWindow : Window
{
    private CancellationTokenSource? cancellation;
    private bool running;
    private string? exportedIso;
    private string? exportedChoices;
    private string? UsbSource(bool exportedOnly) => exportedOnly
        ? File.Exists(exportedIso) && exportedChoices==BuildChoiceStamp()?exportedIso:null
        : File.Exists(IsoPath.Text)?IsoPath.Text:null;
    private void ExistingIsoUsbClick(object sender,RoutedEventArgs e)
    {
        if(running || recoveryRequired)return;
        var dialog=new OpenFileDialog{Filter="Windows ISO|*.iso",Title="Choose your finished Windows ISO"};
        if(dialog.ShowDialog(this)!=true)return;
        try
        {
            SetBusy(true);
            // No build callback: preserve the chosen ISO and copy its existing contents.
            new BootDriveWindow(dialog.FileName){Owner=this}.ShowDialog();
        }
        catch(Exception ex){Status.Text="Could not open USB creation: "+ex.Message;}
        finally{SetBusy(false);}
    }
    private void OpenUsbPlan()
    {
        if(running || recoveryRequired)return;
        try
        {
            var completed=UsbSource(true);
            var source=completed??UsbSource(false);
            if(source is null || (completed is null && Editions.SelectedItem is not Edition)){ShowStep(0);Status.Text="Choose your Windows ISO and edition first.";return;}
            SetBusy(true);
            var saveTo=OutputModeIndex==2?OutputPath.Text:null;
            if(saveTo is not null && string.IsNullOrWhiteSpace(saveTo)){Status.Text="Choose a filename and location for your saved ISO.";return;}
            if(completed is not null && saveTo is not null && !Path.GetFullPath(saveTo).Equals(Path.GetFullPath(completed),StringComparison.OrdinalIgnoreCase))
            {
                completed=null;source=UsbSource(false);
                if(source is null || Editions.SelectedItem is not Edition){ShowStep(0);Status.Text="Choose the source ISO before building a new ISO copy.";return;}
            }
            new BootDriveWindow(source,completed is null?BuildForUsb:null,completed is null?saveTo:null){Owner=this}.ShowDialog();
        }
        catch(Exception ex){Status.Text="Could not open USB creation: "+ex.Message;}
        finally {SetBusy(false);}
    }
    private void ShowExportReady(string path)
    {
        exportedIso=path;
        exportedChoices=BuildChoiceStamp();
        UsbOutputLabel.Text=Path.GetFileName(path);
        UsbOutputLabel.ToolTip=path;
        UsbNextStep.Visibility=Visibility.Visible;
        ShowStep(4);
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(SettingsScroll.ScrollToBottom));
    }
    public MainWindow():this(true){}
    public MainWindow(bool automaticUpdates)
    {
        InitializeComponent();
        InitialiseWallpaperPresets();
        SetupRegionChoice.ItemsSource=SetupRegional.Preferred(SetupRegional.Regions(),"en-GB","en-US");SetupRegionChoice.SelectedIndex=0;
        try { SetupKeyboardChoice.ItemsSource=SetupRegional.Preferred(SetupRegional.Keyboards(),"00000809","00000409"); }
        catch(Exception ex) when(ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            SetupKeyboardChoice.ItemsSource=new[]{new SetupRegional.Choice(null,"Keyboard names unavailable; keep the ISO’s layouts")};
        }
        SetupKeyboardChoice.SelectedIndex=0;
        Closing+=ApplyUpdateOnClose; Closed+=(_,_)=>{updateLifetime.Cancel();updateLifetime.Dispose();};
        Loaded+=async (_,_)=>{CheckRecoveryState();if(automaticUpdates)await CheckUpdates();};
        ShowRecommendations();
        var area = SystemParameters.WorkArea;
        if (area.Width > 0 && area.Height > 0)
        {
            Width = Math.Min(Width, area.Width); Height = Math.Min(Height, area.Height);
            MinWidth = Math.Min(MinWidth, Width); MinHeight = Math.Min(MinHeight, Height);
        }
        OscdimgPath.Text = @"C:\Program Files (x86)\Windows Kits\10\Assessment and Deployment Kit\Deployment Tools\amd64\Oscdimg\oscdimg.exe";
        var includedIsoTool=Path.Combine(AppContext.BaseDirectory,"support","oscdimg.exe");
        if(File.Exists(includedIsoTool)){OscdimgPath.Text=includedIsoTool;IsoToolSettings.Visibility=Visibility.Collapsed;}
        foreach (var c in Policy.RemovableApps.Select(n => new Component("Appx", n)).Concat(Policy.RemovableFeatures.Select(n => new Component("Feature", n))))
        {
            var content=new StackPanel();
            content.Children.Add(new TextBlock{Text=ComponentLabel(c.Name),TextWrapping=TextWrapping.Wrap});
            var help=new TextBlock{Text=ComponentDescription(c.Name),FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,0)};
            help.SetResourceReference(TextBlock.ForegroundProperty,"Muted");content.Children.Add(help);
            var choice=new CheckBox{Content=content,ToolTip=c.Name,Tag=c,Width=285,Margin=new Thickness(0,8,18,8),VerticalContentAlignment=VerticalAlignment.Top};
            System.Windows.Automation.AutomationProperties.SetName(choice,ComponentLabel(c.Name));
            System.Windows.Automation.AutomationProperties.SetHelpText(choice,ComponentDescription(c.Name));
            Components.Children.Add(choice);
        }
        InitialiseActivity();
        RefreshOutputChoice();
        Shell.SizeChanged += (_, e) =>
        {
            NavigationColumn.Width = new GridLength(e.NewSize.Width < 900 ? 174 : 218);
            ImageSummary.Visibility = e.NewSize.Height < 620 ? Visibility.Collapsed : Visibility.Visible;
        };
        SourceInitialized += (_, _) => WindowsChrome.Apply(this);
        Loaded += (_, _) => RefreshMotion();
        Activated += (_, _) => RefreshMotion(); Deactivated += (_, _) => RefreshMotion();
        IsVisibleChanged += (_, _) => RefreshMotion(); StateChanged += (_, _) => RefreshMotion();
        Closed += (_, _) => SetAtmosphereMotion(false);
        Closed += (_, _) => SetActivityPower(false);
        Closing += (_, e) => { if (running) { e.Cancel = true; Status.Text = "Wait for the current operation and cleanup before closing."; } };
    }
    private static string ComponentLabel(string name) => name switch
    {
        "Microsoft.BingNews" => "News", "Microsoft.BingWeather" => "Weather",
        "Microsoft.GetHelp" => "Get Help", "Microsoft.Getstarted" => "Windows Tips",
        "Microsoft.MicrosoftSolitaireCollection" => "Solitaire", "Microsoft.People" => "People",
        "Microsoft.WindowsFeedbackHub" => "Feedback Hub", "Microsoft.YourPhone" => "Phone Link",
        "Clipchamp.Clipchamp" => "Clipchamp video editor", "Printing-XPSServices-Features" => "Save documents as XPS",
        "WorkFolders-Client" => "Company file sync (Work Folders)", _ => name
    };
    private static string ComponentDescription(string name)=>name switch
    {
        "Printing-XPSServices-Features"=>"A virtual printer that saves XPS document files. Keep it if you use XPS documents.",
        "WorkFolders-Client"=>"Syncs files with a company’s Work Folders server. Usually unnecessary on a home PC.",
        "Microsoft.BingNews"=>"Microsoft’s news-reading app.","Microsoft.BingWeather"=>"Microsoft’s weather forecast app.",
        "Microsoft.GetHelp"=>"The Windows help and troubleshooting app.","Microsoft.Getstarted"=>"Windows tips and introductory guides.",
        "Microsoft.MicrosoftSolitaireCollection"=>"Microsoft’s collection of card games.","Microsoft.People"=>"Microsoft’s contact-management app.",
        "Microsoft.WindowsFeedbackHub"=>"Sends Windows feedback and problem reports to Microsoft.","Microsoft.YourPhone"=>"Connects a phone to Windows for messages, calls and other features.",
        "Clipchamp.Clipchamp"=>"Microsoft’s video-editing app.",_=>"Optional Windows component. Leave it installed if you are unsure."
    };
    private readonly object pendingLogGate = new();
    private readonly System.Text.StringBuilder pendingLog = new();
    private bool logFlushQueued;
    private void Write(string text)
    {
        // File logging remains synchronous in BuildEngine. The optional view must never hold up DISM output.
        lock (pendingLogGate)
        {
            pendingLog.AppendLine(text.Length > 100_000 ? text[^100_000..] : text);
            if (pendingLog.Length > 200_000) pendingLog.Remove(0, pendingLog.Length - 100_000);
            if (logFlushQueued || Dispatcher.HasShutdownStarted) return;
            logFlushQueued = true;
        }
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(FlushLog));
    }
    private void FlushLog()
    {
        string batch;
        lock (pendingLogGate) { batch = pendingLog.ToString(); pendingLog.Clear(); logFlushQueued = false; }
        diagnosticBuffer.Append(batch);
        if(diagnosticBuffer.Length>100_000)diagnosticBuffer.Remove(0,diagnosticBuffer.Length-100_000);
        if(diagnosticView is not null)
        {
            var atEnd=diagnosticView.VerticalOffset>=diagnosticView.ExtentHeight-diagnosticView.ViewportHeight-2;
            diagnosticView.Text=DiagnosticText;
            if(atEnd)diagnosticView.ScrollToEnd();
        }
    }
    private void SetBusy(bool value)
    {
        SetActivityPower(value);
        running = value;
        var available=!value && !recoveryRequired;
        Inputs.IsEnabled=available; Navigation.IsEnabled=available;
        PreviousStepButton.IsEnabled=available;
        NextStepButton.IsEnabled=available && (currentStep!=0 || Editions.SelectedItem is Edition);
        BuildButton.IsEnabled=available && Editions.SelectedItem is Edition;
        WindowsVersionCheck.IsEnabled=available && Editions.SelectedItem is Edition;
        CancelButton.IsEnabled = value && cancellation != null;
        CancelButton.Visibility=value?Visibility.Visible:Visibility.Collapsed;
        Busy.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
    }
    private async void ImportClick(object sender, RoutedEventArgs e)
    {
        if(running || recoveryRequired)return;
        var dialog = new OpenFileDialog { Filter = "Windows ISO|*.iso" };
        if (dialog.ShowDialog() != true) return;
        await ImportIso(dialog.FileName);
    }
    private async void DownloadWindowsIsoClick(object sender,RoutedEventArgs e)
    {
        if(running||recoveryRequired)return;
        IsoDownloadResult? result=null;
        try
        {
            SetBusy(true);var dialog=new MicrosoftIsoWindow{Owner=this};
            if(dialog.ShowDialog()==true)result=dialog.Downloaded;
        }
        catch(Exception ex){Status.Text="Could not open Windows download: "+ex.Message;}
        finally{SetBusy(false);}
        if(result is not null)
        {
            Write("Microsoft ISO downloaded: "+Path.GetFileName(result.Path)+" · SHA-256 "+result.Sha256+(result.PublishedHashMatched?" · matched Microsoft's published hash":" · calculated; Microsoft did not supply a comparison hash"));
            await ImportIso(result.Path);
        }
    }
    private async Task ImportIso(string path)
    {
        SetBusy(true);Status.Text="Reading Windows editions from your ISO…"; EditionHint.Text = "Reading available editions…"; Editions.ItemsSource = null; Editions.IsEnabled = false; IsoPath.Text = ""; IsoFileName.Text = "No ISO selected";
        try
        {
            var editions = await new BuildEngine(Write).Import(path);
            IsoPath.Text = path; IsoFileName.Text = Path.GetFileName(path); Editions.ItemsSource = editions; Editions.IsEnabled = editions.Length > 0; EditionHint.Text = "Choose the edition you have a licence for."; Editions.SelectedIndex = 0;
            SourceProfileStatus.Text = SourcePreservation.Classify(path, editions.Select(e => e.ImageName)).Classification + ". Preservation mode: retain existing components and service configuration.";
            Status.Text = $"Found {editions.Length} Windows edition{(editions.Length==1?"":"s")}. Choose one to customise.";
        }
        catch (Exception ex) { EditionHint.Text = "Import an ISO to see its editions."; Status.Text = "Import failed: " + ex.Message; }
        finally { SetBusy(false); }
    }
    private void PresetClick(object sender, RoutedEventArgs e)
    {
        foreach (CheckBox c in Components.Children) c.IsChecked = ((Component)c.Tag).Kind == "Appx";
    }
    private void KeepClick(object sender, RoutedEventArgs e) { foreach (CheckBox c in Components.Children) c.IsChecked = false; }
    private static string BundledTweaksFolder=>Path.Combine(AppContext.BaseDirectory,"integration");
    private void ToolClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Microsoft ADK ISO tool|oscdimg.exe" };
        if (dialog.ShowDialog() == true) OscdimgPath.Text = dialog.FileName;
    }
    private void OutputClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Windows ISO|*.iso", FileName = "TechLoungeOS.iso",OverwritePrompt=false };
        if (dialog.ShowDialog() == true) OutputPath.Text = dialog.FileName;
    }
    private async void BuildClick(object sender,RoutedEventArgs e)
    {
        if(running || recoveryRequired)return;
        if(currentStep!=4){ShowStep(4);Status.Text="Review your build settings, then choose Build ISO.";return;}
        if(OutputModeIndex!=1){OpenUsbPlan();return;}
        try
        {
            var destination=OutputPath.Text;
            var options=ReadBuildOptions(IsoOutput.StagingPath(destination));Policy.Validate(options);
            var replace=File.Exists(destination)?"\n\nThis will replace the existing ISO after the new image passes verification.":"";
            if(MessageBox.Show(this,$"Build a customised ISO?\n\nOptional components to remove: {options.Remove.Length}\nOptional apps selected: {options.Apps.Length}\n\nSave to: {destination}{replace}","Review build",MessageBoxButton.OKCancel,MessageBoxImage.Information)!=MessageBoxResult.OK)return;
            await BuildOutput(options,destination,CancellationToken.None);
        }
        catch(Exception ex){Status.Text="Could not finish the ISO: "+ex.Message;Write(ex.ToString());}
    }
    private Task<bool> RunBuild(BuildOptions options)=>RunBuildCore(options,CancellationToken.None);
    private async Task<bool> RunBuildCore(BuildOptions options,CancellationToken external,IProgress<string>? secondary=null)
    {
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(external); SetBusy(true); Status.Text = "Checking the selected files and build requirements…";
        var success=false;
        var progress = new Progress<string>(phase=>{ShowBuildPhase(phase);secondary?.Report(Status.Text);});
        try
        {
            await Task.Run(() => new BuildEngine(Write, progress: progress).Build(options, cancellation.Token));
            Status.Text = "ISO exported. VM installation validation is still required.";
            ShowExportReady(options.Output);success=true;
        }
        catch (OperationCanceledException) { Status.Text = "Cancelled. Check build.log for cleanup results."; }
        catch (Exception ex) { Status.Text = "Build failed: " + ex.Message; Write(ex.ToString()); }
        finally { cancellation.Dispose(); cancellation = null; SetBusy(false); }
        return success;
    }
    private void ShowBuildPhase(string phase)
    {
        // Queued notifications must not overwrite cancellation or the final result.
        if (!running || cancellation?.IsCancellationRequested == true) return;
        Status.Text = phase switch
        {
            "Committing" => "Saving Windows changes. This can take several minutes; keep the app open.",
            "Creating ISO" => "Creating your bootable ISO…",
            "Verifying ISO contents" => "Checking every file in your exported ISO. Keep the app open…",
            "Finalising output filename" => "All files verified. Waiting for Windows to finish saving the ISO. Keep the app open…",
            "Servicing" => "Customising Windows and checking image health…",
            _ => phase + ". Full logs are saved in the workspace."
        };
    }
    private void CancelClick(object sender, RoutedEventArgs e)
    {
        cancellation?.Cancel(); CancelButton.IsEnabled = false; Status.Text = "Cancellation requested. Waiting for the current operation and cleanup.";
    }
}
