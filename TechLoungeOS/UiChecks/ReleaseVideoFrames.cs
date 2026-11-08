using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TechLoungeOS;

internal static class ReleaseVideoFrames
{
    // Captures the actual compiled controls, not a separately drawn interface.
    internal static void Run(string output,string catalogueFile,string? importedFile=null,bool wide=false)
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
            if(wide){
                var rows=(Panel)window.FindName("Apps");
                rows.Children.OfType<System.Windows.Controls.Primitives.ToggleButton>().Single(r=>((CatalogApp)r.Tag).Id=="Brave.Brave").IsChecked=false;
                Render(window,"apps-minus.png");
                ((System.Windows.Controls.Primitives.ToggleButton)window.FindName("RecommendedPresetButton")).IsChecked=true;
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
        Console.WriteLine("Current source, language-download, app selection and wallpaper controls rendered. No network download, ISO servicing or desktop automation.");

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
                foreach(var control in new[]{"DownloadWindowsIsoButton","RecommendedPresetButton","WallpaperDesign","WallpaperColour","WallpaperPreview","IsoOutputChoice","BothOutput","UsbOutputChoice","BuildButton"}){
                    if(view.FindName(control) is not FrameworkElement element||element.ActualWidth<=0||element.Visibility!=Visibility.Visible)continue;
                    var p=element.TranslatePoint(new Point(),surface);bounds[control]=new{x=p.X,y=p.Y,w=element.ActualWidth,h=element.ActualHeight};
                }
                File.WriteAllText(Path.Combine(output,name+".bounds.json"),JsonSerializer.Serialize(new{width,height,bounds}));
            }
        }
    }
}
