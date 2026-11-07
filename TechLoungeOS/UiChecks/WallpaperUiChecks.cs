using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TechLoungeOS;

static class WallpaperUiChecks
{
    public static void Run(MainWindow window)
    {
        var mode=(ComboBox)window.FindName("WallpaperMode");var design=(ComboBox)window.FindName("WallpaperDesign");var colour=(ComboBox)window.FindName("WallpaperColour");
        var image=(Image)window.FindName("WallpaperPreview");
        if(design.Items.Count!=5 || colour.Items.Count!=3 || mode.SelectedIndex!=0 || design.SelectedIndex!=0 || colour.SelectedIndex!=0)throw new Exception("Five designs, three colours and default Ice waterfall must be selected.");
        for(int d=0;d<5;d++)for(int c=0;c<3;c++){design.SelectedIndex=d;colour.SelectedIndex=c;}
        design.SelectedIndex=0;colour.SelectedIndex=0;
        var deadline=DateTime.UtcNow.AddSeconds(20);
        while(image.Source==null && DateTime.UtcNow<deadline)Pump();
        if(image.Source==null)throw new Exception("Actual packaged wallpaper preview did not render.");
        mode.SelectedIndex=2;Pump();
        if(image.Source!=null || ((FrameworkElement)window.FindName("WallpaperPresetControls")).IsEnabled)throw new Exception("Keep source must disable preset selection and clear its preview.");
        mode.SelectedIndex=1;Pump();
        if(image.Source!=null)throw new Exception("Empty custom choice must not show a stale preset.");
        mode.SelectedIndex=0;
        deadline=DateTime.UtcNow.AddSeconds(20);while(image.Source==null && DateTime.UtcNow<deadline)Pump();
        if(image.Source==null)throw new Exception("Returning to presets did not restore the selected preview.");
        Console.WriteLine("Five-design/three-colour selection, rapid navigation, custom/keep-source switching and default Ice preview passed. Render-only helper; no desktop settings applied.");
    }
    private static void Pump(){var frame=new DispatcherFrame();var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(50)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
}
