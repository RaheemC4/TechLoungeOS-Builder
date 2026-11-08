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
        var usb=new BootDriveWindow("fixture.iso",(_,_,_,_,_)=>throw new InvalidOperationException("Fixture never starts a build"),null,false);
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
            Require(((TextBlock)usb.FindName("StepCaption")).Text.StartsWith("Step 3 of 5")&&((TextBlock)usb.FindName("StepCaption")).Text.Contains("longest stage"),"USB-only has five actual stages and identifies the longest stage without implying an overall percentage");
            Require(((TextBlock)usb.FindName("Message")).Text.Contains("edition you selected")&&!((TextBlock)usb.FindName("Message")).Text.Contains("workspace"),"Current action uses friendly detail instead of the raw technical phase");
            Require(((TextBlock)usb.FindName("ElapsedCaption")).Text.Contains("01:15")&&!((TextBlock)usb.FindName("ElapsedCaption")).Text.Contains("Remaining"),"Elapsed is visible without inventing remaining time");
            status.Text="Downloading and verifying offline Office payload. Full logs are saved in the workspace.";Pump(260);
            var timer=(DispatcherTimer)typeof(BootDriveWindow).GetField("clock",flags)!.GetValue(usb)!;timer.Start();Pump(1100);timer.Stop();
            Require(((TextBlock)usb.FindName("ElapsedCaption")).Text.Contains("01:16"),"The actual window clock advances while its worker is active");
            var entries=(ItemsControl)usb.FindName("ActivityItems");var items=entries.Items.Cast<ActivityEntry>().ToArray();
            Require(items.Count(a=>a.IsWorking)==1&&items[0].Title=="Downloading Microsoft 365","Only the newest active task remains animated while completed entries settle");
            Render(content,Path.Combine(folder,"usb-office.png"));
            typeof(BootDriveWindow).GetMethod("Step",flags)!.Invoke(usb,[5]);
            typeof(BootDriveWindow).GetMethod("OnUsbTransfer",flags)!.Invoke(usb,[new UsbTransferProgress(false,6_500_000_000,13_000_000_000),owner]);Pump(260);
            Require(((TextBlock)usb.FindName("TransferCaption")).Text.Contains("6.50 of 13.00 GB")&&((ProgressBar)usb.FindName("TransferProgress")).Value==50&&((FrameworkElement)usb.FindName("Progress")).Visibility==Visibility.Collapsed,
                "Real USB bytes drive a 50 percent determinate bar and readable copy count");
            Render(content,Path.Combine(folder,"usb-byte-progress.png"));
            typeof(BootDriveWindow).GetMethod("OnUsbTransfer",flags)!.Invoke(usb,[new UsbTransferProgress(true,1_300_000_000,13_000_000_000),owner]);Pump(260);
            Require(((TextBlock)usb.FindName("ResultHeading")).Text=="Checking your setup USB"&&((TextBlock)usb.FindName("TransferCaption")).Text.StartsWith("Checked")&&((ProgressBar)usb.FindName("TransferProgress")).Value==10,
                "Verification has its own progress and clear current-action caption");
            if(captureVideo)
            {
                var frames=Path.Combine(folder,"motion");Directory.CreateDirectory(frames);timer.Start();
                var watch=System.Diagnostics.Stopwatch.StartNew();
                for(var frame=0;frame<90;frame++)
                {
                    if(frame==0)typeof(BootDriveWindow).GetMethod("Step",flags)!.Invoke(usb,[5]);
                    typeof(BootDriveWindow).GetMethod("OnUsbTransfer",flags)!.Invoke(usb,
                        [new UsbTransferProgress(false,2_000_000_000L+frame*50_000_000L,13_000_000_000L),owner]);
                    Pump(Math.Max(1,(int)(frame*1000d/30-watch.Elapsed.TotalMilliseconds)));
                    Render(content,Path.Combine(frames,$"frame-{frame:0000}.png"));
                }
                timer.Stop();
            }
            usb.ShowFailure(new IOException("A required download could not be completed. Check your connection and retry."),false,false);
            var finished=((TextBlock)usb.FindName("Message")).Text;
            typeof(BootDriveWindow).GetMethod("BuildPhase",flags)!.Invoke(usb,["Copying Windows installation media",owner]);
            typeof(BootDriveWindow).GetMethod("OnUsbTransfer",flags)!.Invoke(usb,[new UsbTransferProgress(false,13_000_000_000,13_000_000_000),owner]);
            Require(((TextBlock)usb.FindName("Message")).Text==finished,"A queued phase cannot overwrite the persistent failure");
            Pump(260);Render(content,Path.Combine(folder,"usb-failure.png"));
            usb.ShowSuccess("WINDOWS 11 · Kingston DataTraveler 3.0",TimeSpan.FromSeconds(1219),false);Pump(260);
            Require(((FrameworkElement)usb.FindName("SuccessPanel")).Visibility==Visibility.Visible&&!((Button)usb.FindName("Start")).IsVisible&&
                ((TextBlock)usb.FindName("SuccessHeading")).Text=="Your setup USB is ready"&&((TextBlock)usb.FindName("SuccessTiming")).Text.Contains("20:19"),
                "Successful USB creation has a dedicated completion screen with verified elapsed time and no competing erase button");
            foreach(var size in new[]{new Size(620,520),new Size(760,720)})
            {
                host.Width=size.Width;host.Height=size.Height;host.UpdateLayout();Pump(20);
                var panel=(FrameworkElement)usb.FindName("SuccessPanel");
                foreach(var name in new[]{"SuccessHeading","SuccessDrive","SuccessTiming","SuccessInstructions","SuccessDone"})
                {
                    var control=(FrameworkElement)usb.FindName(name);var bounds=new Rect(control.TranslatePoint(new Point(),panel),control.RenderSize);
                    Require(bounds.Left>=0&&bounds.Top>=0&&bounds.Right<=panel.ActualWidth&&bounds.Bottom<=panel.ActualHeight,"USB completion "+name+" fits at "+size);
                }
                Render(content,Path.Combine(folder,size.Width==620?"usb-ready-minimum.png":"usb-ready.png"));
            }
            var face=new Typeface(((TextBlock)usb.FindName("SuccessHeading")).FontFamily,FontStyles.Normal,FontWeights.Normal,FontStretches.Normal);
            Require(face.TryGetGlyphTypeface(out var glyph)&&glyph.FontUri.ToString().Contains("Sora",StringComparison.OrdinalIgnoreCase),"USB completion uses the packaged Sora font rather than a system fallback");
        }
        finally{typeof(BootDriveWindow).GetField("operation",flags)!.SetValue(usb,null);owner.Dispose();host.Close();}
        Console.WriteLine("Native WPF activity/transition/layout fixtures passed. No build, drive enumeration, servicing or physical USB write ran.");
    }
    private static void Pump(int milliseconds){var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(milliseconds)};timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);}
    private static void Render(FrameworkElement view,string path){var bitmap=new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth*2),(int)Math.Ceiling(view.ActualHeight*2),192,192,PixelFormats.Pbgra32);var drawing=new DrawingVisual();using(var paint=drawing.RenderOpen())paint.DrawRectangle(new VisualBrush(view),null,new Rect(0,0,view.ActualWidth,view.ActualHeight));bitmap.Render(drawing);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(path);encoder.Save(output);}
}
