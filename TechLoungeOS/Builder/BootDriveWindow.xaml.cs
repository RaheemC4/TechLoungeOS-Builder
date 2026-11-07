using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace TechLoungeOS;
public partial class BootDriveWindow : Window
{
    private CancellationTokenSource? operation;
    private readonly BootableDrive engine;
    private readonly string? sourceIso;
    private readonly Func<string?,string[],CancellationToken,IProgress<string>,Task<UsbBuildResult>>? build;
    private UsbBuildResult? built;
    private PreparedUsb? prepared;
    private string? previousSave;
    private readonly string? saveDestination;
    private string[] usbVolumes=[];
    private readonly System.Collections.ObjectModel.ObservableCollection<ActivityEntry> activity=[];
    private readonly System.Text.StringBuilder diagnostics=new();
    private readonly object diagnosticGate=new();
    private bool failed;
    private WorkProgress? timing;
    private readonly System.Windows.Threading.DispatcherTimer clock=new(){Interval=TimeSpan.FromSeconds(1)};
    private int currentStep;
    private int StepCount=>build is null?3:7;
    private void UpdateClock()
    {
        if(timing is null)return;
        ElapsedCaption.Text=WorkProgress.Caption(timing,DateTimeOffset.UtcNow);
        ActivityPresentation.Caption(ElapsedCaption,true);
    }
    private void Step(int value)
    {
        if(value==currentStep)return;currentStep=value;
        var labels=build is null?new[]{"Check setup files","Confirm the USB drive","Write and verify USB"}:new[]{"Prepare build","Copy Windows files","Customise Windows","Create and verify ISO","Check USB files","Confirm the USB drive","Write and verify USB"};
        StepCaption.Text=$"Step {value} of {StepCount} · {labels[value-1]}";StepCaption.Visibility=Visibility.Visible;ActivityPresentation.Caption(StepCaption);
    }
    private void BuildPhase(string message,CancellationTokenSource owner)
    {
        if(operation!=owner||failed||owner.IsCancellationRequested||currentStep>4)return;
        Step(Math.Max(currentStep,WorkProgress.BuildStep(message)));StatusMessage.Text=message;
    }
    public BootDriveWindow(string? iso):this(iso,null,null){}
    internal BootDriveWindow(string? iso,Func<string?,string[],CancellationToken,IProgress<string>,Task<UsbBuildResult>>? build,string? saveTo,bool enumerateDrives=true)
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
        failed=true;clock.Stop();Progress.Visibility=Visibility.Collapsed;ResultHeading.Text="USB was not created";
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
        if(Drives.SelectedItem is not UsbDisk disk || !File.Exists(sourceIso)){StatusMessage.Text="Choose a Windows image on the main page and select a USB drive.";return;}
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
        failed=false;ResultHeading.Text="Creating your setup USB";ResultBanner.SetResourceReference(Border.BorderBrushProperty,"Line");operation=new();timing=new(DateTimeOffset.UtcNow);currentStep=0;Step(1);clock.Start();ElapsedCaption.Visibility=Visibility.Visible;UpdateClock();OptionsPanel.Visibility=Visibility.Collapsed;Start.IsEnabled=false;Stop.IsEnabled=true;Drives.IsEnabled=false;CloseButton.IsEnabled=false;Progress.Visibility=Visibility.Visible;
        UsbLabel.IsEnabled=false;
        string? work=null;var writingStarted=false;var finished=false;
        try
        {
            if(build is not null && (built is null || !File.Exists(built.Iso) || previousSave!=saveTo))
            {
                var owner=operation;StatusMessage.Text="Building your customisations. The USB has not been erased.";
                built=await build(saveTo,usbVolumes.Concat(disk.Volumes).ToArray(),operation.Token,new MeasuredBuildProgress(message=>BuildPhase(message,owner),line=>timing?.Observe(line,DateTimeOffset.UtcNow)));
                previousSave=saveTo;prepared=null;
            }
            var iso=built?.Iso??sourceIso!;
            if(prepared is not null && disk.Volumes.Contains(Path.GetPathRoot(prepared.Workspace)![..1],StringComparer.OrdinalIgnoreCase))prepared=null;
            work=prepared?.Workspace??WorkspaceStorage.NewPath("usb",checked(new FileInfo(iso).Length*4+(4L<<30)),usbVolumes.Concat(disk.Volumes));
            Step(build is null?1:5);StatusMessage.Text="Preparing and checking the media. The USB drive has not been erased.";
            prepared??=await Task.Run(()=>engine.Prepare(iso,work,disk,operation.Token));
            operation.Token.ThrowIfCancellationRequested();
            Step(build is null?2:6);StatusMessage.Text="Files checked. Confirm the USB drive before writing.";
            var confirm=new Window{Owner=this,Title="Review USB erase",Width=580,Height=350,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,Background=Background,Foreground=Foreground,FontFamily=FontFamily};
            var content=new StackPanel{Margin=new Thickness(24)};confirm.Content=content;
            content.Children.Add(new TextBlock{Text="Erase this USB drive?",FontSize=24});
            content.Children.Add(new TextBlock{Text=disk.Label+"\nVolumes: "+string.Join(", ",disk.Volumes)+"\n\nAll files and partitions on this drive will be deleted.\nType ERASE "+disk.Number+" to continue.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,14)});
            var input=new TextBox();System.Windows.Automation.AutomationProperties.SetName(input,"Type the exact erase confirmation");content.Children.Add(input);
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0)};
            var erase=new Button{Content="Erase and create",IsEnabled=false};erase.SetResourceReference(StyleProperty,"Primary");
            var cancel=new Button{Content="Keep drive",IsCancel=true};input.TextChanged+=(_,_)=>erase.IsEnabled=input.Text==$"ERASE {disk.Number}";
            erase.Click+=(_,_)=>confirm.DialogResult=true;buttons.Children.Add(erase);buttons.Children.Add(cancel);content.Children.Add(buttons);
            if(confirm.ShowDialog()!=true){StatusMessage.Text="Drive unchanged. Your prepared files are kept for retry.";Write("Prepared files: "+work);return;}
            Step(StepCount);StatusMessage.Text="Creating the confirmed USB drive. Keep it connected until verification finishes.";
            var typed=input.Text;
            writingStarted=true;await Task.Run(()=>engine.Write(prepared,disk,typed,operation.Token,label));finished=true;
            if(built is {Temporary:true})
            {
                try{await Task.Run(()=>IsoOutput.RemoveTemporary(built.Iso,prepared.IsoSha256));built=null;prepared=null;}
                catch(Exception cleanup) when(cleanup is IOException or UnauthorizedAccessException)
                {
                    StatusMessage.Text="Your USB is ready and verified. Temporary ISO cleanup needs attention: "+cleanup.Message;Write(StatusMessage.Text);return;
                }
            }
            ResultHeading.Text="Your USB is ready";StatusMessage.Text="Setup drive files verified. Use Safely Remove Hardware, then choose this USB in the other PC's UEFI or BIOS/CSM boot menu. Installation still depends on that PC's CPU, drivers and the source ISO.";
        }
        catch(OperationCanceledException){ResultHeading.Text="USB creation stopped";StatusMessage.Text=writingStarted?"The USB may be incomplete. Recreate it before installing Windows.":"Stopped safely. Your USB has not been changed.";Write("Cancelled. Recovery files: "+work);}
        catch(Exception ex){Write("Recovery files: "+work);ShowFailure(ex,writingStarted);}
        finally{clock.Stop();if(timing is not null)ElapsedCaption.Text=(finished?"Finished in ":"Elapsed ")+WorkProgress.Duration(timing.Elapsed(DateTimeOffset.UtcNow))+(finished?"":" · Stopped");OptionsPanel.Visibility=Visibility.Visible;operation.Dispose();operation=null;Start.IsEnabled=true;Stop.IsEnabled=false;Drives.IsEnabled=true;CloseButton.IsEnabled=true;Progress.Visibility=Visibility.Collapsed;UsbLabel.IsEnabled=true;}
    }
    private void CancelClick(object sender,RoutedEventArgs e){operation?.Cancel();Stop.IsEnabled=false;}
    private void CloseClick(object sender,RoutedEventArgs e){Close();}
}
