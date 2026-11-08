using System.Windows;
using System.Windows.Media;
namespace TechLoungeOS;
public partial class App : Application
{
    private readonly bool launchMainWindow;
    public App():this(true){}
    public App(bool launchMainWindow)=>this.launchMainWindow=launchMainWindow;
    protected override void OnStartup(StartupEventArgs e)
    {
        // Update-cache creation belongs to update operations, never ordinary app startup.
        Updates.UpdateCoordinator? startupGate=null;
        try {startupGate=Updates.UpdateCoordinator.Enter(AppContext.BaseDirectory);}
        catch(Exception ex){MessageBox.Show(ex.Message,"TechLoungeOS Builder");Shutdown(1);return;}
        using var gate=startupGate;
        ApplySystemAppearance();
        SystemParameters.StaticPropertyChanged += AppearanceChanged;
        base.OnStartup(e);
        if(launchMainWindow)
        {
            MainWindow=new MainWindow();MainWindow.Show();
            if(e.Args is ["--resume-usb",var checkpoint])Dispatcher.BeginInvoke(new Action(()=>
            {
                try{var usb=BootDriveWindow.ResumePrepared(checkpoint);usb.Owner=MainWindow;usb.ShowDialog();}
                catch(Exception error){MessageBox.Show(MainWindow,error.Message,"Prepared setup unavailable",MessageBoxButton.OK,MessageBoxImage.Error);}
            }));
        }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        SystemParameters.StaticPropertyChanged -= AppearanceChanged;
        base.OnExit(e);
    }
    private void AppearanceChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemParameters.HighContrast) or nameof(SystemParameters.ClientAreaAnimation)) Dispatcher.Invoke(() =>
        {
            ApplySystemAppearance();
            foreach (Window window in Windows) WindowsChrome.Apply(window);
            foreach (MainWindow window in Windows.OfType<MainWindow>()) window.RefreshMotion();
        });
    }
    private Dictionary<string, object>? normalBrushes;
    private void ApplySystemAppearance()
    {
        normalBrushes ??= new[] { "Canvas", "Surface", "Ink", "Muted", "Line", "Accent", "AccentText", "Tint" }
            .ToDictionary(key => key, key => Resources[key]);
        foreach (var (key, brush) in normalBrushes) Resources[key] = brush;
        if (!SystemParameters.HighContrast) return;
        foreach (var key in new[] { "Canvas", "Surface", "Tint" }) Resources[key] = SystemColors.WindowBrush;
        foreach (var key in new[] { "Ink", "Muted", "Line" }) Resources[key] = SystemColors.WindowTextBrush;
        Resources["Accent"] = SystemColors.HighlightBrush; Resources["AccentText"] = SystemColors.HighlightTextBrush;
    }
}
