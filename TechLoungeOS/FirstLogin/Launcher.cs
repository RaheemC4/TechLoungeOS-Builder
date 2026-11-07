using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;

namespace TechLoungeOS.FirstLogin
{
    internal static class Launcher
    {
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]
        private static extern int MessageBox(IntPtr owner,string message,string title,uint flags);

        internal static string Quote(string value)
        {
            var text=new StringBuilder("\"");int slashes=0;
            foreach(char c in value)
            {
                if(c=='\\'){slashes++;continue;}
                text.Append('\\',c=='"'?slashes*2+1:slashes);text.Append(c);slashes=0;
            }
            text.Append('\\',slashes*2);return text.Append('"').ToString();
        }

        internal static int RunHidden(string executable,string arguments,string workingDirectory,string log,int timeoutMilliseconds=0)
        {
            if(timeoutMilliseconds<0)throw new ArgumentOutOfRangeException("timeoutMilliseconds");
            Directory.CreateDirectory(Path.GetDirectoryName(log));
            FileStream bootstrapLock;
            try { bootstrapLock=new FileStream(log+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None); }
            catch(IOException error) { if((error.HResult&0xffff)==32)return 0;throw; }
            // A duplicate sign-in invocation must not open the same log or show
            // an error dialog before the script's own provisioning lock runs.
            using(bootstrapLock)
            using(var stream=new FileStream(log,File.Exists(log)&&new FileInfo(log).Length<1048576?FileMode.Append:FileMode.Create,FileAccess.Write,FileShare.Read))
            using(var writer=new StreamWriter(stream,new UTF8Encoding(false)))
            using(var process=new Process())
            using(var activity=new InstallerActivity())
            {
                if(!activity.Active)writer.WriteLine("Windows did not accept the temporary setup power request. Installation will continue; keep the PC awake until it completes.");
                var gate=new object();int written=0;bool truncated=false;
                DataReceivedEventHandler collect=(sender,e)=>{
                    if(e.Data==null)return;
                    lock(gate)
                    {
                        // Always drain both pipes even if the log drive fills.
                        try
                        {
                            if(written+e.Data.Length<1048576){writer.WriteLine(e.Data);written+=e.Data.Length+2;}
                            else if(!truncated){writer.WriteLine("Further bootstrap output omitted. See first-login.log.");truncated=true;}
                        }
                        catch(IOException){ }
                        catch(UnauthorizedAccessException){ }
                    }
                };
                process.StartInfo=new ProcessStartInfo(executable,arguments){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=new UTF8Encoding(false),StandardErrorEncoding=new UTF8Encoding(false),WorkingDirectory=workingDirectory};
                if(Path.GetFileName(executable).Equals("powershell.exe",StringComparison.OrdinalIgnoreCase))
                    process.StartInfo.EnvironmentVariables["PSModulePath"]=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","Modules")+";"+Environment.GetEnvironmentVariable("PSModulePath");
                process.OutputDataReceived+=collect;process.ErrorDataReceived+=collect;
                process.Start();process.BeginOutputReadLine();process.BeginErrorReadLine();
                // Only the optional wallpaper refresh has a deadline. App installers retain the unlimited wait.
                if(timeoutMilliseconds>0 && !process.WaitForExit(timeoutMilliseconds))
                {
                    process.Kill();process.WaitForExit();return 124;
                }
                process.WaitForExit();return process.ExitCode;
            }
        }

        internal static int RefreshWallpaper(string common,string state)
        {
            string helper=Path.Combine(common,"TechLoungeOS","Wallpapers","Personalise.exe");
            if(!File.Exists(helper) || !File.Exists(Path.Combine(state,"Wallpapers","selected.txt")))return 0;
            try {return RunHidden(helper,"--refresh",Path.GetDirectoryName(helper),Path.Combine(state,"wallpaper-refresh.log"),15000);}
            catch(Exception error)
            {
                // A nonessential background refresh must not turn a completed setup into sign-in popups.
                try {File.WriteAllText(Path.Combine(state,"wallpaper-refresh-status.txt"),error.Message);}catch(IOException){}catch(UnauthorizedAccessException){}
                return 1;
            }
        }

        internal static ProcessStartInfo Tweaks(string common,string local)
        {
            string folder=Path.Combine(common,"TechLoungeOS","TechLoungeTweaks");
            string exe=Path.Combine(folder,"TechLoungeTweaks.exe");
            if(!File.Exists(exe))throw new FileNotFoundException("TechLoungeTweaks is missing. Restore the complete integration bundle.",exe);
            // Keep the Builder-owned runtime outside the directory replaced by
            // the unchanged Tweaks updater. Retain older integration layouts.
            string runtime=Path.Combine(common,"TechLoungeOS","WebView2Runtime");
            if(!Directory.Exists(runtime))runtime=Path.Combine(folder,"WebView2Runtime");
            if(Directory.Exists(runtime))
            {
                if(!File.Exists(Path.Combine(runtime,"msedgewebview2.exe")))throw new FileNotFoundException("The app-private WebView2 runtime is missing. Restore the complete integration bundle.");
                // These variables affect only this short-lived process and its
                // launched app. Nothing is written to the user/machine environment.
                Environment.SetEnvironmentVariable("WEBVIEW2_BROWSER_EXECUTABLE_FOLDER",runtime);
                Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER",Path.Combine(local,"TechLoungeOS","WebView2"));
            }
            // Preserve the app's own normal elevation prompt on an explicit click.
            return new ProcessStartInfo(exe){UseShellExecute=true,WorkingDirectory=folder};
        }

        internal static ProcessStartInfo TweaksElevation(string launcher)
        {
            return new ProcessStartInfo(launcher,"--open-tweaks"){UseShellExecute=true,Verb="runas",WorkingDirectory=Path.GetDirectoryName(launcher)};
        }

        internal static int Machine(string common,string[] args)
        {
            using(var identity=WindowsIdentity.GetCurrent())
                if(identity.User.Value!="S-1-5-18")throw new UnauthorizedAccessException("Machine installation can only run through its fixed Windows Setup task.");
            string root=Path.Combine(common,"TechLoungeOS","MachineSetup");
            string system=Environment.GetFolderPath(Environment.SpecialFolder.System);
            string powershell=Path.Combine(system,"WindowsPowerShell","v1.0","powershell.exe");
            if(args.Length==1 && args[0]=="--machine-setup")
                return RunHidden(powershell,"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "+Quote(Path.Combine(root,"worker.ps1")),root,Path.Combine(root,"bootstrap.log"));
            if(args.Length!=2 || args[0]!="--machine-installer" || Array.IndexOf(new[]{"Runtimes","Edge","StoreXbox","HWiNFO","LegacyNetwork","Recovery","Office"},args[1])<0)
                throw new ArgumentException("Unknown fixed machine installer.");
            string folder=Path.Combine(root,"payloads",args[1]);
            string script=Path.Combine(folder,args[1]=="Office"?"TechLoungeOS-Office.cmd":"install.ps1");
            if(!File.Exists(script))throw new FileNotFoundException("The verified machine installer cache is incomplete.",script);
            return args[1]=="Office"
                ? RunHidden(Path.Combine(system,"cmd.exe"),"/d /s /c \"\""+script+"\"\"",folder,Path.Combine(root,"Office-bootstrap.log"))
                : RunHidden(powershell,"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "+Quote(script),folder,Path.Combine(root,args[1]+"-bootstrap.log"));
        }

        internal static int RegisterSetup(string common)
        {
            using(var identity=WindowsIdentity.GetCurrent())
                if(!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))throw new UnauthorizedAccessException("Setup registration requires the account wizard's elevated Windows Setup callback.");
            string root=Path.Combine(common,"TechLoungeOS");
            string powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
            // Registration and small registry preferences only. Long installers never run here.
            foreach(string name in new[]{"desktop-machine.ps1","recommended-machine.ps1","MachineSetup\\queue.ps1","BootOptimizer\\queue.ps1","PowerDefaults\\queue.ps1","PowerDefaults\\initial.ps1"})
            {
                string file=Path.Combine(root,name);
                if(!File.Exists(file))continue;
                if(name.EndsWith("queue.ps1",StringComparison.Ordinal) && File.Exists(Path.Combine(Path.GetDirectoryName(file),"queue-ready")))continue;
                int code=RunHidden(powershell,"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "+Quote(file),root,
                    Path.Combine(root,"account-setup","registration-"+name.Replace('\\','-')+".log"));
                if(code!=0)return code;
            }
            foreach(string name in new[]{"TechLoungeOS-Nvidia","TechLoungeOS-Graphics"})
            {
                string file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"Setup",name,"queue.ps1");
                if(!File.Exists(file))continue;
                int code=RunHidden(powershell,"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "+Quote(file),root,
                    Path.Combine(root,"account-setup","registration-"+name+".log"));
                if(code!=0)return code;
            }
            return 0;
        }

        [STAThread]
        private static int Main(string[] args)
        {
            string common=Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string state=Path.Combine(local,"TechLoungeOS");
            bool machine=args.Length>0 && (args[0]=="--machine-setup" || args[0]=="--machine-installer");
            bool registration=args.Length==1 && args[0]=="--register-setup";
            try
            {
                if(machine)return Machine(common,args);
                if(registration)return RegisterSetup(common);
                if(args.Length==1&&args[0]=="--open-tweaks")
                {
                    // Elevate the integration launcher before setting the private
                    // runtime environment. AppInfo elevation of Tweaks itself
                    // drops those variables and can leave its window hidden.
                    // This branch only runs after an explicit Tweaks click;
                    // automatic first-login provisioning never requests elevation.
                    using(var identity=WindowsIdentity.GetCurrent())
                    {
                        bool elevated=new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                        var start=elevated?Tweaks(common,local):TweaksElevation(System.Reflection.Assembly.GetExecutingAssembly().Location);
                        using(var app=Process.Start(start)){}
                    }
                    return 0;
                }
                if(args.Length!=0)throw new ArgumentException("Unknown launcher option.");
                if(File.Exists(Path.Combine(state,"complete"))){RefreshWallpaper(common,state);return 0;}
                if(!DesktopReady.Wait())
                {
                    Directory.CreateDirectory(state);
                    File.WriteAllText(Path.Combine(state,"bootstrap-status.txt"),"Setup is waiting for a visible Windows desktop. No app installer was started. Retry after the desktop is ready.");
                    return 1;
                }
                string script=Path.Combine(common,"TechLoungeOS","first-login.ps1");
                if(!File.Exists(script))throw new FileNotFoundException("Windows setup files are missing. Restore the TechLoungeOS installation payload.",script);
                string powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
                Directory.CreateDirectory(state);
                FileStream progressLock;
                try{progressLock=new FileStream(Path.Combine(state,"progress.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
                catch(IOException error){if((error.HResult&0xffff)==32)return 0;throw;}
                int code;
                using(progressLock)code=SetupProgress.Run(state,delegate {return RunHidden(powershell,"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "+Quote(script),Path.GetDirectoryName(script),Path.Combine(state,"bootstrap.log"));});
                // Ordinary incomplete/offline steps already have status/logs and
                // retry markers. Avoid repeated sign-in message boxes for these.
                if(code!=0)File.WriteAllText(Path.Combine(state,"bootstrap-status.txt"),"Setup needs attention. Read status.txt and first-login.log in this folder. Failed steps retry at the next sign-in. Exit "+code);
                else RefreshWallpaper(common,state);
                return code;
            }
            catch(Exception error)
            {
                try{Directory.CreateDirectory(state);File.WriteAllText(Path.Combine(state,"bootstrap-status.txt"),error.Message);}catch(IOException){}catch(UnauthorizedAccessException){}
                if(!machine && !registration)MessageBox(IntPtr.Zero,error.Message+"\n\nDetails: "+Path.Combine(state,"bootstrap-status.txt"),"TechLoungeOS setup needs attention",0x10);
                return 1;
            }
        }
    }

    // The synchronous caller owns this thread-scoped request until its child exits.
    // Screens may turn off and explicit Sleep remains available. No power plan is changed.
    internal sealed class InstallerActivity : IDisposable
    {
        private readonly Func<uint,uint> setState;
        private uint previous;
        internal bool Active { get { return previous!=0; } }
        [DllImport("kernel32.dll",ExactSpelling=true)]
        private static extern uint SetThreadExecutionState(uint flags);

        internal InstallerActivity() : this(SetThreadExecutionState) { }
        internal InstallerActivity(Func<uint,uint> setState)
        {
            this.setState=setState;
            previous=setState(0x80000001); // ES_CONTINUOUS | ES_SYSTEM_REQUIRED
            if(previous!=0)setState(previous|0x80000001);
        }
        public void Dispose()
        {
            if(previous==0)return;
            uint restore=previous;previous=0;
            setState(restore|0x80000000);
        }
    }
}
