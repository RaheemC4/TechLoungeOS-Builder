using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace TechLoungeOS;

public partial class MainWindow
{
    private bool wallpaperReady;
    private volatile int wallpaperRevision;
    private readonly SemaphoreSlim wallpaperPreviewGate=new(1,1);
    private void InitialiseWallpaperPresets()
    {
        WallpaperDesign.ItemsSource=WallpaperPresets.Designs;WallpaperColour.ItemsSource=WallpaperPresets.Colours;
        WallpaperDesign.SelectedIndex=0;WallpaperColour.SelectedIndex=0;
        wallpaperReady=true;WallpaperMode.SelectedIndex=0;
        Closed+=(_,_)=>{wallpaperReady=false;wallpaperRevision++;};
    }
    private string SelectedWallpaperPreset()=>((WallpaperPresets.Choice)WallpaperDesign.SelectedItem).Id+"."+((WallpaperPresets.Choice)WallpaperColour.SelectedItem).Id;
    private async void WallpaperPresetChanged(object sender,System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if(!wallpaperReady)return;
        int revision=++wallpaperRevision;
        WallpaperPresetControls.IsEnabled=WallpaperMode.SelectedIndex==0;
        WallpaperPresetControls.Visibility=WallpaperMode.SelectedIndex==0?Visibility.Visible:Visibility.Collapsed;
        CustomWallpaperControls.Visibility=WallpaperMode.SelectedIndex==1?Visibility.Visible:Visibility.Collapsed;
        WallpaperPreviewFrame.Visibility=WallpaperMode.SelectedIndex==2?Visibility.Collapsed:Visibility.Visible;
        if(WallpaperMode.SelectedIndex!=0)
        {
            if(WallpaperMode.SelectedIndex==1 && File.Exists(WallpaperPath.Text)) {LoadWallpaperPreview(WallpaperPath.Text);return;}
            WallpaperPreview.Source=null;WallpaperPlaceholder.Text=WallpaperMode.SelectedIndex==2?"Keep the source background":"Choose a custom image";WallpaperPlaceholder.Visibility=Visibility.Visible;return;
        }
        var id=SelectedWallpaperPreset();var output=Path.Combine(Path.GetTempPath(),"TLOS-wallpaper-preview-"+Guid.NewGuid().ToString("N")+".png");
        await wallpaperPreviewGate.WaitAsync();
        try
        {
            if(revision!=wallpaperRevision)return;
            var start=new System.Diagnostics.ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"Personalise.exe")){UseShellExecute=false,CreateNoWindow=true};
            foreach(var argument in new[]{"--render",WallpaperPresets.Library,id,"960","540",output})start.ArgumentList.Add(argument);
            using var process=System.Diagnostics.Process.Start(start)??throw new IOException("Wallpaper preview renderer could not start.");
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){if(!process.HasExited)process.Kill();throw new IOException("Wallpaper preview took too long.");}
            if(process.ExitCode!=0)throw new IOException("The preset renderer could not read the packaged collection.");
            if(!Dispatcher.HasShutdownStarted)await Dispatcher.InvokeAsync(()=>{if(revision==wallpaperRevision)LoadWallpaperPreview(output);});
        }
        catch(TaskCanceledException) when(Dispatcher.HasShutdownStarted){ }
        catch(Exception ex)when(ex is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        {if(!Dispatcher.HasShutdownStarted)await Dispatcher.InvokeAsync(()=>{if(revision==wallpaperRevision)Status.Text="Wallpaper preview unavailable: "+ex.Message;});}
        finally{wallpaperPreviewGate.Release();try{if(File.Exists(output))File.Delete(output);}catch(IOException){ /* Owned preview file only; never remove another path. */ }}
    }
    private void LoadWallpaperPreview(string path)
    {
        using var file=File.OpenRead(path);
        var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=file;image.EndInit();image.Freeze();
        WallpaperPreview.Source=image;WallpaperPlaceholder.Visibility=Visibility.Collapsed;
    }
    private void WallpaperClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "Choose the installed desktop background", Filter = "Wallpaper image|*.png;*.jpg;*.jpeg;*.bmp" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            Wallpaper.Validate(dialog.FileName);
            using var file = File.OpenRead(dialog.FileName);
            var metadata = BitmapDecoder.Create(file, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None).Frames[0];
            if ((long)metadata.PixelWidth * metadata.PixelHeight > 60000000) throw new IOException("Choose an image under 60 million pixels.");
            file.Position = 0;
            var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 1200; image.StreamSource = file; image.EndInit();
            image.Freeze(); WallpaperPath.Text = dialog.FileName;WallpaperMode.SelectedIndex=1;wallpaperRevision++;WallpaperPreview.Source = image;
            WallpaperPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException or FileFormatException or UnauthorizedAccessException)
        { Status.Text = "Wallpaper could not be selected: " + ex.Message; }
    }

    private void ClearWallpaperClick(object sender, RoutedEventArgs e)
    {
        WallpaperPath.Text = "";WallpaperMode.SelectedIndex=2;wallpaperRevision++; WallpaperPreview.Source = null;WallpaperPlaceholder.Text="Keep the source background"; WallpaperPlaceholder.Visibility = Visibility.Visible;
    }
}
