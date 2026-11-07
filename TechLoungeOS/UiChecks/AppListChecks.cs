using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TechLoungeOS;

internal static class AppListChecks
{
    internal static void Run(string folder)
    {
        Directory.CreateDirectory(folder);var window=new MainWindow(false);
        try
        {
            ((RadioButton)window.FindName("AppsStep")).IsChecked=true;
            var list=(Panel)window.FindName("Apps");var preset=(ToggleButton)window.FindName("RecommendedPresetButton");
            var search=(Panel)window.FindName("SearchResults");var choices=(Panel)window.FindName("RecommendedApps");
            void Require(bool ok,string message){if(!ok)throw new IOException(message);Console.WriteLine("PASS: "+message);}
            void Toggle(ToggleButton row)=>((IToggleProvider)new ToggleButtonAutomationPeer(row).GetPattern(PatternInterface.Toggle)).Toggle();
            void Results(params CatalogApp[] apps)=>typeof(MainWindow).GetMethod("ShowSearchResults",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(window,[apps]);
            Require(list.Children.Count==0 && window.FindName("SelectedAppsDetails") is null,"One main app list replaces the duplicate selected-app expander");
            Toggle(preset);Pump();preset.ApplyTemplate();Require(list.Children.Count==9 && preset.IsChecked==true && ((UIElement)preset.Template.FindName("Selected",preset)).Opacity==1,"Accessible Recommended toggle adds nine apps and visibly highlights its selected state");
            Require(preset.Template.FindName("Check",preset) is null && preset.Template.FindName("Focus",preset) is null,"Recommended group has no extra checkbox or outer focus rectangle");
            Toggle(preset);Pump();Require(list.Children.Count==0 && preset.IsChecked==false,"Untoggling Recommended removes its rows");
            Results(new("VLC","VideoLAN.VLC","fixture"),new("Shiftd",RecommendedApps.Shiftd,"fixture"));
            Require(search.Children.Count==1,"Mandatory Shiftd remains outside optional search selection");
            Toggle((ToggleButton)search.Children[0]);Pump();Require(list.Children.Count==1,"Selecting a search result adds one row to the same main list");
            Toggle(preset);Pump();Require(list.Children.Count==10,"Recommendations retain a separately chosen optional app");
            Toggle(preset);Pump();Require(list.Children.Count==1 && ((CatalogApp)((ToggleButton)list.Children[0]).Tag).Id=="VideoLAN.VLC","Removing recommendations preserves the separate optional app");
            Toggle((ToggleButton)list.Children[0]);Pump();Require(list.Children.Count==0 && ((ToggleButton)search.Children[0]).IsChecked==false,"Unticking the main row removes it and synchronises the picker");
            ((ComboBox)window.FindName("AppCategory")).SelectedIndex=1;
            var firefox=choices.Children.OfType<ToggleButton>().Single(r=>((CatalogApp)r.Tag).Id=="Mozilla.Firefox");
            Toggle(firefox);Toggle(firefox);Toggle(firefox);Pump();Require(list.Children.Count==1 && firefox.IsChecked==true,"Rapid optional-app toggles settle without duplicate or stale rows");
            Toggle(preset);Pump();var steam=list.Children.OfType<ToggleButton>().Single(r=>((CatalogApp)r.Tag).Id=="Valve.Steam");Toggle(steam);Pump();
            Require(list.Children.Count==9 && preset.IsChecked==false,"Individual removal updates the recommendation-group highlight without removing other apps");
            Require(((FrameworkElement)window.FindName("RecommendedApps")).Parent is ScrollViewer && window.FindName("AppPickerPopup") is Popup,"The browsing collection lives in the compact picker, not a second main-page list");
            Require(window.FindName("OscdimgPath") is null && window.FindName("IsoToolSettings") is null,"No Microsoft tool path or manual executable selector remains");
            foreach(var size in new[]{new Size(720,500),new Size(1160,860)})
            {
                var surface=(FrameworkElement)window.Content;window.Width=size.Width;window.Height=size.Height;surface.Measure(size);surface.Arrange(new Rect(size));surface.UpdateLayout();Pump();
                Require(list.ActualWidth>200 && list.Children.OfType<FrameworkElement>().All(r=>r.ActualWidth>150),"Selected app rows remain usable at "+size);
                Render(surface,Path.Combine(folder,"apps-"+size.Width+".png"),(int)size.Width,(int)size.Height);
            }
            var usb=new BootDriveWindow(null);
            try
            {
                typeof(BootDriveWindow).GetMethod("Write",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(usb,["[{\"UniqueId\":\"private-device-identity\"}]"]);
                var entries=(ItemsControl)usb.FindName("ActivityItems");
                Require(entries.Items.Cast<ActivityEntry>().All(a=>!a.Detail.Contains("UniqueId")),"Raw USB inventory is retained only in diagnostics, never the friendly activity feed");
                usb.ShowFailure(new IOException("Could not prepare the required build component."),false,false);
                Require(((TextBlock)usb.FindName("ResultHeading")).Text=="USB was not created" && ((TextBlock)usb.FindName("Message")).Text.Contains("has not been erased"),"Pre-write failure has a persistent explicit error and accurate drive-state message");
                usb.ShowFailure(new IOException("Write failed"),true,false);
                Require(((TextBlock)usb.FindName("Message")).Text.Contains("may be incomplete"),"A write-phase failure never claims the USB is unchanged");
                var surface=(FrameworkElement)usb.Content;surface.Measure(new Size(720,700));surface.Arrange(new Rect(0,0,720,700));surface.UpdateLayout();Render(surface,Path.Combine(folder,"usb-failure.png"),720,700);
            }
            finally{usb.Close();}
        }
        finally{window.Close();}
        Console.WriteLine("Compiled app-list and USB-feedback fixtures passed. No installer, ISO servicing or physical USB write was run.");
    }
    private static void Pump(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
    private static void Render(FrameworkElement view,string path,int width,int height){var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(view);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);}
}
