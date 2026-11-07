using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TechLoungeOS;

internal static class UsabilityChecks
{
    internal static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var window=new MainWindow(false);
        try
        {
            var usbEntry=(FrameworkElement?)window.FindName("ExistingIsoUsbEntry");
            var usbButton=(Button?)window.FindName("ExistingIsoUsbButton");
            Pump();
            Require(usbEntry?.Visibility==Visibility.Visible && usbButton is {IsEnabled:true,Focusable:true},"A finished ISO has a discoverable USB entry before importing an image");
            foreach(var size in new[]{new Size(720,500),new Size(1280,800)})
            {
                window.Width=size.Width;window.Height=size.Height;
                var surface=(FrameworkElement)window.Content;surface.Measure(size);surface.Arrange(new Rect(size));surface.UpdateLayout();Pump();
                var scroll=(ScrollViewer)window.FindName("SettingsScroll");scroll.ScrollToBottom();Pump();surface.UpdateLayout();
                var bounds=usbButton!.TransformToAncestor(scroll).TransformBounds(new Rect(new Point(),usbButton.RenderSize));
                Require(bounds.Height>=30 && bounds.Width>=120 && bounds.Top>=-1 && bounds.Bottom<=scroll.ViewportHeight+1,"Standalone USB action remains fully reachable at "+size);
            }
            Snapshot("standalone-usb.png");
            var sourceChoices=(ComboBox)window.FindName("Editions");
            sourceChoices.ItemsSource=new[]{new Edition(1,"Inert UI edition","9","10.0.26200.9457")};sourceChoices.SelectedIndex=0;Pump();
            Require(usbEntry!.Visibility==Visibility.Collapsed,"Imported customisation hides the standalone USB entry instead of duplicating Finish");
            sourceChoices.SelectedIndex=-1;Pump();
            Require(usbEntry.Visibility==Visibility.Visible,"Removing the edition restores the standalone USB entry");
            var status=(TextBlock)window.FindName("Status");
            status.Text="Found 6 Windows editions. Choose one to customise.";
            var entry=window.ActivityEntries[0];
            Require(entry.Title=="Windows image ready" && entry.Detail.Contains("6 Windows editions") && entry.State=="Done","Import result becomes a readable activity card");
            var count=window.ActivityEntries.Count;status.Text="Found 6 Windows editions. Choose one to customise.";
            Require(window.ActivityEntries.Count==count,"Repeated status does not duplicate activity entries");
            var running=typeof(MainWindow).GetField("running",BindingFlags.Instance|BindingFlags.NonPublic)!;
            running.SetValue(window,true);status.Text="Copying Windows installation media";
            status.Text="Preparing Microsoft Office";
            Require(window.ActivityEntries[0].State=="Working" && window.ActivityEntries[1].State=="Done","Completed build phases do not keep a stale working indicator");
            running.SetValue(window,false);
            status.Text="Build failed: "+new string('x',1200);
            Require(window.ActivityEntries[0].State=="Needs attention" && window.ActivityEntries[0].Detail.Length<=280,"Long failures remain readable and clearly marked");
            for(int i=0;i<65;i++)status.Text="Activity fixture "+i;
            Require(window.ActivityEntries.Count==40 && window.ActivityEntries[0].Detail=="Activity fixture 64","Activity history is bounded and newest first");
            Require(window.FindName("Log")==null && window.FindName("TechnicalLog")==null && window.FindName("SourceApps")==null,"Raw-text and source-app panels are absent from the normal workflow");
            foreach(var name in new[]{"OfficeCachePath","StorePackagesPath","Net8Compatibility"})Require(window.FindName(name)==null,"Advanced download control removed: "+name);
            var components=((Panel)window.FindName("Components")).Children.OfType<CheckBox>().ToArray();
            foreach(var id in new[]{"Printing-XPSServices-Features","WorkFolders-Client"})
            {
                var choice=components.Single(c=>((Component)c.Tag).Name==id);
                var lines=((StackPanel)choice.Content).Children.OfType<TextBlock>().ToArray();
                Require(lines.Length==2 && lines[1].Text.Length>40 && choice.IsChecked!=true,"Optional component has inline guidance without selecting removal: "+id);
            }
            Require(window.FindName("UsbButton")==null && window.FindName("ExportUsbButton")==null,"Duplicate sidebar and post-export USB actions remain absent");
            var output=(ComboBox)window.FindName("OutputMode");
            Require(output.Items.Count==3 && output.SelectedIndex==0 && ((FrameworkElement)window.FindName("IsoDestinationSection")).Visibility==Visibility.Collapsed,"USB-only defaults to no ISO destination input");
            output.SelectedIndex=1;Require(((TextBlock)window.FindName("BuildActionLabel")).Text=="Save ISO" && ((FrameworkElement)window.FindName("IsoDestinationSection")).Visibility==Visibility.Visible,"ISO-only exposes the single save destination and action");
            output.SelectedIndex=2;Require(((TextBlock)window.FindName("BuildActionLabel")).Text=="Choose USB drive","Combined output continues to the USB picker");
            output.SelectedIndex=0;
            var mode=(ComboBox)window.FindName("WallpaperMode");mode.SelectedIndex=2;
            Require(((FrameworkElement)window.FindName("WallpaperPresetControls")).Visibility==Visibility.Collapsed && ((FrameworkElement)window.FindName("CustomWallpaperControls")).Visibility==Visibility.Collapsed,"Original wallpaper hides irrelevant custom/preset controls");
            mode.SelectedIndex=1;Require(((FrameworkElement)window.FindName("CustomWallpaperControls")).Visibility==Visibility.Visible && ((FrameworkElement)window.FindName("WallpaperPresetControls")).Visibility==Visibility.Collapsed,"Custom mode has one image picker and no preset controls");
            mode.SelectedIndex=0;
            var source=new Edition(1,"Windows 11 Pro","9","10.0.26200.9457");var editions=(ComboBox)window.FindName("Editions");editions.ItemsSource=new[]{source};editions.SelectedIndex=0;
            var current=new WindowsReleaseCatalog.Release("25H2",new Version(source.Version),new DateOnly(2026,9,14),"2026-09 OOB","KB5129195");
            var latest=new WindowsReleaseCatalog.Release("25H2",new Version(10,0,26200,9725),new DateOnly(2026,10,26),"2026-10 OOB","KB5134133");
            var feature=new WindowsReleaseCatalog.Release("26H2",new Version(10,0,26300,9457),new DateOnly(2026,9,29),"GA","");
            var task=window.CheckWindowsVersionDetails((_,_)=>Task.FromResult(new WindowsReleaseCatalog.VersionReport(latest,current,feature,new DateTimeOffset(2026,11,1,18,0,0,TimeSpan.Zero))));
            while(!task.IsCompleted)Pump();task.GetAwaiter().GetResult();
            Require(((TextBlock)window.FindName("WindowsCurrentName")).Text=="Windows 11 25H2" && ((TextBlock)window.FindName("WindowsCurrentDetails")).Text.Contains("14 September 2026"),"Current release name and exact published build date are displayed");
            Require(((TextBlock)window.FindName("WindowsLatestDetails")).Text.Contains("26 October 2026") && ((TextBlock)window.FindName("WindowsNewRelease")).Text.Contains("26H2") && ((TextBlock)window.FindName("WindowsVersionChecked")).Text.Contains("1 Nov 2026"),"Compatible update date, newer release and check timestamp stay distinct");
            var offer=new WindowsUpgradeRules.Offer("win11-26h2-x64",latest,latest with{Name="26H2",Version=new Version(10,0,26300,9725)});
            task=window.CheckWindowsVersionDetails((_,_)=>Task.FromResult(new WindowsReleaseCatalog.VersionReport(latest,current,feature,new DateTimeOffset(2026,11,1,18,0,0,TimeSpan.Zero),offer)));
            while(!task.IsCompleted)Pump();task.GetAwaiter().GetResult();
            Require(((TextBlock)window.FindName("WindowsLatestName")).Text=="Windows 11 26H2" && ((TextBlock)window.FindName("WindowsVersionStatus")).Text.Contains("enablement"),"Eligible 25H2 image offers 26H2 with explicit preservation checks");
            editions.ItemsSource=new[]{source with{EditionId="IoTEnterpriseS"}};editions.SelectedIndex=0;
            task=window.CheckWindowsVersionDetails((_,_)=>Task.FromResult(new WindowsReleaseCatalog.VersionReport(latest,current,feature,new DateTimeOffset(2026,11,1,18,0,0,TimeSpan.Zero),offer)));
            while(!task.IsCompleted)Pump();task.GetAwaiter().GetResult();
            Require(((TextBlock)window.FindName("WindowsLatestName")).Text=="Windows 11 25H2","LTSC metadata remains on its own servicing release");
            editions.ItemsSource=new[]{source};editions.SelectedIndex=0;
            task=window.CheckWindowsVersionDetails((_,_)=>Task.FromResult(new WindowsReleaseCatalog.VersionReport(latest,current,feature,new DateTimeOffset(2026,11,1,18,0,0,TimeSpan.Zero),offer)));
            while(!task.IsCompleted)Pump();task.GetAwaiter().GetResult();
            ((RadioButton)window.FindName("SourceStep")).IsChecked=true;((Expander)window.FindName("WindowsUpdatesSection")).IsExpanded=true;
            ((Expander)window.FindName("BuildActivity")).IsExpanded=true;Snapshot("activity-and-versions.png");
            ((Expander)window.FindName("BuildActivity")).IsExpanded=false;((RadioButton)window.FindName("PersonalisationStep")).IsChecked=true;Snapshot("wallpaper-and-region.png");
            ((RadioButton)window.FindName("ExportStep")).IsChecked=true;Snapshot("single-finish-flow.png");
            CopyChecks.Run(window);
        }
        finally{window.Close();}
        Console.WriteLine("Usability fixtures/render checks passed. No ISO servicing, physical USB writing or VM execution.");

        void Snapshot(string name)
        {
            var size=new Size(1160,860);window.Width=size.Width;window.Height=size.Height;
            var content=(FrameworkElement)window.Content;content.Measure(size);content.Arrange(new Rect(size));content.UpdateLayout();Pump();content.UpdateLayout();
            var bitmap=new RenderTargetBitmap(1160,860,96,96,PixelFormats.Pbgra32);bitmap.Render(content);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(folder,name));encoder.Save(file);
        }
    }
    private static void Require(bool value,string message){if(!value)throw new IOException(message);Console.WriteLine("PASS: "+message);}
    private static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(180)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
}
