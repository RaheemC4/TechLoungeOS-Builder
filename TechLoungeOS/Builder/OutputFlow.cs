using System.Windows;
using System.Windows.Controls;

namespace TechLoungeOS;

public partial class MainWindow
{
    private void OutputModeChanged(object sender,SelectionChangedEventArgs e)=>RefreshOutputChoice();
    private void RefreshOutputChoice()
    {
        if(BuildActionLabel is null || IsoDestinationSection is null)return;
        var isoOnly=OutputMode.SelectedIndex==1;
        IsoDestinationSection.Visibility=OutputMode.SelectedIndex==0?Visibility.Collapsed:Visibility.Visible;
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
        return new(IsoPath.Text,edition.ImageIndex,work,output,OscdimgPath.Text,
            Components.Children.Cast<CheckBox>().Where(c=>c.IsChecked==true).Select(c=>(Component)c.Tag).ToArray(),selectedApps.Select(a=>a.Id).ToArray(),true,true,BundledTweaksFolder)
        {
            WallpaperFile=WallpaperMode.SelectedIndex==1?WallpaperPath.Text:null,
            WallpaperPreset=WallpaperMode.SelectedIndex==0?SelectedWallpaperPreset():null,
            UseCurrentTweaksInstaller=true,IncludeAmdIntelGraphics=true,IncludeNvidiaSetup=true,Target16GbUsb=true,
            IncludeRuntimePack=true,IncludeNet8=true,IncludeStoreXbox=true,IncludeOffice=true,AnimatedWinPe=true,
            StoreXboxPackages=null,
            OfficeSource=null,
            UpdateWindows=UpdateWindowsImage.IsChecked==true,
            SetupRegion=SetupRegionChoice.Choice?.Id,SetupKeyboard=SetupKeyboardChoice.Choice?.Id
        };
    }
    private async Task<UsbBuildResult> BuildForUsb(string? saveTo,string[] excludedVolumes,CancellationToken ct,IProgress<string> progress)
    {
        var staged=saveTo is null?"":IsoOutput.StagingPath(saveTo);
        var options=ReadBuildOptions(staged,excludedVolumes);
        if(saveTo is null)options=options with{Output=Path.Combine(Path.GetDirectoryName(options.Workspace)!,".tlos-"+Guid.NewGuid().ToString("N")+".iso")};
        return await BuildOutput(options,saveTo,ct,progress);
    }
    private async Task<UsbBuildResult> BuildOutput(BuildOptions options,string? saveTo,CancellationToken ct,IProgress<string>? progress=null)
    {
        IsoOutput.Destination? destination=null;
        if(saveTo is not null)
        {
            SetBusy(true);
            try{destination=await Task.Run(()=>IsoOutput.Capture(saveTo),ct);}
            finally{SetBusy(false);}
        }
        if(!await RunBuildCore(options,ct,progress))
        {
            ct.ThrowIfCancellationRequested();throw new IOException(Status.Text);
        }
        if(destination is null)return new(options.Output,true);
        SetBusy(true);Status.Text="Saving your verified ISO…";progress?.Report(Status.Text);
        try
        {
            // A cancellation never interrupts atomic replacement of a verified image.
            ct.ThrowIfCancellationRequested();
            await Task.Run(()=>IsoOutput.Commit(options.Output,destination));
            OutputPath.Text=destination.Path;
            Status.Text="Your customised ISO is ready.";
            if(destination.Path.Equals(Path.GetFullPath(options.Iso),StringComparison.OrdinalIgnoreCase))
            {
                // The selected-edition index may change when multi-edition media is replaced.
                Editions.ItemsSource=null;Editions.IsEnabled=false;
                var editions=await new BuildEngine(Write).Import(destination.Path);
                Editions.ItemsSource=editions;Editions.IsEnabled=editions.Length>0;Editions.SelectedIndex=0;
            }
            ShowExportReady(destination.Path);
            return new(destination.Path,false);
        }
        finally{SetBusy(false);}
    }
}
