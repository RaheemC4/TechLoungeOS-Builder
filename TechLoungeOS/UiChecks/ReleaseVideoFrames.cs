using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TechLoungeOS;

internal static class ReleaseVideoFrames
{
    // Captures the actual compiled controls, not a separately drawn interface.
    internal static void Run(string output,string catalogueFile,string? importedFile=null)
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
            }
            ((RadioButton)window.FindName("AppsStep")).IsChecked=true;
            Render(window,"apps-before.png");
            ((Button)window.FindName("RecommendedPresetButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Render(window,"apps.png");
            ((RadioButton)window.FindName("PersonalisationStep")).IsChecked=true;
            Render(window,"desktop.png");
            ((RadioButton)window.FindName("ExportStep")).IsChecked=true;
            Render(window,"finish.png");
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

        void Render(Window view,string name,int width=1160,int height=860)
        {
            var pending=new System.Windows.Threading.DispatcherFrame();view.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,new Action(()=>pending.Continue=false));System.Windows.Threading.Dispatcher.PushFrame(pending);
            view.Width=width;view.Height=height;
            var surface=(FrameworkElement)view.Content;if(surface is Panel panel)panel.SetResourceReference(Panel.BackgroundProperty,"Canvas");var size=new Size(width,height);
            surface.Measure(size);surface.Arrange(new Rect(size));surface.UpdateLayout();
            var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(surface);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file=File.Create(Path.Combine(output,name));encoder.Save(file);
        }
    }
}
