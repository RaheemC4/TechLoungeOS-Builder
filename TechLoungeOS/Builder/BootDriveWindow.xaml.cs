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
    public BootDriveWindow(string? iso):this(iso,null,null){}
    internal BootDriveWindow(string? iso,Func<string?,string[],CancellationToken,IProgress<string>,Task<UsbBuildResult>>? build,string? saveTo)
    {
        InitializeComponent();sourceIso=iso;this.build=build;Source.Text=File.Exists(iso)?Path.GetFileName(iso):"Choose a Windows image on the main page.";Source.ToolTip=iso;
        saveDestination=saveTo;
        if(saveTo is not null){IsoSaveSummary.Text="ISO copy: "+saveTo;IsoSaveSummary.Visibility=Visibility.Visible;}
        if(build is null)SourceHint.Text="Your completed ISO will be copied and verified on the USB drive.";

        var area=SystemParameters.WorkArea;Width=Math.Min(Width,area.Width);Height=Math.Min(Height,area.Height);MinWidth=Math.Min(MinWidth,Width);MinHeight=Math.Min(MinHeight,Height);
        engine=new(new ProcessRunner(Write),Write);
        Loaded+=async (_,_)=>await Refresh();
        SourceInitialized+=(_,_)=>WindowsChrome.Apply(this);
        Closing+=(_,e)=>{if(operation!=null){e.Cancel=true;operation.Cancel();Message.Text="Stopping after the current operation. The drive may contain incomplete setup files.";}};
    }
    private void Write(string message)=>Dispatcher.BeginInvoke(new Action(()=>{Activity.AppendText(message+Environment.NewLine);if(Activity.Text.Length>30000)Activity.Text=Activity.Text[^20000..];Activity.ScrollToEnd();}));
    private async Task Refresh()
    {
        try
        {
            Start.IsEnabled=false;
            var disks=await Task.Run(()=>engine.List());
            usbVolumes=disks.Where(d=>d.BusType==7).SelectMany(d=>d.Volumes).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            Drives.ItemsSource=disks.Where(d=>d.BusType==7 && BootableDrive.Rejection(d)==null).ToArray();
            Drives.SelectedIndex=-1;
            Message.Text=Drives.Items.Count==0?"No eligible USB drives. Connect a USB drive that does not contain Windows, the app or its working files.":"Select the USB drive you intend to erase.";
        }
        catch(Exception ex){Message.Text="Could not read drives: "+ex.Message;}
        finally{Start.IsEnabled=true;}
    }
    private async void RefreshClick(object sender,RoutedEventArgs e){if(operation==null)await Refresh();}
    private async void StartClick(object sender,RoutedEventArgs e)
    {
        if(operation!=null)return;
        if(Drives.SelectedItem is not UsbDisk disk || !File.Exists(sourceIso)){Message.Text="Choose a Windows image on the main page and select a USB drive.";return;}
        string label;string? saveTo=null;
        try
        {
            label=BootableDrive.VolumeLabel(UsbLabel.Text);
            if(build is not null && saveDestination is not null){saveTo=Path.GetFullPath(saveDestination);_ = IsoOutput.StagingPath(saveTo);}
            foreach(var path in new[]{sourceIso,saveTo}.Where(p=>p is not null))
                if(disk.Volumes.Contains(Path.GetPathRoot(path!)![..1],StringComparer.OrdinalIgnoreCase))throw new IOException("The USB drive cannot contain your source or saved ISO. Choose a different drive.");
        }
        catch(Exception ex){Message.Text=ex.Message;return;}
        if(saveTo is not null && File.Exists(saveTo) && (built is null || previousSave!=saveTo) && MessageBox.Show(this,"Replace this ISO after the customised image passes verification?\n\n"+saveTo,"Replace existing ISO",MessageBoxButton.OKCancel,MessageBoxImage.Warning)!=MessageBoxResult.OK)return;
        operation=new();Start.IsEnabled=false;Stop.IsEnabled=true;Drives.IsEnabled=false;CloseButton.IsEnabled=false;Progress.Visibility=Visibility.Visible;
        UsbLabel.IsEnabled=false;
        string? work=null;
        try
        {
            if(build is not null && (built is null || !File.Exists(built.Iso) || previousSave!=saveTo))
            {
                Message.Text="Building your customisations. The USB has not been erased.";
                built=await build(saveTo,usbVolumes.Concat(disk.Volumes).ToArray(),operation.Token,new Progress<string>(message=>Message.Text=message));
                previousSave=saveTo;prepared=null;
            }
            var iso=built?.Iso??sourceIso!;
            if(prepared is not null && disk.Volumes.Contains(Path.GetPathRoot(prepared.Workspace)![..1],StringComparer.OrdinalIgnoreCase))prepared=null;
            work=prepared?.Workspace??WorkspaceStorage.NewPath("usb",checked(new FileInfo(iso).Length*4+(4L<<30)),usbVolumes.Concat(disk.Volumes));
            Message.Text="Preparing and checking the media. The USB drive has not been erased.";
            prepared??=await Task.Run(()=>engine.Prepare(iso,work,disk,operation.Token));
            operation.Token.ThrowIfCancellationRequested();
            var confirm=new Window{Owner=this,Title="Review USB erase",Width=580,Height=350,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,Background=Background,Foreground=Foreground,FontFamily=FontFamily};
            var content=new StackPanel{Margin=new Thickness(24)};confirm.Content=content;
            content.Children.Add(new TextBlock{Text="Erase this USB drive?",FontSize=24});
            content.Children.Add(new TextBlock{Text=disk.Label+"\nVolumes: "+string.Join(", ",disk.Volumes)+"\n\nAll files and partitions on this drive will be deleted.\nType ERASE "+disk.Number+" to continue.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,14,0,14)});
            var input=new TextBox();System.Windows.Automation.AutomationProperties.SetName(input,"Type the exact erase confirmation");content.Children.Add(input);
            var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0)};
            var erase=new Button{Content="Erase and create",IsEnabled=false};erase.SetResourceReference(StyleProperty,"Primary");
            var cancel=new Button{Content="Keep drive",IsCancel=true};input.TextChanged+=(_,_)=>erase.IsEnabled=input.Text==$"ERASE {disk.Number}";
            erase.Click+=(_,_)=>confirm.DialogResult=true;buttons.Children.Add(erase);buttons.Children.Add(cancel);content.Children.Add(buttons);
            if(confirm.ShowDialog()!=true){Message.Text="Drive unchanged. Prepared files and logs: "+work;return;}
            Message.Text="Creating the confirmed USB drive. Keep it connected until verification finishes.";
            var typed=input.Text;
            await Task.Run(()=>engine.Write(prepared,disk,typed,operation.Token,label));
            if(built is {Temporary:true})
            {
                try{await Task.Run(()=>IsoOutput.RemoveTemporary(built.Iso,prepared.IsoSha256));built=null;prepared=null;}
                catch(Exception cleanup) when(cleanup is IOException or UnauthorizedAccessException)
                {
                    Message.Text="Your USB is ready and verified. Temporary ISO cleanup needs attention: "+cleanup.Message;Write(Message.Text);return;
                }
            }
            Message.Text="Setup drive files verified. Use Safely Remove Hardware, then choose this USB in the other PC's UEFI or BIOS/CSM boot menu. Installation still depends on that PC's CPU, drivers and the source ISO.";
        }
        catch(OperationCanceledException){Message.Text="Stopped. If writing had started, this USB is incomplete. Recreate it before installation. Details: "+work;}
        catch(Exception ex){Message.Text="USB creation stopped: "+ex.Message+" Details: "+work;Write(ex.ToString());}
        finally{operation.Dispose();operation=null;Start.IsEnabled=true;Stop.IsEnabled=false;Drives.IsEnabled=true;CloseButton.IsEnabled=true;Progress.Visibility=Visibility.Collapsed;UsbLabel.IsEnabled=true;}
    }
    private void CancelClick(object sender,RoutedEventArgs e){operation?.Cancel();Stop.IsEnabled=false;}
    private void CloseClick(object sender,RoutedEventArgs e){Close();}
}
