using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TechLoungeOS;

internal static class ReleaseVideoFrames
{
    // Captures the actual compiled controls, not a separately drawn interface.
    internal static void Run(string output,string catalogueFile,string? importedFile=null,bool wide=false,string? searchSnapshot=null)
    {
        Directory.CreateDirectory(output);
        var window=new MainWindow(false);
        try
        {
            Render(window,"source.png");
            if(importedFile is not null)
            {
                using var captured=JsonDocument.Parse(File.ReadAllBytes(importedFile));
                var editions=captured.RootElement.GetProperty("editions").Deserialize<Edition[]>()??throw new IOException("Missing observed editions");
                ((TextBlock)window.FindName("IsoPath")).Text=captured.RootElement.GetProperty("path").GetString();
                ((ComboBox)window.FindName("Editions")).ItemsSource=editions;((ComboBox)window.FindName("Editions")).SelectedIndex=0;
                ((TextBlock)window.FindName("Status")).Text=$"Found {editions.Length} Windows editions. Choose one to customise.";
            }
            ((RadioButton)window.FindName("ComponentsStep")).IsChecked=true;
            Render(window,"components.png");
            if(wide){
                typeof(MainWindow).GetMethod("PresetClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(window,[window,new RoutedEventArgs()]);
                Render(window,"components-selected.png");
            }
            ((RadioButton)window.FindName("AppsStep")).IsChecked=true;
            Render(window,"apps-before.png");
            ((System.Windows.Controls.Primitives.ToggleButton)window.FindName("RecommendedPresetButton")).IsChecked=true;
            Render(window,"apps.png");
            if(wide && searchSnapshot is null){
                var rows=(Panel)window.FindName("Apps");
                rows.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>().Single(r=>((CatalogApp)r.Tag).Id=="Brave.Brave").IsChecked=false;
                Render(window,"apps-minus.png");
                ((System.Windows.Controls.Primitives.ToggleButton)window.FindName("RecommendedPresetButton")).IsChecked=true;
            }
            if(searchSnapshot is not null)
            {
                var results=AppCatalog.Parse(File.ReadAllText(searchSnapshot));
                if(results.Length!=1||results[0].Id!="OBSProject.OBSStudio")throw new IOException("Expected the observed exact OBS Studio result.");
                var query=(TextBox)window.FindName("AppQuery");
                for(int length=1;length<="OBS Studio".Length;length++){query.Text="OBS Studio"[..length];Render(window,$"search-{length:00}.png");}
                var search=window.SearchApps(false,(_,_)=>Task.FromResult(results));
                while(!search.IsCompleted)Pump();search.GetAwaiter().GetResult();
                var result=((Panel)window.FindName("SearchResults")).Children.OfType<AppChoice>().Single();
                var deadline=DateTime.UtcNow.AddSeconds(10);
                while(!Descendants(result).OfType<Image>().Any(i=>i.Source!=null)&&DateTime.UtcNow<deadline)Pump();
                var popup=(System.Windows.Controls.Primitives.Popup)window.FindName("AppPickerPopup");
                var surface=popup.Child;popup.Child=null;
                var picker=new Window{Content=surface,ShowInTaskbar=false,ShowActivated=false};
                try{Render(picker,"search-picker.png",640,220);result.IsChecked=true;Render(picker,"search-picker-selected.png",640,220);}
                finally{picker.Content=null;popup.Child=surface;picker.Close();}
                Render(window,"apps-with-obs.png");
                if(!((Panel)window.FindName("Apps")).Children.OfType<AppChoice>().Any(c=>((CatalogApp)c.Tag).Id==results[0].Id&&c.IsChecked==true))throw new IOException("Search selection did not reach the main app list.");
                query.Text="";
                CaptureUsb();
            }
            ((RadioButton)window.FindName("PersonalisationStep")).IsChecked=true;
            if(wide && ((Image)window.FindName("WallpaperPreview")).Source==null)WaitWallpaper(null);
            Render(window,"desktop.png");
            if(wide){
                var colour=(ComboBox)window.FindName("WallpaperColour");
                var image=(Image)window.FindName("WallpaperPreview");
                var previous=image.Source;colour.SelectedIndex=1;WaitWallpaper(previous);Render(window,"desktop-violet.png");
                previous=image.Source;colour.SelectedIndex=2;WaitWallpaper(previous);Render(window,"desktop-amber.png");
                previous=image.Source;colour.SelectedIndex=0;WaitWallpaper(previous);
            }
            ((RadioButton)window.FindName("ExportStep")).IsChecked=true;
            Render(window,"finish.png");
            if(wide){((RadioButton)window.FindName("IsoOutputChoice")).IsChecked=true;Render(window,"finish-iso.png");((RadioButton)window.FindName("BothOutput")).IsChecked=true;Render(window,"finish-both.png");}
            var dialogue=new MicrosoftIsoWindow(false){ShowInTaskbar=false,ShowActivated=false,WindowStartupLocation=WindowStartupLocation.Manual,Left=-32000,Top=-32000};
            try
            {
                var catalogue=JsonSerializer.Deserialize<IsoCatalogue>(File.ReadAllBytes(catalogueFile))??throw new IOException("Live catalogue snapshot required");
                var ordered=MicrosoftIso.Languages(JsonSerializer.Serialize(new{Skus=catalogue.Languages.Select(l=>new{l.Id,Language=l.Name})}));
                dialogue.ShowCatalogue(catalogue with{Languages=ordered});
                dialogue.Show();
                var frame=new System.Windows.Threading.DispatcherFrame();dialogue.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));System.Windows.Threading.Dispatcher.PushFrame(frame);
                Render(dialogue,"download.png",650,480);
            }
            finally{dialogue.Close();}
        }
        finally{window.Close();}
        Console.WriteLine("Actual source, app-selection and wallpaper controls captured. No ISO download, Windows servicing, app installation or physical-drive write.");

        void Pump()
        {
            var frame=new System.Windows.Threading.DispatcherFrame();var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(40)};
            timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();System.Windows.Threading.Dispatcher.PushFrame(frame);
        }
        IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}
        }
        void CaptureUsb()
        {
            using var owner=new CancellationTokenSource();
            var usb=new BootDriveWindow(null,(_,_,_,_,_)=>throw new IOException("Video fixture never builds or writes a drive."),null,false);
            var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
            try{
                ((TextBlock)usb.FindName("Source")).Text="Your customised Windows setup";
                ((FrameworkElement)usb.FindName("OptionsPanel")).Visibility=Visibility.Collapsed;
                typeof(BootDriveWindow).GetField("operation",flags)!.SetValue(usb,owner);
                typeof(BootDriveWindow).GetField("timing",flags)!.SetValue(usb,new WorkProgress(DateTimeOffset.UtcNow));
                ((Button)usb.FindName("Start")).IsEnabled=false;
                ((Button)usb.FindName("Stop")).IsEnabled=true;
                typeof(BootDriveWindow).GetMethod("Step",flags)!.Invoke(usb,[5]);
                foreach(var message in new[]{"Checking Windows installation files.","Files checked. Preparing the confirmed USB drive.","Creating the confirmed USB drive. Keep it connected until verification finishes."}){
                    ((TextBlock)usb.FindName("StatusMessage")).Text=message;
                    typeof(BootDriveWindow).GetMethod("RecordMessage",flags)!.Invoke(usb,null);
                }
                var frames=Path.Combine(output,"usb-motion");Directory.CreateDirectory(frames);
                for(int i=0;i<90;i++){
                    var verifying=i>=60;var fraction=verifying?(i-59)/30d:(i+1)/60d;
                    typeof(BootDriveWindow).GetMethod("OnUsbTransfer",flags)!.Invoke(usb,[new UsbTransferProgress(verifying,(long)(fraction*13_000_000_000),13_000_000_000),owner]);
                    Pump();Render(usb,Path.Combine("usb-motion",$"frame-{i:0000}.png"),900,700);
                }
                usb.ShowSuccess("Windows setup USB",TimeSpan.FromMinutes(3),false);
                ((TextBlock)usb.FindName("SuccessTiming")).Text="Example completion screen · No physical drive was written";
                Pump();Render(usb,"usb-complete.png",900,700);
            }finally{typeof(BootDriveWindow).GetField("operation",flags)!.SetValue(usb,null);usb.Close();}
        }

        void WaitWallpaper(ImageSource? previous)
        {
            var image=(Image)window.FindName("WallpaperPreview");var deadline=DateTime.UtcNow.AddSeconds(20);
            while(ReferenceEquals(image.Source,previous) && DateTime.UtcNow<deadline){
                var frame=new System.Windows.Threading.DispatcherFrame();
                var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(50)};
                timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();System.Windows.Threading.Dispatcher.PushFrame(frame);
            }
            if(image.Source==null || ReferenceEquals(image.Source,previous))throw new TimeoutException("Selected wallpaper preview did not finish before capture.");
        }

        void Render(Window view,string name,int width=0,int height=0)
        {
            if(searchSnapshot is not null&&!name.StartsWith("usb-motion",StringComparison.Ordinal))for(int i=0;i<8;i++)Pump();
            if(width==0)width=wide?1600:1160;
            if(height==0)height=wide?800:860;
            var pending=new System.Windows.Threading.DispatcherFrame();view.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,new Action(()=>pending.Continue=false));System.Windows.Threading.Dispatcher.PushFrame(pending);
            view.Width=width;view.Height=height;
            var surface=(FrameworkElement)view.Content;if(surface is Panel panel)panel.SetResourceReference(Panel.BackgroundProperty,"Canvas");var size=new Size(width,height);
            surface.Measure(size);surface.Arrange(new Rect(size));surface.UpdateLayout();
            var bitmap=new RenderTargetBitmap(width*2,height*2,192,192,PixelFormats.Pbgra32);bitmap.Render(surface);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file=File.Create(Path.Combine(output,name));encoder.Save(file);
            if(wide){
                var bounds=new Dictionary<string,object>();
                foreach(var control in new[]{"DownloadWindowsIsoButton","RecommendedPresetButton","AppQuery","SearchButton","WallpaperDesign","WallpaperColour","WallpaperPreview","IsoOutputChoice","BothOutput","UsbOutputChoice","BuildButton","ComponentsStep","AppsStep","PersonalisationStep","ExportStep"}){
                    if(view.FindName(control) is not FrameworkElement element||element.ActualWidth<=0||element.Visibility!=Visibility.Visible)continue;
                    var p=element.TranslatePoint(new Point(),surface);bounds[control]=new{x=p.X,y=p.Y,w=element.ActualWidth,h=element.ActualHeight};
                }
                foreach(var button in Descendants(surface).OfType<Button>()){
                    var key=button.Content?.ToString() switch{"Use gaming preset"=>"GamingPreset","Done"=>"PickerDone","Cancel"=>"DialogCancel",_=>null};
                    if(key is null)continue;
                    var p=button.TranslatePoint(new Point(),surface);bounds[key]=new{x=p.X,y=p.Y,w=button.ActualWidth,h=button.ActualHeight};
                }
                foreach(var choice in Descendants(surface).OfType<AppChoice>().Where(c=>c.Tag is CatalogApp)){
                    var p=choice.TranslatePoint(new Point(),surface);bounds[((CatalogApp)choice.Tag).Id]=new{x=p.X,y=p.Y,w=choice.ActualWidth,h=choice.ActualHeight};
                }
                File.WriteAllText(Path.Combine(output,name+".bounds.json"),JsonSerializer.Serialize(new{width,height,bounds}));
            }
        }
    }
}
