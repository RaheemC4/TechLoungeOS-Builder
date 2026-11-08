using System.Windows;
using System.Windows.Controls;

namespace TechLoungeOS;

public partial class MainWindow
{
    private string? completedOfficeCache;
    internal static string? ReusableOfficeCache(string? folder,IEnumerable<string>? excludedVolumes)
    {
        if(folder is null)return null;
        try
        {
            if(!Path.IsPathFullyQualified(folder)||folder.StartsWith(@"\\",StringComparison.Ordinal)||
                (excludedVolumes?.Contains(Path.GetPathRoot(folder)![..1],StringComparer.OrdinalIgnoreCase)??false))return null;
            Policy.RequireUnlinkedPath(folder);OfficePack.ValidateFiles(folder);
            return File.Exists(Path.Combine(folder,"TechLoungeOS-Office.sha256.json"))?folder:null;
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException){return null;}
    }
    private int OutputModeIndex=>IsoOutputChoice?.IsChecked==true?1:BothOutput?.IsChecked==true?2:0;
    private void OutputModeChanged(object sender,RoutedEventArgs e)
    {
        RefreshOutputChoice();
        if(sender is RadioButton choice && choice.IsLoaded && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
            choice.BeginAnimation(OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(.7,1,TimeSpan.FromMilliseconds(150)){FillBehavior=System.Windows.Media.Animation.FillBehavior.Stop});
    }
    private void OutputHoverChanged(object sender,System.Windows.Input.MouseEventArgs e)
    {
        if(sender is not RadioButton choice || choice.Template.FindName("Hover",choice) is not Border hover)return;
        var from=hover.Opacity;var target=choice.IsMouseOver?.045:0;hover.BeginAnimation(OpacityProperty,null);hover.Opacity=target;
        if(SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
            hover.BeginAnimation(OpacityProperty,new System.Windows.Media.Animation.DoubleAnimation(from,target,TimeSpan.FromMilliseconds(120)){FillBehavior=System.Windows.Media.Animation.FillBehavior.Stop});
    }
    private void RefreshOutputChoice()
    {
        if(BuildActionLabel is null || IsoDestinationSection is null)return;
        var isoOnly=OutputModeIndex==1;
        IsoDestinationSection.Visibility=OutputModeIndex==0?Visibility.Collapsed:Visibility.Visible;
        OutputNextHint.Text=isoOnly?"Save a verified ISO at your chosen location.":"Next, choose your USB drive and label. Erasing the drive requires a separate confirmation.";
        BuildActionLabel.Text=isoOnly?"Save ISO":"Choose USB drive";BuildActionIcon.Text=isoOnly?"\uE8A5":"\uE88E";
        System.Windows.Automation.AutomationProperties.SetName(BuildButton,isoOnly?"Save customised ISO":"Create setup USB");
    }
    private string BuildChoiceStamp()=>System.Text.Json.JsonSerializer.Serialize(new{
        Source=IsoPath.Text,Edition=(Editions.SelectedItem as Edition)?.ImageIndex,
        SourceLength=File.Exists(IsoPath.Text)?new FileInfo(IsoPath.Text).Length:0,
        SourceWritten=File.Exists(IsoPath.Text)?File.GetLastWriteTimeUtc(IsoPath.Text):DateTime.MinValue,
        Apps=selectedApps.Select(a=>a.Id).ToArray(),Remove=Components.Children.Cast<CheckBox>().Where(c=>c.IsChecked==true).Select(c=>(Component)c.Tag).ToArray(),
        WallpaperMode=WallpaperMode.SelectedIndex,WallpaperPath=WallpaperPath.Text,Preset=WallpaperMode.SelectedIndex==0?SelectedWallpaperPreset():null,
        Region=SetupRegionChoice.Choice?.Id,Keyboard=SetupKeyboardChoice.Choice?.Id,
        UpdateWindows=UpdateWindowsImage.IsChecked});
    private BuildOptions ReadBuildOptions(string output,IEnumerable<string>? excludedVolumes=null)
    {
        if(Editions.SelectedItem is not Edition edition)throw new IOException("Import an ISO and select a Windows edition first.");
        if(WallpaperMode.SelectedIndex==1 && string.IsNullOrWhiteSpace(WallpaperPath.Text))throw new IOException("Choose a background image, a preset, or Keep source background.");
        if(!Directory.Exists(BundledTweaksFolder))throw new IOException("Required support files are missing. Keep the complete Builder folder together, including its integration folder.");
        var work=WorkspaceStorage.NewPath("build",checked(new FileInfo(IsoPath.Text).Length*5+(40L<<30)+(UpdateWindowsImage.IsChecked==true?40L<<30:0)),excludedVolumes);
        var office=ReusableOfficeCache(completedOfficeCache,excludedVolumes);
        return new(IsoPath.Text,edition.ImageIndex,work,output,IsoTool.CachePath,
            Components.Children.Cast<CheckBox>().Where(c=>c.IsChecked==true).Select(c=>(Component)c.Tag).ToArray(),selectedApps.Select(a=>a.Id).ToArray(),true,true,BundledTweaksFolder)
        {
            WallpaperFile=WallpaperMode.SelectedIndex==1?WallpaperPath.Text:null,
            WallpaperPreset=WallpaperMode.SelectedIndex==0?SelectedWallpaperPreset():null,
            UseCurrentTweaksInstaller=true,IncludeAmdIntelGraphics=true,IncludeNvidiaSetup=true,Target16GbUsb=true,
            IncludeRuntimePack=true,IncludeNet8=true,IncludeStoreXbox=true,IncludeOffice=true,AnimatedWinPe=true,
            StoreXboxPackages=null,
            // ODT still resolves Current and verifies/refreshes the private copy.
            OfficeSource=office,
            UpdateWindows=UpdateWindowsImage.IsChecked==true,
            SetupRegion=SetupRegionChoice.Choice?.Id,SetupKeyboard=SetupKeyboardChoice.Choice?.Id
        };
    }
    private async Task<UsbBuildResult> BuildForUsb(string? saveTo,string[] excludedVolumes,long usbTargetBytes,CancellationToken ct,IProgress<string> progress)
    {
        var staged=saveTo is null?"":IsoOutput.StagingPath(saveTo);
        var options=ReadBuildOptions(staged,excludedVolumes) with {
            UsbTargetBytes=usbTargetBytes,
            // Saved ISOs retain the conservative target. USB-only builds use the
            // selected drive's real capacity, without dropping any payloads.
            Target16GbUsb=saveTo is not null || usbTargetBytes<=16_000_000_000L
        };
        if(saveTo is null)
        {
            options=options with{ExportIso=false,Output=Path.Combine(Path.GetDirectoryName(options.Workspace)!,".tlos-"+Guid.NewGuid().ToString("N")+".iso")};
            await RunBuildCore(options,ct,progress);
            return new(null,options.Workspace);
        }
        return await BuildOutput(options,saveTo,ct,progress);
    }
    private async Task<UsbBuildResult> BuildOutput(BuildOptions options,string saveTo,CancellationToken ct,IProgress<string>? progress=null)
    {
        SetBusy(true);
        IsoOutput.Destination destination;
        try{destination=await Task.Run(()=>IsoOutput.Capture(saveTo),ct);}
        finally{SetBusy(false);}
        await RunBuildCore(options,ct,progress);
        SetBusy(true);Status.Text="Saving your verified ISO…";progress?.Report(Status.Text);
        try
        {
            // A cancellation never interrupts atomic replacement of a verified image.
            ct.ThrowIfCancellationRequested();
            var exportedHash=await Task.Run(()=>IsoOutput.Commit(options.Output,destination));
            OutputPath.Text=destination.Path;
            Status.Text="Your customised ISO is ready.";
            if(destination.Path.Equals(Path.GetFullPath(options.Iso),StringComparison.OrdinalIgnoreCase))
            {
                // The selected-edition index may change when multi-edition media is replaced.
                Editions.ItemsSource=null;Editions.IsEnabled=false;
                var editions=await new BuildEngine(Write).Import(destination.Path);
                Editions.ItemsSource=editions;Editions.IsEnabled=editions.Length>0;Editions.SelectedIndex=0;
            }
            ShowExportReady(destination.Path,options.Workspace,exportedHash);
            return new(destination.Path,options.Workspace,exportedHash);
        }
        finally{SetBusy(false);}
    }
}
