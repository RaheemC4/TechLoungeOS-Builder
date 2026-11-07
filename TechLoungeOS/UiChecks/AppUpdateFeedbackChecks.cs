using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TechLoungeOS;
using TechLoungeOS.Updates;

internal static class AppUpdateFeedbackChecks
{
    internal static void Run(string folder)
    {
        Directory.CreateDirectory(folder);
        var window=new MainWindow(false);
        var previousContext=SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
        var button=(Button)window.FindName("CheckUpdatesButton");
        var label=(TextBlock)window.FindName("CheckUpdatesLabel");
        var feedback=(TextBlock)window.FindName("UpdateFeedback");
        var details=(TextBlock)window.FindName("UpdateMessage");
        var status=(TextBlock)window.FindName("Status");
        try
        {
            var pending=new TaskCompletionSource<ReadyUpdate?>();
            var operation=window.CheckUpdates(true,_=>pending.Task);
            Require(button.IsEnabled && label.Text=="Check for updates" && feedback.Text=="Checking for app updates…","Immediate checking feedback keeps the button label and appearance stable");
            Wait(window.CheckUpdates(true,_=>throw new Exception("Duplicate request started")));
            pending.SetResult(null);Wait(operation);
            Require(button.IsEnabled && label.Text=="Check for updates" && feedback.Text=="You’re up to date" && details.Text.Contains(AppUpdates.CurrentVersion.ToString()),"Current version feedback and restored action");
            Snapshot("up-to-date");
            using var client=new HttpClient(new NoRelease());
            var updates=new AppUpdates(client);
            Wait(window.CheckUpdates(true,ct=>updates.Check(AppUpdates.CurrentVersion,ct)));
            Require(feedback.Text=="No app updates available" && details.Text.Contains("You can still customise ISOs") && !details.Text.Contains("up to date"),"Unpublished release must not claim latest version");
            Snapshot("no-public-release");
            Wait(window.CheckUpdates(true,_=>Task.FromException<ReadyUpdate?>(new IOException("inert offline fixture"))));
            Require(feedback.Text=="Couldn’t check for updates" && details.Text.Contains("inert offline fixture") && button.IsEnabled,"Failure remains visible and retryable");
            Wait(window.CheckUpdates(true,_=>Task.FromCanceled<ReadyUpdate?>(new CancellationToken(true))));
            Require(feedback.Text=="Check cancelled" && button.IsEnabled,"Cancellation restores action");
            typeof(MainWindow).GetMethod("SetBusy",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[true]);
            status.Text="Building your ISO…";
            Wait(window.CheckUpdates(true,_=>Task.FromResult<ReadyUpdate?>(null)));
            Require(status.Text=="Building your ISO…" && feedback.Text=="You’re up to date","App check does not overwrite an active media operation");
            typeof(MainWindow).GetMethod("SetBusy",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[false]);
            const string raw="[{\"ImageIndex\":1,\"Architecture\":\"9\",\"ImageName\":\"Windows 11 Pro\"}]";
            typeof(MainWindow).GetMethod("Write",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[raw]);
            status.Text="Found 1 Windows edition. Choose one to customise.";Pump();
            Require(window.ActivityEntries[0].Title=="Windows image ready" && window.DiagnosticText.Contains(raw),"Plain-English activity preserves exact technical output");
            Require(window.FindName("TechnicalLog")==null && window.FindName("Log")==null,"Normal activity has no nested technical disclosure or raw textbox");
            Require(button.ReadLocalValue(Control.BackgroundProperty)==DependencyProperty.UnsetValue && button.ReadLocalValue(Control.ForegroundProperty)==DependencyProperty.UnsetValue,"Native hover colours are not overridden locally");
            CheckRegionalSearch();
            CheckWorkspaceSelection();
            CheckMotion();
            CopyChecks.Run(window);
            Console.WriteLine("App-update checking/current/unpublished/failure/cancel/retry/duplicate/media-busy feedback passed; activity keeps raw details. Rendered at 720x500 and 1160x860. No network, servicing or VM execution.");
        }
        finally{window.Close();SynchronizationContext.SetSynchronizationContext(previousContext);}

        void CheckMotion()
        {
            // A real WPF presentation source without focus changes or an on-screen window.
            window.ShowActivated=false;window.ShowInTaskbar=false;window.WindowStartupLocation=WindowStartupLocation.Manual;
            window.Left=-20000;window.Top=-20000;window.Show();Pump();
            Wait(Task.Delay(220));
            var slot=(FrameworkElement)window.FindName("UpdateFeedbackSlot");
            var outgoing=(TextBlock)window.FindName("PreviousUpdateFeedback");
            var icon=(TextBlock)window.FindName("UpdateCheckIcon");
            var origin=button.TranslatePoint(new Point(),(UIElement)window.Content);
            var width=button.ActualWidth;var height=slot.ActualHeight;
            var watch=System.Diagnostics.Stopwatch.StartNew();
            var task=window.CheckUpdates(true,_=>Task.FromException<ReadyUpdate?>(new IOException("instant failure motion fixture")));
            Wait(Task.Delay(230));
            Require(!task.IsCompleted && feedback.Text=="Checking for app updates…" && label.Text=="Check for updates","Even instant failures retain a readable checking state and fixed action text");
            Require(button.TranslatePoint(new Point(),(UIElement)window.Content)==origin && button.ActualWidth==width && slot.ActualHeight==height,"Checking does not shift or resize the action or feedback slot");
            var animated=SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast;
            Require(((RotateTransform)icon.RenderTransform).HasAnimatedProperties==animated,"Checking icon respects Windows animation/high contrast preferences");
            Wait(task);
            Require(watch.ElapsedMilliseconds>=340 && feedback.Text=="Couldn’t check for updates","The final result replaces checking after the minimum readable interval");
            if(animated)
            {
                Require(feedback.HasAnimatedProperties && outgoing.HasAnimatedProperties,"Result uses a real WPF crossfade");
                for(int i=0;i<5;i++)
                {
                    var content=(FrameworkElement)window.Content;content.UpdateLayout();
                    var frame=new RenderTargetBitmap((int)content.ActualWidth,58,96,96,PixelFormats.Pbgra32);frame.Render(content);
                    var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(frame));
                    using(var stream=File.Create(Path.Combine(folder,$"transition-{i}.png")))png.Save(stream);
                    Require(Math.Abs(feedback.Opacity+outgoing.Opacity-1)<.06,"Crossfade retains combined opacity without a blank frame");
                    Wait(Task.Delay(55));
                }
            }
            Wait(Task.Delay(220));
            Require(feedback.Opacity==1 && outgoing.Opacity==0 && !((RotateTransform)icon.RenderTransform).HasAnimatedProperties,"Feedback settles fully visible and the loading animation stops");
            var hiddenCopy=feedback.Text;
            window.Hide();Wait(window.CheckUpdates(true,_=>Task.FromException<ReadyUpdate?>(new IOException("same error"))));
            Require(feedback.Text==hiddenCopy && feedback.Opacity==1,"Repeating the same error does not leave the text dimmed");
        }

        void CheckRegionalSearch()
        {
            foreach(var names in new[]{("SetupRegionChoice","en-GB","en-US"),("SetupKeyboardChoice","00000809","00000409")})
            {
                var picker=(ChoicePicker)window.FindName(names.Item1);
                Require(((SetupRegional.Choice)picker.Items[0]).Id==names.Item2 && ((SetupRegional.Choice)picker.Items[1]).Id==names.Item3 && picker.Choice?.Id==names.Item2,"UK default and UK/US first in native dropdown");
                var count=picker.Items.Count;picker.FilterChoices("united states");
                Require(picker.Items.Count<count && picker.Items.Cast<SetupRegional.Choice>().Any(c=>c.Id==names.Item3) && picker.Choice?.Id==names.Item2,"Inline filtering keeps the committed choice until a result is selected");
                picker.SelectedItem=picker.Items.Cast<SetupRegional.Choice>().Single(c=>c.Id==names.Item3);picker.RestoreChoice();
                Require(picker.Choice?.Id==names.Item3 && picker.Items.Count==count,"Choosing a filtered result commits it and restores the full list");
                picker.FilterChoices("no-match-1582");Require(picker.Items.Count==0 && picker.Choice?.Id==names.Item3,"No-match search does not discard the chosen setting");
                picker.RestoreChoice();picker.SelectedIndex=0;
            }
            Require(window.FindName("RegionSearch")==null && window.FindName("KeyboardSearch")==null,"No separate region/keyboard search boxes");
        }
        void CheckWorkspaceSelection()
        {
            var volumes=new[]{new WorkspaceStorage.Volume(@"C:\","NTFS",150,true),new WorkspaceStorage.Volume(@"D:\","NTFS",600,true),new WorkspaceStorage.Volume(@"E:\","exFAT",900,true),new WorkspaceStorage.Volume(@"F:\","NTFS",999,false)};
            Require(WorkspaceStorage.Select(volumes,100,[])==@"D:\","Automatic storage chooses the suitable local NTFS drive with most space");
            Require(WorkspaceStorage.Select(volumes,100,["D"])==@"C:\","Automatic USB storage excludes all target-drive volumes");
            try{WorkspaceStorage.Select(volumes,700,[]);throw new Exception("Insufficient space was accepted");}catch(IOException){ }
            Require(window.FindName("Workspace")==null && window.FindName("Target16Gb")==null,"Automatic storage and size defaults have no UI fields");
        }

        void Snapshot(string name)
        {
            foreach(var size in new[]{new Size(720,500),new Size(1160,860)})
            {
                window.Width=size.Width;window.Height=size.Height;
                var content=(FrameworkElement)window.Content;
                content.Measure(size);content.Arrange(new Rect(size));content.UpdateLayout();Pump();
                Require(button.ActualHeight<=32 && button.ActualWidth<185,"Compact update action");
                var point=feedback.TranslatePoint(new Point(),content);
                Require(point.X>=0 && point.Y>=0 && point.X+feedback.ActualWidth<=size.Width && point.Y+feedback.ActualHeight<=58,"Persistent result fits the header");
                var image=new RenderTargetBitmap((int)size.Width,(int)size.Height,96,96,PixelFormats.Pbgra32);image.Render(content);
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));
                using var stream=File.Create(Path.Combine(folder,$"{name}-{size.Width}x{size.Height}.png"));png.Save(stream);
            }
        }
    }
    private static void Require(bool passed,string message){if(!passed)throw new IOException(message);}
    private static void Pump(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
    private static void Wait(Task task){while(!task.IsCompleted)Pump();task.GetAwaiter().GetResult();}
    private sealed class NoRelease:HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
