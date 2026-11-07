using System;
using System.Diagnostics;
using System.IO;

namespace TechLoungeOS.FirstLogin
{
    internal static class Checks
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try{return Run(args);}
            catch(Exception error){Directory.CreateDirectory(args[0]);File.WriteAllText(Path.Combine(args[0],"failure.txt"),error.ToString());return 1;}
        }
        private static int Run(string[] args)
        {
            string root=args[0];Directory.CreateDirectory(root);
            var powerCalls=new System.Collections.Generic.List<uint>();
            var power=new InstallerActivity(flags=>{powerCalls.Add(flags);return 0x80000002;});
            if(!power.Active)throw new Exception("A successful installer power request was lost.");
            power.Dispose();power.Dispose();
            if(powerCalls.Count!=3 || powerCalls[0]!=0x80000001 || powerCalls[1]!=0x80000003 || powerCalls[2]!=0x80000002)
                throw new Exception("Setup must prevent only idle sleep and restore the previous thread request exactly once.");
            powerCalls.Clear();
            using(var failed=new InstallerActivity(flags=>{powerCalls.Add(flags);return 0;}))
                if(failed.Active)throw new Exception("A rejected power request was reported as active.");
            if(powerCalls.Count!=1)throw new Exception("A rejected request must not clear an unrelated power request.");
            powerCalls.Clear();
            try {
                using(var activity=new InstallerActivity(flags=>{powerCalls.Add(flags);return 0x80000000;}))
                    throw new IOException("Inert child-start failure fixture");
            }catch(IOException){}
            if(powerCalls.Count!=3 || powerCalls[2]!=0x80000000)
                throw new Exception("A failed installer launch must release the idle-sleep request.");
            File.WriteAllText(Path.Combine(root,"installer-power-checks.txt"),"Idle sleep only, previous state restored, rejection and exceptional exit passed with an inert API delegate.");
            int probes=0,waited=0;
            if(!DesktopReady.Wait(()=>++probes>=3,ms=>waited+=ms,3000) || probes!=6 || waited!=1250)
                throw new Exception("Installers must wait for four consecutive desktop-ready observations.");
            probes=0;waited=0;
            if(DesktopReady.Wait(()=>++probes%4!=0,ms=>waited+=ms,2000) || probes!=8 || waited!=2000)
                throw new Exception("An unstable desktop must not start installers or wait indefinitely.");
            probes=0;waited=0;
            if(DesktopReady.Wait(()=>{probes++;return false;},ms=>waited+=ms,1000) || probes!=4 || waited!=1000)
                throw new Exception("A missing desktop must remain a bounded, retryable state.");
            File.WriteAllText(Path.Combine(root,"desktop-gate-checks.txt"),"stable, unstable and missing shell checks passed without probing or changing the host desktop");
            string log=Path.Combine(root,"bootstrap.log");
            string token=@"O'Brien 测试 \ ""quoted"" end\";
            int code=Launcher.RunHidden(args[1],Launcher.Quote(token),root,log);
            if(code!=17)throw new Exception("Child exit code was not retained.");
            string content=File.ReadAllText(log);
            if(!content.Contains("console=0")||!content.Contains("stderr-drained")||!content.Contains(token))throw new Exception("A console was attached, output was lost or Unicode/quoting changed.");
            using(var active=new FileStream(log+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None))
            {
                // A nonexistent child proves a duplicate exits before launching.
                if(Launcher.RunHidden(Path.Combine(root,"must-not-run.exe"),"",root,log)!=0)throw new Exception("Concurrent launch did not leave the active invocation alone.");
                if(File.ReadAllText(log)!=content)throw new Exception("Concurrent launch altered the active log.");
            }
            string common=Path.Combine(root,"common");string local=Path.Combine(root,"local");
            string modules=Environment.GetEnvironmentVariable("PSModulePath");
            try {
                Environment.SetEnvironmentVariable("PSModulePath",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"PowerShell","7","Modules"));
                string moduleLog=Path.Combine(root,"module-path.log");
                string shell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
                if(Launcher.RunHidden(shell,"-NoProfile -NonInteractive -Command \"(Get-Command Get-FileHash).Module.Path\"",root,moduleLog)!=0 ||
                    File.ReadAllText(moduleLog).IndexOf(@"WindowsPowerShell\v1.0\Modules\Microsoft.PowerShell.Utility",StringComparison.OrdinalIgnoreCase)<0)
                    throw new Exception("Inherited PowerShell 7 modules prevented the native Windows helper loading its checksum command.");
            }finally{Environment.SetEnvironmentVariable("PSModulePath",modules);}
            using(var identity=System.Security.Principal.WindowsIdentity.GetCurrent())
            {
                if(identity.User.Value!="S-1-5-18")
                    foreach(var mode in new[]{new[]{"--machine-setup"},new[]{"--machine-installer","Office"}})
                    {
                        try{Launcher.Machine(common,mode);throw new Exception("An ordinary user reached the machine installer.");}
                        catch(UnauthorizedAccessException){}
                    }
                if(!new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
                {
                    try{Launcher.RegisterSetup(common);throw new Exception("An ordinary user reached elevated setup registration.");}catch(UnauthorizedAccessException){}
                }
            }
            string selected=Path.Combine(local,"Wallpapers","selected.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(selected));File.WriteAllText(selected,"waterfall.ice");
            string wallpaper=Path.Combine(common,"TechLoungeOS","Wallpapers");Directory.CreateDirectory(wallpaper);
            if(Launcher.RefreshWallpaper(common,local)!=0)throw new Exception("Missing optional wallpaper helper must leave completed setup alone.");
            File.Copy(args[1],Path.Combine(wallpaper,"Personalise.exe"));
            if(Launcher.RefreshWallpaper(common,local)!=17)throw new Exception("Wallpaper refresh result was lost.");
            string refreshLog=File.ReadAllText(Path.Combine(local,"wallpaper-refresh.log"));
            if(!refreshLog.Contains("--refresh") || !refreshLog.Contains("console=0"))throw new Exception("Wallpaper repair must request only a hidden managed-frame refresh.");
            var timer=Stopwatch.StartNew();
            if(Launcher.RunHidden(args[1],"--wait",root,Path.Combine(root,"bounded.log"),200)!=124 || timer.ElapsedMilliseconds>5000)throw new Exception("Only the bounded helper must stop when its deadline expires.");
            string folder=Path.Combine(common,"TechLoungeOS","TechLoungeTweaks");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"TechLoungeTweaks.exe"),"inert; never launched");
            string runtime=Path.Combine(folder,"WebView2Runtime");Directory.CreateDirectory(runtime);
            try{Launcher.Tweaks(common,local);throw new Exception("Missing runtime should fail before launch.");}catch(FileNotFoundException){}
            File.WriteAllText(Path.Combine(runtime,"msedgewebview2.exe"),"inert; never launched");
            ProcessStartInfo info=Launcher.Tweaks(common,local);
            var elevation=Launcher.TweaksElevation(Path.Combine(root,"FirstLogin.exe"));
            if(!elevation.UseShellExecute || elevation.Verb!="runas" || elevation.Arguments!="--open-tweaks" || elevation.FileName!=Path.Combine(root,"FirstLogin.exe"))throw new Exception("Explicit Tweaks click must elevate its integration launcher before selecting the private runtime.");
            if(!info.UseShellExecute||info.FileName!=Path.Combine(folder,"TechLoungeTweaks.exe")||Environment.GetEnvironmentVariable("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER")!=runtime||Environment.GetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER")!=Path.Combine(local,"TechLoungeOS","WebView2"))throw new Exception("Private runtime or normal explicit-click elevation was not retained.");
            string stableRuntime=Path.Combine(common,"TechLoungeOS","WebView2Runtime");Directory.Move(runtime,stableRuntime);
            Directory.Move(folder,folder+".previous");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"TechLoungeTweaks.exe"),"updated inert fixture");
            Launcher.Tweaks(common,local);
            if(Environment.GetEnvironmentVariable("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER")!=stableRuntime || !File.Exists(Path.Combine(stableRuntime,"msedgewebview2.exe")))throw new Exception("Replacing the Tweaks app directory must retain its Builder-owned runtime.");
            using(var progress=new SetupProgress(root,delegate{return 0;},false))
            {
                if(progress.Font.FontFamily.Name!="Sora")throw new Exception("Packaged regular setup font did not load.");
                var title=FindControl(progress,"Finishing your setup");
                var task=FindControl(progress,"Preparing your selected apps");
                var hide=FindControl(progress,"Hide");
                if(title==null||task==null||hide==null||title.Font.FontFamily.Name!="Sora SemiBold"||task.Font.FontFamily.Name!="Sora SemiBold"||hide.Font.FontFamily.Name!="Sora")
                    throw new Exception("Setup headings, status and buttons must use the packaged real Sora weights.");
                progress.StartPosition=System.Windows.Forms.FormStartPosition.Manual;progress.Location=new System.Drawing.Point(-20000,-20000);progress.Opacity=0;progress.ShowInTaskbar=false;
                progress.Show();progress.PerformLayout();System.Windows.Forms.Application.DoEvents();
                CheckDescenders(title);
                var originalFont=title.Font;
                foreach(float scale in new[]{1.25f,1.5f,2f})
                using(var scaled=new System.Drawing.Font(originalFont.FontFamily,originalFont.Size*scale,originalFont.Style))
                {
                    try{title.Font=scaled;progress.PerformLayout();System.Windows.Forms.Application.DoEvents();CheckDescenders(title);}
                    finally{title.Font=originalFont;}
                }
                progress.PerformLayout();System.Windows.Forms.Application.DoEvents();
                if(!System.Windows.Forms.SystemInformation.HighContrast)using(var buttonFrame=new System.Drawing.Bitmap(hide.Width,hide.Height)){
                    hide.DrawToBitmap(buttonFrame,hide.ClientRectangle);
                    if(buttonFrame.GetPixel(0,0).ToArgb()!=hide.Parent.BackColor.ToArgb())throw new Exception("Rounded action corners leaked a transparent or black rectangular background.");
                }
                File.WriteAllText(Path.Combine(root,"descender-checks.txt"),"Unrestricted native glyph comparison passed at 21, 26.25, 31.5 and 42 point Sora heading sizes. No clipped lower strokes; these are font-layout fixtures, not physical DPI monitor tests.");
                using(var frame=new System.Drawing.Bitmap(progress.Width,progress.Height)){
                    progress.DrawToBitmap(frame,new System.Drawing.Rectangle(System.Drawing.Point.Empty,progress.Size));
                    int textPixels=0;for(int y=50;y<frame.Height-50;y++)for(int x=20;x<frame.Width-20;x++){var pixel=frame.GetPixel(x,y);if(pixel.R>170&&pixel.G>170&&pixel.B>170)textPixels++;}
                    if(textPixels<300)throw new Exception("Setup progress did not render readable content.");
                    frame.Save(Path.Combine(root,"setup-progress.png"),System.Drawing.Imaging.ImageFormat.Png);
                }
                progress.Close();
            }
            var status=Path.Combine(root,"progress-fixture.json");File.WriteAllText(status,"{\"step\":\"Office\",\"phase\":\"installing\"}");
            using(var reader=new FileStream(status,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            {
                string next=status+".tmp";File.WriteAllText(next,"{\"step\":\"Discord\",\"phase\":\"installing\"}");File.Replace(next,status,null);
                if((string)SetupProgress.ReadProgress(status)["step"]!="Discord")throw new Exception("Progress reader did not follow an atomic update.");
            }
            File.WriteAllText(Path.Combine(root,"progress-checks.txt"),"Packaged Sora faces (Regular and SemiBold), off-screen layout/render and atomic progress updates passed. No installer or live progress window started.");
            CheckProgressInteraction(root);
            File.WriteAllText(Path.Combine(root,"checks.txt"),"Hidden console, stdout/stderr, exit code, Unicode/quoting, duplicate launch, bounded wallpaper refresh, missing runtime and app-scoped environment passed.");return 0;
        }

        private static System.Windows.Forms.Control FindControl(System.Windows.Forms.Control root,string text)
        {
            if(root.Text==text)return root;
            foreach(System.Windows.Forms.Control control in root.Controls)
            {
                var found=FindControl(control,text);if(found!=null)return found;
            }
            return null;
        }

        private static void CheckDescenders(System.Windows.Forms.Control title)
        {
            // Compare with unrestricted native text rendering, including ink below the baseline.
            using(var reference=new System.Drawing.Bitmap(title.Width,Math.Max(120,title.Height*3)))
            using(var graphics=System.Drawing.Graphics.FromImage(reference))
            {
                graphics.Clear(title.BackColor);
                System.Windows.Forms.TextRenderer.DrawText(graphics,title.Text,title.Font,
                    new System.Drawing.Rectangle(0,0,reference.Width,reference.Height),title.ForeColor,
                    System.Windows.Forms.TextFormatFlags.Top|System.Windows.Forms.TextFormatFlags.Left|
                    System.Windows.Forms.TextFormatFlags.NoPrefix|System.Windows.Forms.TextFormatFlags.NoPadding|System.Windows.Forms.TextFormatFlags.WordBreak);
                int lastInk=-1;
                for(int y=0;y<reference.Height;y++)for(int x=0;x<reference.Width;x++)
                    if(reference.GetPixel(x,y).ToArgb()!=title.BackColor.ToArgb())lastInk=y;
                if(lastInk<0||lastInk+1>=title.Height)
                    throw new Exception("Setup heading clips its descenders: ink ends at "+lastInk+", available height "+title.Height);
            }
        }

        private static void PumpUntil(Func<bool> ready)
        {
            var timer=Stopwatch.StartNew();
            while(!ready())
            {
                if(timer.Elapsed.TotalSeconds>5)throw new TimeoutException("The inert setup-progress interaction did not settle.");
                System.Windows.Forms.Application.DoEvents();System.Threading.Thread.Sleep(10);
            }
            System.Windows.Forms.Application.DoEvents();
        }

        private static void CheckProgressInteraction(string root)
        {
            int attempts=0;
            using(var started=new System.Threading.ManualResetEvent(false))
            using(var release=new System.Threading.ManualResetEvent(false))
            using(var progress=new SetupProgress(root,delegate {
                int attempt=System.Threading.Interlocked.Increment(ref attempts);
                if(attempt==1){started.Set();if(!release.WaitOne(5000))throw new TimeoutException("Fixture release not received");return 1;}
                return 0;
            }))
            {
                try
                {
                    progress.StartPosition=System.Windows.Forms.FormStartPosition.Manual;
                    progress.Location=new System.Drawing.Point(-20000,-20000);progress.Opacity=0;progress.ShowInTaskbar=false;
                    progress.Show();PumpUntil(delegate{return started.WaitOne(0);});
                    var retry=FindControl(progress,"Retry setup") as System.Windows.Forms.Button;
                    var hide=FindControl(progress,"Hide") as System.Windows.Forms.Button;
                    if(retry==null || hide==null || retry.Enabled)throw new Exception("Running setup exposed an enabled retry action.");
                    hide.PerformClick();
                    if(progress.Visible || attempts!=1)throw new Exception("Hide did not retain the running setup.");
                    progress.Close();
                    if(progress.IsDisposed)throw new Exception("Closing progress aborted its running observer.");
                    release.Set();PumpUntil(delegate{return retry.Enabled;});
                    if(!progress.Visible || attempts!=1)throw new Exception("A failed step did not restore retry/progress without another attempt.");
                    retry.PerformClick();retry.PerformClick();
                    PumpUntil(delegate{return attempts==2 && !retry.Enabled;});
                    // Drain the worker's completion callback before disposal.
                    PumpUntil(delegate{return FindControl(progress,"You’re ready")!=null;});
                    if(attempts!=2)throw new Exception("Repeated retry clicks started duplicate work.");
                    progress.Close();
                    if(!progress.IsDisposed)throw new Exception("Completed progress did not close cleanly.");
                }
                finally{release.Set();}
            }
            File.WriteAllText(Path.Combine(root,"progress-interaction-checks.txt"),"Real Framework message loop: responsive hidden work, hide/close retention, failure visibility, successful retry and duplicate-click protection passed with inert work only.");
        }
    }
}
