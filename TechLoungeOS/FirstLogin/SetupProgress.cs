using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace TechLoungeOS.FirstLogin
{
    // A finite first-start surface. Closing it never terminates an installer.
    internal sealed class SetupProgress : Form
    {
        private readonly string state;
        private readonly Func<int> work;
        private readonly Label heading, step, detail,brand,subtitle,elapsed,notice;
        private readonly Button retry,restart,finish;
        private readonly ProgressTrack track;
        private readonly System.Windows.Forms.Timer timer;
        private readonly NotifyIcon tray;
        private readonly PrivateFontCollection fonts = new PrivateFontCollection();
        private readonly List<IntPtr> fontMemory = new List<IntPtr>();
        private readonly List<IntPtr> fontHandles = new List<IntPtr>();
        private Thread worker;
        private volatile bool running;
        private int result = 1;
        private bool resourcesReleased;
        private readonly List<Font> ownedFonts = new List<Font>();
        private readonly Stopwatch elapsedClock=new Stopwatch();
        private Form activityWindow;
        private readonly string[] plannedSteps;

        [DllImport("gdi32.dll")] private static extern IntPtr AddFontMemResourceEx(IntPtr data,uint length,IntPtr reserved,ref uint count);
        [DllImport("gdi32.dll")] private static extern bool RemoveFontMemResourceEx(IntPtr handle);
        [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
        [DllImport("user32.dll")] private static extern bool SystemParametersInfo(uint action,uint parameter,out bool value,uint flags);

        internal static bool Motion { get { bool allowed; return !SystemInformation.HighContrast && SystemParametersInfo(0x1042,0,out allowed,0) && allowed; } }
        internal SetupProgress(string state,Func<int> work,bool startOnShown=true)
        {
            this.state=state;this.work=work;
            plannedSteps=ReadPlan(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"TechLoungeOS","progress-plan.json"));
            Text="TechLoungeOS setup";ShowInTaskbar=true;StartPosition=FormStartPosition.CenterScreen;
            ClientSize=new Size(620,470);MinimumSize=new Size(460,410);AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
            FormBorderStyle=SystemInformation.HighContrast?FormBorderStyle.FixedSingle:FormBorderStyle.None;
            DoubleBuffered=true;
            MaximizeBox=false;ShowIcon=true;Icon=Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location);
            BackColor=SystemInformation.HighContrast?SystemColors.Window:Color.FromArgb(20,23,29);
            ForeColor=SystemInformation.HighContrast?SystemColors.WindowText:Color.FromArgb(236,240,246);
            LoadFont("TechLoungeOS.SoraRegular.ttf");LoadFont("TechLoungeOS.SoraSemibold.ttf");
            Font=Face("Sora",10.5f);
            var layout=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(28,14,28,22),ColumnCount=1,RowCount=7,AutoScroll=true};
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,38));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            foreach(float height in new[]{112f,30f,14f})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
            var caption=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
            caption.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));caption.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,34));
            brand=new Label {Text="TechLoungeOS",Font=Face("Sora SemiBold",9),Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=Subtle};
            brand.MouseDown+=delegate(object sender,MouseEventArgs e){if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0xA1,(IntPtr)2,IntPtr.Zero);}};
            var dismiss=new SmoothButton {Text="×",AccessibleName="Hide setup",Width=32,Height=30,Quiet=true,Margin=Padding.Empty};dismiss.Click+=delegate{if(running)Hide();else Close();};
            caption.Controls.Add(brand,0,0);caption.Controls.Add(dismiss,1,0);
            var titles=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Margin=new Padding(0,0,0,12)};
            titles.RowStyles.Add(new RowStyle(SizeType.AutoSize));titles.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            heading=new AnimatedLabel {Text="Finishing your setup",Dock=DockStyle.Fill,Font=Face("Sora SemiBold",21),AutoSize=true,AutoEllipsis=true,Margin=Padding.Empty};
            subtitle=new Label {Text="Your desktop is ready to use.",Dock=DockStyle.Fill,AutoSize=true,ForeColor=Subtle,Margin=new Padding(0,3,0,0)};
            titles.Controls.Add(heading,0,0);titles.Controls.Add(subtitle,0,1);
            var status=new RoundedStatusPanel {Dock=DockStyle.Fill,Padding=new Padding(18),Margin=Padding.Empty};
            var statusLayout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,BackColor=status.BackColor};
            statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,30));statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            step=new AnimatedLabel {Text="Preparing your selected apps",Dock=DockStyle.Fill,Font=Face("Sora SemiBold",11),AutoEllipsis=true,Margin=Padding.Empty};
            detail=new AnimatedLabel {Text="Successful installs are saved as we go.",Dock=DockStyle.Fill,ForeColor=Subtle,AutoEllipsis=true,Margin=new Padding(0,5,0,0)};
            statusLayout.Controls.Add(step,0,0);statusLayout.Controls.Add(detail,0,1);status.Controls.Add(statusLayout);
            track=new ProgressTrack {Dock=DockStyle.Fill,Margin=Padding.Empty};
            var actions=new FlowLayoutPanel {Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=true,Padding=new Padding(0,4,0,0),Margin=Padding.Empty};
            var hide=new SmoothButton {Text="Hide",Width=94,Primary=true};hide.Click+=delegate {if(running)Hide();else Close();};
            finish=hide;
            restart=new SmoothButton {Text="Restart now",Width=135,Visible=false,Primary=true};
            restart.Click+=delegate {
                if(running||result!=0)return;
                try{Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"shutdown.exe"),"/r /t 0"){UseShellExecute=false,CreateNoWindow=true});}
                catch(Exception error){detail.Text="Could not request a restart: "+error.Message;}
            };
            retry=new SmoothButton {Text="Retry setup",Width=125,Enabled=false,Visible=false,Primary=true};retry.Click+=delegate {StartWork();};
            var logs=new SmoothButton {Text="View activity",Width=135};logs.Click+=delegate {ShowActivity();};
            elapsed=new Label {Dock=DockStyle.Fill,ForeColor=Subtle,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true};
            notice=new Label {Text="Keep this PC on and connected. Don’t shut down or restart until setup finishes.",Dock=DockStyle.Fill,ForeColor=Subtle,Padding=new Padding(0,10,0,0)};
            actions.Controls.Add(hide);actions.Controls.Add(restart);actions.Controls.Add(retry);actions.Controls.Add(logs);
            layout.Controls.Add(caption,0,0);layout.Controls.Add(titles,0,1);layout.Controls.Add(status,0,2);layout.Controls.Add(elapsed,0,3);layout.Controls.Add(track,0,4);layout.Controls.Add(notice,0,5);layout.Controls.Add(actions,0,6);Controls.Add(layout);
            tray=new NotifyIcon {Icon=Icon??SystemIcons.Application,Text="TechLoungeOS setup",Visible=false};
            tray.DoubleClick+=delegate {Show();WindowState=FormWindowState.Normal;Activate();};
            var menu=new ContextMenuStrip {Font=Face("Sora",9)};menu.Items.Add("Show setup progress",null,delegate {Show();WindowState=FormWindowState.Normal;Activate();});tray.ContextMenuStrip=menu;
            timer=new System.Windows.Forms.Timer {Interval=500};timer.Tick+=delegate {RefreshProgress();};timer.Start();
            Shown+=delegate {if(startOnShown){tray.Visible=true;StartWork();}};
            FormClosing+=delegate(object sender,FormClosingEventArgs e){if(running && e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Hide();}};
        }

        [DllImport("user32.dll")]private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]private static extern IntPtr SendMessage(IntPtr hwnd,uint message,IntPtr wparam,IntPtr lparam);
        private static Color Subtle {get{return SystemInformation.HighContrast?SystemColors.WindowText:Color.FromArgb(165,178,197);}}
        private Font Face(string name,float size){foreach(var family in fonts.Families)if(family.Name==name){var face=new Font(family,size,FontStyle.Regular);ownedFonts.Add(face);return face;}throw new IOException("The packaged Sora font did not load: "+name);}
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);if(!SystemInformation.HighContrast&&Width>0&&Height>0){var old=Region;using(var path=SetupDrawing.Rounded(new RectangleF(0,0,Width,Height),18*DeviceDpi/96f))Region=new Region(path);if(old!=null)old.Dispose();}Invalidate();}
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(!SystemInformation.HighContrast){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var path=SetupDrawing.Rounded(new RectangleF(.5f,.5f,Width-1,Height-1),18*DeviceDpi/96f))using(var pen=new Pen(Color.FromArgb(53,61,74)))e.Graphics.DrawPath(pen,path);}}

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark=SystemInformation.HighContrast?0:1;
            try{DwmSetWindowAttribute(Handle,20,ref dark,4);}catch(DllNotFoundException){}
        }
        protected override bool ShowWithoutActivation {get{return true;}}
        protected override void OnSystemColorsChanged(EventArgs e)
        {
            base.OnSystemColorsChanged(e);
            BackColor=SystemInformation.HighContrast?SystemColors.Window:Color.FromArgb(20,23,29);
            ForeColor=SystemInformation.HighContrast?SystemColors.WindowText:Color.FromArgb(236,240,246);
            foreach(var label in new[]{brand,subtitle,detail})if(label!=null)label.ForeColor=Subtle;
            FormBorderStyle=SystemInformation.HighContrast?FormBorderStyle.FixedSingle:FormBorderStyle.None;
            if(SystemInformation.HighContrast){var previous=Region;Region=null;if(previous!=null)previous.Dispose();}else OnSizeChanged(EventArgs.Empty);
            Invalidate(true);
        }

        private void LoadFont(string name)
        {
            using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if(stream==null)throw new IOException("The setup font was not packaged.");
                var bytes=new byte[checked((int)stream.Length)];int read=0;
                while(read<bytes.Length){int count=stream.Read(bytes,read,bytes.Length-read);if(count==0)throw new EndOfStreamException();read+=count;}
                var memory=Marshal.AllocHGlobal(bytes.Length);fontMemory.Add(memory);Marshal.Copy(bytes,0,memory,bytes.Length);
                fonts.AddMemoryFont(memory,bytes.Length);uint faces=0;var handle=AddFontMemResourceEx(memory,(uint)bytes.Length,IntPtr.Zero,ref faces);
                if(handle==IntPtr.Zero || faces==0)throw new IOException("The setup font could not be loaded.");fontHandles.Add(handle);
            }
        }

        private void StartWork()
        {
            if(running)return;
            running=true;elapsedClock.Restart();restart.Visible=false;finish.Text="Hide";retry.Enabled=false;retry.Visible=false;heading.Text="Finishing your setup";track.State=SetupProgressState.Working;
            notice.Text="Keep this PC on and connected. Don’t shut down or restart until setup finishes.";
            worker=new Thread(new ThreadStart(delegate {
                int code;
                try{code=work();}catch(Exception error){code=1;try{File.WriteAllText(Path.Combine(state,"bootstrap-status.txt"),error.Message);}catch(IOException){}catch(UnauthorizedAccessException){}}
                try{if(!IsDisposed)BeginInvoke(new Action(delegate {
                    result=code;running=false;elapsedClock.Stop();retry.Enabled=code!=0;retry.Visible=code!=0;track.State=code==0?SetupProgressState.Complete:SetupProgressState.Attention;
                    notice.Text=code==0?"Setup has finished. You can restart when convenient.":"Successful installs are retained. Review activity and retry the unfinished steps.";
                    heading.Text=code==0?"You’re ready":"Setup needs attention";
                    step.Text=code==0?"Your selected apps and settings are ready.":"Successful installs are saved. Retry the remaining steps.";
                    finish.Text=code==0?"Done":"Hide";
                    if(code!=0){Show();WindowState=FormWindowState.Normal;}
                    RefreshProgress();
                }));}catch(InvalidOperationException){}
            }));
            worker.IsBackground=false;worker.Name="TechLoungeOS first-start installer observer";worker.Start();
        }

        internal static Dictionary<string,object> ReadProgress(string path)
        {
            using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            {
                if(input.Length>65536)throw new IOException("Unexpected setup status size.");
                using(var reader=new StreamReader(input)){var result=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(reader.ReadToEnd());if(result==null)throw new IOException("Empty setup progress.");return result;}
            }
        }

        private void RefreshProgress()
        {
            elapsed.Text="Elapsed "+elapsedClock.Elapsed.ToString(@"hh\:mm\:ss");
            if(plannedSteps.Length>0){
                int done=0;foreach(var id in plannedSteps)if(File.Exists(Path.Combine(state,id+".done")))done++;
                elapsed.Text+=" · "+done+" of "+plannedSteps.Length+" setup steps completed";
                track.Progress=(double)done/plannedSteps.Length;
            }
            track.State=running?SetupProgressState.Working:result==0?SetupProgressState.Complete:SetupProgressState.Attention;
            try {
                var status=ReadProgress(Path.Combine(state,"progress.json"));object value;
                if(running && status.TryGetValue("step",out value))step.Text=Friendly(Convert.ToString(value));
                if(status.TryGetValue("message",out value))detail.Text=Convert.ToString(value);
                if(plannedSteps.Length==0&&status.TryGetValue("completed",out value))elapsed.Text+=" · "+Convert.ToString(value)+" setup tasks completed";
                if(running && status.TryGetValue("stepStartedUtc",out value)){
                    DateTime since;if(DateTime.TryParse(Convert.ToString(value),System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.RoundtripKind,out since)){
                        var duration=DateTime.UtcNow-since.ToUniversalTime();if(duration>=TimeSpan.Zero)elapsed.Text+=" · This task "+duration.ToString(@"hh\:mm\:ss");
                    }
                }
                if(!running && result==0 && status.TryGetValue("phase",out value) && Convert.ToString(value)=="restart-needed"){
                    heading.Text="Restart when convenient";step.Text="Your apps are installed. Windows needs a normal restart.";
                    restart.Visible=true;finish.Text="Later";
                }
            }catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(InvalidOperationException){}
        }

        internal static string Friendly(string id)
        {
            switch(id){case "lock-screen":return "Your lock-screen background";case "recommended-user":return "Your Recommended settings";case "recommended-machine":return "Recommended Windows settings";case "nvidia-setup":return "NVIDIA driver, App and profile";case "Office":case "office":return "Microsoft 365";case "Runtimes":return "App and gaming runtimes";case "StoreXbox":return "Microsoft Store and Xbox";case "Edge":return "Microsoft Edge";case "Recovery":return "Windows recovery";case "LegacyNetwork":return "Network compatibility";case "TechLounge.DiscordOpenAsar":return "Discord";case "Brave.Brave":return "Brave";case "Google.Chrome":return "Google Chrome";case "Valve.Steam":return "Steam";case "Guru3D.Afterburner":return "MSI Afterburner";case "Stremio.Stremio":return "Stremio";case "Proton.ProtonVPN":return "Proton VPN";case "TechLounge.ChatGPT":return "ChatGPT";case "TechLounge.HatchZip":return "HatchZip";case "REALiX.HWiNFO":case "HWiNFO":return "HWiNFO64";case "machine-setup":return "Preparing required installers";default:return string.IsNullOrEmpty(id)?"Finishing your setup":id.Replace('-',' ');}
        }

        private void ShowActivity()
        {
            if(activityWindow!=null&&!activityWindow.IsDisposed){activityWindow.Activate();return;}
            var window=new Form {Text="TechLoungeOS setup activity",ClientSize=new Size(700,480),MinimumSize=new Size(500,340),StartPosition=FormStartPosition.CenterParent,BackColor=BackColor,ForeColor=ForeColor,Font=Font,ShowInTaskbar=false};
            activityWindow=window;
            var layout=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=4};
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,56));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
            var summary=new Label {Dock=DockStyle.Fill,AutoSize=true,Font=step.Font,Margin=new Padding(0,0,0,8)};
            var timing=new Label {Dock=DockStyle.Fill,ForeColor=Subtle};
            var output=new TextBox {Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BackColor=BackColor,ForeColor=ForeColor,BorderStyle=BorderStyle.FixedSingle};
            var close=new SmoothButton {Text="Close activity",Width=150,Dock=DockStyle.Right};close.Click+=delegate {window.Close();};
            layout.Controls.Add(summary,0,0);layout.Controls.Add(timing,0,1);layout.Controls.Add(output,0,2);layout.Controls.Add(close,0,3);window.Controls.Add(layout);
            var refresh=new System.Windows.Forms.Timer {Interval=1000};
            EventHandler update=delegate {
                summary.Text=step.Text;timing.Text=elapsed.Text+Environment.NewLine+(running?"Keep this PC on. Installers continue when this activity window is closed.":notice.Text);
                try{var text=ReadActivityTail(state);if(output.Text!=text){output.Text=text;output.SelectionStart=output.TextLength;output.ScrollToCaret();}}
                catch(IOException error){output.Text="Activity is temporarily unavailable: "+error.Message;}catch(UnauthorizedAccessException error){output.Text=error.Message;}
            };
            refresh.Tick+=update;window.Shown+=delegate {update(null,EventArgs.Empty);refresh.Start();};window.FormClosed+=delegate {refresh.Dispose();activityWindow=null;};window.Show(this);
        }

        internal static string ReadActivityTail(string folder)
        {
            var parts=new List<string>();
            foreach(var name in new[]{"first-login.log","bootstrap.log","status.txt","lock-screen-error.txt"}){
                string path=Path.Combine(folder,name);if(!File.Exists(path))continue;
                using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
                    bool tail=input.Length>24576;if(tail)input.Seek(-24576,SeekOrigin.End);
                    using(var reader=new StreamReader(input)){if(tail)reader.ReadLine();parts.Add(name+Environment.NewLine+reader.ReadToEnd());}
                }
            }
            return parts.Count==0?"Waiting for the first installer message. Current work is shown above.":string.Join(Environment.NewLine+Environment.NewLine,parts.ToArray());
        }

        internal static string[] ReadPlan(string path)
        {
            if(!File.Exists(path))return new string[0];
            try{
                var plan=ReadProgress(path);object raw;if(!plan.TryGetValue("steps",out raw))return new string[0];
                var array=raw as System.Collections.IEnumerable;if(array==null)return new string[0];
                var ids=new List<string>();foreach(var item in array){string id=item as string;if(id==null||!System.Text.RegularExpressions.Regex.IsMatch(id,@"\A[A-Za-z0-9][A-Za-z0-9._-]{0,199}\z")||ids.Contains(id)||ids.Count>=500)return new string[0];ids.Add(id);}
                return ids.ToArray();
            }catch(IOException){return new string[0];}catch(UnauthorizedAccessException){return new string[0];}catch(ArgumentException){return new string[0];}catch(InvalidOperationException){return new string[0];}
        }

        internal static int Run(string state,Func<int> work)
        {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            using(var window=new SetupProgress(state,work)){Application.Run(window);return window.result;}
        }
        protected override void Dispose(bool disposing)
        {
            if(disposing&&!resourcesReleased){
                resourcesReleased=true;
                if(timer!=null)timer.Dispose();if(tray!=null){tray.Visible=false;if(tray.ContextMenuStrip!=null)tray.ContextMenuStrip.Dispose();tray.Dispose();}
                base.Dispose(true);
                foreach(var face in ownedFonts)face.Dispose();ownedFonts.Clear();
                fonts.Dispose();foreach(var handle in fontHandles)RemoveFontMemResourceEx(handle);fontHandles.Clear();
                foreach(var memory in fontMemory)Marshal.FreeHGlobal(memory);fontMemory.Clear();
            }
            else base.Dispose(disposing);
        }
    }

}
