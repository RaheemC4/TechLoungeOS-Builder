using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TechLoungeOS;

internal static class UsbActivityChecks
{
    internal static void Run(string folder,bool captureVideo=false)
    {
        Directory.CreateDirectory(folder);
        var usb=new BootDriveWindow("fixture.iso",(_,_,_,_)=>throw new InvalidOperationException("Fixture never starts a build"),null,false);
        var content=(FrameworkElement)usb.Content;var host=usb;
        host.ShowInTaskbar=false;host.ShowActivated=false;host.WindowStartupLocation=WindowStartupLocation.Manual;host.Left=-32000;host.Top=-32000;
        var owner=new CancellationTokenSource();
        void Require(bool value,string message){if(!value)throw new IOException(message);Console.WriteLine("PASS: "+message);}
        var flags=BindingFlags.NonPublic|BindingFlags.Instance;
        try
        {
            host.Show();Pump(20);
            foreach(var size in new[]{new Size(620,520),new Size(760,720)})
            {
                host.Width=size.Width;host.Height=size.Height;host.UpdateLayout();Pump(20);
                foreach(var name in new[]{"Start","Drives","UsbLabel","ResultBanner"})
                {
                    var control=(FrameworkElement)usb.FindName(name);var bounds=new Rect(control.TranslatePoint(new Point(),content),control.RenderSize);
                    Require(bounds.Left>=0&&bounds.Right<=content.ActualWidth&&bounds.Top>=0&&bounds.Bottom<=content.ActualHeight,"USB "+name+" fits without an outer scrollbar at "+size);
                }
            }
            typeof(BootDriveWindow).GetField("operation",flags)!.SetValue(usb,owner);
            typeof(BootDriveWindow).GetField("timing",flags)!.SetValue(usb,new WorkProgress(DateTimeOffset.UtcNow.AddSeconds(-75)));
            ((FrameworkElement)usb.FindName("OptionsPanel")).Visibility=Visibility.Collapsed;
            typeof(BootDriveWindow).GetMethod("Step",flags)!.Invoke(usb,[3]);
            var status=(TextBlock)usb.FindName("StatusMessage");status.Text="Exporting selected edition. Full logs are saved in the workspace.";
            ((FrameworkElement)usb.FindName("ElapsedCaption")).Visibility=Visibility.Visible;
            ((FrameworkElement)usb.FindName("Progress")).Visibility=Visibility.Visible;
            typeof(BootDriveWindow).GetMethod("UpdateClock",flags)!.Invoke(usb,null);
            Pump(30);Render(content,Path.Combine(folder,"activity-arriving.png"));Pump(260);Render(content,Path.Combine(folder,"usb-progress.png"));
            Require(((TextBlock)usb.FindName("StepCaption")).Text.StartsWith("Step 3 of 7"),"Customise-to-USB declares a consistent seven-step plan");
            Require(((TextBlock)usb.FindName("Message")).Text.Contains("edition you selected")&&!((TextBlock)usb.FindName("Message")).Text.Contains("workspace"),"Current action uses friendly detail instead of the raw technical phase");
            Require(((TextBlock)usb.FindName("ElapsedCaption")).Text.Contains("01:15")&&!((TextBlock)usb.FindName("ElapsedCaption")).Text.Contains("Remaining"),"Elapsed is visible without inventing remaining time");
            status.Text="Downloading and verifying offline Office payload. Full logs are saved in the workspace.";Pump(260);
            var timer=(DispatcherTimer)typeof(BootDriveWindow).GetField("clock",flags)!.GetValue(usb)!;timer.Start();Pump(1100);timer.Stop();
            Require(((TextBlock)usb.FindName("ElapsedCaption")).Text.Contains("01:16"),"The actual window clock advances while its worker is active");
            var entries=(ItemsControl)usb.FindName("ActivityItems");var items=entries.Items.Cast<ActivityEntry>().ToArray();
            Require(items.Count(a=>a.IsWorking)==1&&items[0].Title=="Downloading Microsoft 365","Only the newest active task remains animated while completed entries settle");
            Render(content,Path.Combine(folder,"usb-office.png"));
            if(captureVideo)
            {
                var frames=Path.Combine(folder,"motion");Directory.CreateDirectory(frames);timer.Start();
                var watch=System.Diagnostics.Stopwatch.StartNew();
                for(var frame=0;frame<90;frame++)
                {
                    if(frame==0)status.Text="Exporting selected edition. Full logs are saved in the workspace.";
                    if(frame==15){typeof(BootDriveWindow).GetMethod("Step",flags)!.Invoke(usb,[1]);status.Text="Downloading and verifying offline Office payload. Full logs are saved in the workspace.";}
                    Pump(Math.Max(1,(int)(frame*1000d/30-watch.Elapsed.TotalMilliseconds)));
                    Render(content,Path.Combine(frames,$"frame-{frame:0000}.png"));
                }
                timer.Stop();
            }
            usb.ShowFailure(new IOException("A required download could not be completed. Check your connection and retry."),false,false);
            var finished=((TextBlock)usb.FindName("Message")).Text;
            typeof(BootDriveWindow).GetMethod("BuildPhase",flags)!.Invoke(usb,["Copying Windows installation media",owner]);
            Require(((TextBlock)usb.FindName("Message")).Text==finished,"A queued phase cannot overwrite the persistent failure");
            Pump(260);Render(content,Path.Combine(folder,"usb-failure.png"));
        }
        finally{typeof(BootDriveWindow).GetField("operation",flags)!.SetValue(usb,null);owner.Dispose();host.Close();}
        Console.WriteLine("Native WPF activity/transition/layout fixtures passed. No build, drive enumeration, servicing or physical USB write ran.");
    }
    private static void Pump(int milliseconds){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(milliseconds)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
    private static void Render(FrameworkElement view,string path){var bitmap=new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth*2),(int)Math.Ceiling(view.ActualHeight*2),192,192,PixelFormats.Pbgra32);var drawing=new DrawingVisual();using(var paint=drawing.RenderOpen())paint.DrawRectangle(new VisualBrush(view),null,new Rect(0,0,view.ActualWidth,view.ActualHeight));bitmap.Render(drawing);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(path);encoder.Save(output);}
}
