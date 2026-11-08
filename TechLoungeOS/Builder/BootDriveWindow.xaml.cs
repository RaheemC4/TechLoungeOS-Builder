using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace TechLoungeOS;
public partial class BootDriveWindow : Window
{
    private CancellationTokenSource? operation;
    private readonly BootableDrive engine;
    private readonly string? sourceIso;
    private readonly Func<string?,string[],long,CancellationToken,IProgress<string>,Task<UsbBuildResult>>? build;
    private UsbBuildResult? built;
    private PreparedUsb? prepared;
    private string? previousSave;
    private readonly string? saveDestination;
    private string[] usbVolumes=[];
    private readonly System.Collections.ObjectModel.ObservableCollection<ActivityEntry> activity=[];
    private readonly System.Text.StringBuilder diagnostics=new();
    private readonly object diagnosticGate=new();
    private bool failed;
    private bool viewingCompleted;
    private WorkProgress? timing;
    private UsbTransferProgress? transfer;
    private readonly System.Windows.Threading.DispatcherTimer clock=new(){Interval=TimeSpan.FromSeconds(1)};
    private int currentStep;
    private int StepCount=>build is null?2:saveDestination is null?5:6;
    private void UpdateClock()
    {
        if(timing is null)return;
        ElapsedCaption.Text=WorkProgress.Caption(timing,DateTimeOffset.UtcNow);
        ActivityPresentation.Caption(ElapsedCaption,true);
        if(transfer is { } current)
        {
            TransferCaption.Text=current.Caption;TransferCaption.Visibility=Visibility.Visible;ActivityPresentation.Caption(TransferCaption,true);
            var from=TransferProgress.Value;TransferProgress.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty,null);TransferProgress.Value=current.Percent;
            if(SystemParameters.ClientAreaAnimation&&!SystemParameters.HighContrast)
                TransferProgress.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty,new System.Windows.Media.Animation.DoubleAnimation(from,current.Percent,TimeSpan.FromMilliseconds(200)){FillBehavior=System.Windows.Media.Animation.FillBehavior.Stop});
        }
    }
    private void OnUsbTransfer(UsbTransferProgress value,CancellationTokenSource owner)
    {
        if(operation!=owner||failed||owner.IsCancellationRequested)return;
        if(transfer?.Verifying!=value.Verifying)
        {
            TransferProgress.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty,null);TransferProgress.Value=0;
            StatusMessage.Text=value.Verifying?"Verifying every file written to the USB drive.":"Creating the confirmed USB drive. Keep it connected until verification finishes.";
        }
        transfer=value;Progress.Visibility=Visibility.Collapsed;TransferProgress.Visibility=Visibility.Visible;
        timing?.Observe(value.Percent.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)+"% complete",DateTimeOffset.UtcNow);
        UpdateClock();
    }
    private void Step(int value)
    {
        if(value==currentStep)return;currentStep=value;
        var labels=build is null?new[]{"Check setup files","Write and verify USB"}:saveDestination is null?
            new[]{"Prepare build","Copy Windows files","Customise Windows","Check USB files","Write and verify USB"}:
            new[]{"Prepare build","Copy Windows files","Customise Windows","Create and verify ISO","Check USB files","Write and verify USB"};
        StepCaption.Text=$"Step {value} of {StepCount} · {labels[value-1]}"+(build is not null&&value==3?" · longest stage":"");StepCaption.Visibility=Visibility.Visible;ActivityPresentation.Caption(StepCaption);
    }
    private void BuildPhase(string message,CancellationTokenSource owner)
    {
        if(operation!=owner||failed||owner.IsCancellationRequested||currentStep>4)return;
        Step(Math.Max(currentStep,WorkProgress.BuildStep(message)));StatusMessage.Text=message;
    }
    public BootDriveWindow(string? iso):this(iso,null,null){}
    internal static BootDriveWindow FromBuiltMedia(string iso,string workspace,string hash)
    {
        var window=new BootDriveWindow(iso){built=new(iso,workspace,hash)};
        window.SourceHint.Text="Reuses your completed build files without extracting the ISO again.";
        return window;
    }
    internal static BootDriveWindow ResumePrepared(string checkpoint)
    {
        var saved=BootableDrive.LoadCheckpoint(checkpoint);
        var window=new BootDriveWindow(null){prepared=saved};
        window.Source.Text="Your prepared Windows setup";
        window.Source.ToolTip=saved.Workspace;
        window.SourceHint.Text="Uses the Windows version, apps and wallpaper already prepared. Choose your USB drive to continue.";
        return window;
    }
    internal BootDriveWindow(string? iso,Func<string?,string[],long,CancellationToken,IProgress<string>,Task<UsbBuildResult>>? build,string? saveTo,bool enumerateDrives=true)
    {
        InitializeComponent();sourceIso=iso;this.build=build;Source.Text=File.Exists(iso)?Path.GetFileName(iso):"Choose a Windows image on the main page.";Source.ToolTip=iso;
        saveDestination=saveTo;
        if(saveTo is not null){IsoSaveSummary.Text="ISO copy: "+saveTo;IsoSaveSummary.Visibility=Visibility.Visible;}
        if(build is null)SourceHint.Text="Your completed ISO will be copied and verified on the USB drive.";

        var area=SystemParameters.WorkArea;Width=Math.Min(Width,area.Width);Height=Math.Min(Height,area.Height);MinWidth=Math.Min(MinWidth,Width);MinHeight=Math.Min(MinHeight,Height);
        engine=new(new ProcessRunner(Write),Write);ActivityItems.ItemsSource=activity;
        var descriptor=System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty,typeof(TextBlock));
        EventHandler changed=(_,_)=>RecordMessage();descriptor.AddValueChanged(StatusMessage,changed);
        Closed+=(_,_)=>descriptor.RemoveValueChanged(StatusMessage,changed);RecordMessage();clock.Tick+=(_,_)=>UpdateClock();Closed+=(_,_)=>clock.Stop();
        if(enumerateDrives)Loaded+=async (_,_)=>await Refresh();
        SourceInitialized+=(_,_)=>WindowsChrome.Apply(this);
        Closing+=(_,e)=>{if(operation!=null){e.Cancel=true;operation.Cancel();StatusMessage.Text="Stopping after the current operation. The drive may contain incomplete setup files.";}};
    }
    private void Write(string message)
    {
        timing?.Observe(message,DateTimeOffset.UtcNow);
        lock(diagnosticGate){diagnostics.AppendLine(message);if(diagnostics.Length>250000)diagnostics.Remove(0,diagnostics.Length-200000);}
    }
    private void RecordMessage()
    {
        var entry=failed?new ActivityEntry(DateTimeOffset.Now,"USB was not created",StatusMessage.Text,"Needs attention","\uE7BA"):ActivityEntry.FromStatus(StatusMessage.Text,operation is not null);
        if(activity.FirstOrDefault()?.Detail==entry.Detail && activity[0].State==entry.State)return;
        timing?.Phase(DateTimeOffset.UtcNow);
        if(activity.FirstOrDefault() is {State:"Working"} active)activity[0]=active with{State=failed?"Needs attention":"Done"};
        activity.Insert(0,entry);while(activity.Count>30)activity.RemoveAt(activity.Count-1);
        if(operation is not null && entry.IsWorking)ResultHeading.Text=entry.Title;
        Message.Text=entry.Detail;ActivityPresentation.Caption(Message);
        ActivityScroll.ScrollToTop();
    }
    private void SaveDiagnosticLog(object sender,RoutedEventArgs e)
    {
        var dialog=new SaveFileDialog{Filter="Text log|*.txt",FileName="TechLoungeOS-USB-log.txt"};
        if(dialog.ShowDialog(this)!=true)return;
        try{lock(diagnosticGate)File.WriteAllText(dialog.FileName,diagnostics.ToString());}
        catch(Exception error){MessageBox.Show(this,"Could not save the log: "+error.Message,"Save log",MessageBoxButton.OK,MessageBoxImage.Error);}
    }
    internal void ShowFailure(Exception error,bool writingStarted,bool showDialog=true)
    {
        failed=true;clock.Stop();transfer=null;TransferCaption.Visibility=Visibility.Collapsed;TransferProgress.Visibility=Visibility.Collapsed;Progress.Visibility=Visibility.Collapsed;ResultHeading.Text="USB was not created";
        ResultBanner.BorderBrush=SystemParameters.HighContrast?SystemColors.WindowTextBrush:new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(242,141,153));
        var explanation=error.Message.Split(new[]{"\r\n","\n"},StringSplitOptions.None)[0];
        if(explanation.Length>240 || explanation.Contains("powershell.exe",StringComparison.OrdinalIgnoreCase)||explanation.Contains("dism.exe",StringComparison.OrdinalIgnoreCase))explanation="Windows could not complete this step. Save the diagnostic log for the exact cause.";
        StatusMessage.Text=explanation+"\n\n"+(writingStarted?"The USB may be incomplete. Recreate it before using it to install Windows.":"Your USB drive has not been erased or changed.");
        Write(error.ToString());Start.Content="Try again";
        if(showDialog)MessageBox.Show(this,Message.Text,"USB was not created",MessageBoxButton.OK,MessageBoxImage.Error);
    }
    private async Task Refresh()
    {
        try
        {
            Start.IsEnabled=false;
            var disks=await Task.Run(()=>engine.List());
            usbVolumes=disks.Where(d=>d.BusType==7).SelectMany(d=>d.Volumes).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            Drives.ItemsSource=disks.Where(d=>d.BusType==7 && BootableDrive.Rejection(d)==null).ToArray();
            Drives.SelectedIndex=-1;
            StatusMessage.Text=Drives.Items.Count==0?"No eligible USB drives. Connect a USB drive that does not contain Windows, the app or its working files.":"Select the USB drive you intend to erase.";
        }
        catch(Exception ex){ShowFailure(new IOException("Could not read your USB drives. Reconnect the drive and try again.",ex),false);}
        finally{Start.IsEnabled=true;}
    }
    private async void RefreshClick(object sender,RoutedEventArgs e){if(operation==null)await Refresh();}
    private async void StartClick(object sender,RoutedEventArgs e)
    {
        if(operation!=null)return;
        if(viewingCompleted)
        {
            viewingCompleted=false;OptionsPanel.Visibility=Visibility.Visible;Drives.SelectedIndex=-1;Start.Content="Create setup USB";
            Start.SetResourceReference(StyleProperty,"Primary");CloseButton.SetResourceReference(StyleProperty,typeof(Button));CloseButton.Content="Close";
            Title="Create a Windows setup drive";StatusMessage.Text="Select the USB drive you intend to erase.";return;
        }
        if(Drives.SelectedItem is not UsbDisk disk || (prepared is null&&!File.Exists(sourceIso))){StatusMessage.Text="Choose a Windows image on the main page and select a USB drive.";return;}
        string label;string? saveTo=null;
        try
        {
            label=BootableDrive.VolumeLabel(UsbLabel.Text);
            if(build is not null && saveDestination is not null){saveTo=Path.GetFullPath(saveDestination);_ = IsoOutput.StagingPath(saveTo);}
            foreach(var path in new[]{sourceIso,saveTo}.Where(p=>p is not null))
                if(disk.Volumes.Contains(Path.GetPathRoot(path!)![..1],StringComparer.OrdinalIgnoreCase))throw new IOException("The USB drive cannot contain your source or saved ISO. Choose a different drive.");
        }
        catch(Exception ex){ShowFailure(ex,false);return;}
        if(saveTo is not null && File.Exists(saveTo) && (built is null || previousSave!=saveTo) && MessageBox.Show(this,"Replace this ISO after the customised image passes verification?\n\n"+saveTo,"Replace existing ISO",MessageBoxButton.OKCancel,MessageBoxImage.Warning)!=MessageBoxResult.OK)return;
        if(MessageBox.Show(this,"Erase and create a setup USB on this drive?\n\n"+disk.Label+"\n\nAll files and partitions on this USB will be deleted. The app will check the setup files first, then write automatically without asking again.","Confirm USB drive",MessageBoxButton.OKCancel,MessageBoxImage.Warning,MessageBoxResult.Cancel)!=MessageBoxResult.OK)return;
        transfer=null;TransferCaption.Visibility=Visibility.Collapsed;TransferProgress.Visibility=Visibility.Collapsed;
        failed=false;ResultHeading.Text="Creating your setup USB";ResultBanner.SetResourceReference(Border.BorderBrushProperty,"Line");operation=new();timing=new(DateTimeOffset.UtcNow);currentStep=0;Step(1);clock.Start();ElapsedCaption.Visibility=Visibility.Visible;UpdateClock();OptionsPanel.Visibility=Visibility.Collapsed;Start.IsEnabled=false;Stop.IsEnabled=true;Drives.IsEnabled=false;CloseButton.IsEnabled=false;Progress.Visibility=Visibility.Visible;
        UsbLabel.IsEnabled=false;
        string? work=null;var writingStarted=false;var finished=false;
        try
        {
            if(build is not null && (built is null || (built.MediaWorkspace is null?!File.Exists(built.Iso):!Directory.Exists(Path.Combine(built.MediaWorkspace,"media"))) || previousSave!=saveTo))
            {
                var owner=operation;StatusMessage.Text="Building your customisations. The USB has not been erased.";
                built=await build(saveTo,usbVolumes.Concat(disk.Volumes).ToArray(),disk.Size,operation.Token,new MeasuredBuildProgress(message=>BuildPhase(message,owner),Write));
                previousSave=saveTo;prepared=null;
            }
            var iso=built?.Iso??sourceIso!;
            if(prepared is not null && disk.Volumes.Contains(Path.GetPathRoot(prepared.Workspace)![..1],StringComparer.OrdinalIgnoreCase))throw new IOException("The USB target contains the prepared setup files. Choose a different drive.");
            work=prepared?.Workspace??built?.MediaWorkspace??WorkspaceStorage.NewPath("usb",checked(new FileInfo(iso).Length*4+(4L<<30)),usbVolumes.Concat(disk.Volumes));
            Step(StepCount-1);StatusMessage.Text="Preparing and checking the media. The USB drive has not been erased.";
            prepared??=await Task.Run(()=>built?.MediaWorkspace is not null?engine.PrepareBuiltMedia(work,disk,operation.Token,built.Iso,built.IsoSha256):engine.Prepare(iso,work,disk,operation.Token));
            operation.Token.ThrowIfCancellationRequested();
            Step(StepCount);StatusMessage.Text="Creating the confirmed USB drive. Keep it connected until verification finishes.";
            var writeOwner=operation;var writeProgress=new Progress<UsbTransferProgress>(value=>OnUsbTransfer(value,writeOwner));
            writingStarted=true;await Task.Run(()=>engine.Write(prepared,disk,$"ERASE {disk.Number}",operation.Token,label,writeProgress));finished=true;
            ResultHeading.Text="Your USB is ready";StatusMessage.Text="Setup drive files verified. Restart this PC and select the USB from its boot menu, or safely eject it before moving it to another PC. Installation still depends on the PC's CPU, drivers and the source ISO.";
        }
        catch(OperationCanceledException){ResultHeading.Text="USB creation stopped";StatusMessage.Text=writingStarted?"The USB may be incomplete. Recreate it before installing Windows.":"Stopped safely. Your USB has not been changed.";Write("Cancelled. Recovery files: "+work);}
        catch(Exception ex){if(work is not null)Write("Recovery files: "+work);ShowFailure(ex,writingStarted);}
        finally{clock.Stop();transfer=null;TransferCaption.Visibility=Visibility.Collapsed;TransferProgress.Visibility=Visibility.Collapsed;if(timing is not null)ElapsedCaption.Text=(finished?"Finished in ":"Elapsed ")+WorkProgress.Duration(timing.Elapsed(DateTimeOffset.UtcNow))+(finished?"":" · Stopped");OptionsPanel.Visibility=Visibility.Visible;operation.Dispose();operation=null;Start.IsEnabled=true;Stop.IsEnabled=false;Drives.IsEnabled=true;CloseButton.IsEnabled=true;Progress.Visibility=Visibility.Collapsed;UsbLabel.IsEnabled=true;}
        if(finished)ShowSuccess(label+" · "+disk.FriendlyName,timing!.Elapsed(DateTimeOffset.UtcNow));
    }
    internal void ShowSuccess(string drive,TimeSpan elapsed,bool notify=true)
    {
        Title="USB ready · TechLoungeOS Builder";SuccessDrive.Text=drive;SuccessTiming.Text="All files verified · Finished in "+WorkProgress.Duration(elapsed);
        foreach(FrameworkElement child in BootRoot.Children)child.Visibility=child==SuccessPanel?Visibility.Visible:Visibility.Collapsed;
        ActivityPresentation.Caption(SuccessPanel);if(IsActive)SuccessDone.Focus();
        if(notify)CompletionAttention.Notify(this);
        System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(SuccessHeading)?.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged);
    }
    private void ViewCompletedActivity(object sender,RoutedEventArgs e)
    {
        foreach(FrameworkElement child in BootRoot.Children)child.Visibility=child==SuccessPanel?Visibility.Collapsed:Visibility.Visible;
        viewingCompleted=true;
        OptionsPanel.Visibility=Visibility.Collapsed;Start.Content="Create another USB";CloseButton.Content="Done";
        CloseButton.SetResourceReference(StyleProperty,"Primary");Start.SetResourceReference(StyleProperty,typeof(Button));CloseButton.Focus();
    }
    private void CancelClick(object sender,RoutedEventArgs e){operation?.Cancel();Stop.IsEnabled=false;}
    private void CloseClick(object sender,RoutedEventArgs e){Close();}
}
