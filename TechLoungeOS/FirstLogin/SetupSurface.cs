using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TechLoungeOS.FirstLogin
{
    internal static class SetupDrawing
    {
        internal static GraphicsPath Rounded(RectangleF bounds,float radius)
        {
            var path=new GraphicsPath();float d=Math.Min(radius*2,Math.Min(bounds.Width,bounds.Height));
            if(d<=0)return path;
            path.AddArc(bounds.Left,bounds.Top,d,d,180,90);path.AddArc(bounds.Right-d,bounds.Top,d,d,270,90);
            path.AddArc(bounds.Right-d,bounds.Bottom-d,d,d,0,90);path.AddArc(bounds.Left,bounds.Bottom-d,d,d,90,90);path.CloseFigure();return path;
        }
        internal static Color Mix(Color from,Color to,double amount)
        {
            amount=Math.Max(0,Math.Min(1,amount));
            return Color.FromArgb((int)Math.Round(from.R+(to.R-from.R)*amount),(int)Math.Round(from.G+(to.G-from.G)*amount),(int)Math.Round(from.B+(to.B-from.B)*amount));
        }
    }

    internal sealed class RoundedStatusPanel : Panel
    {
        internal RoundedStatusPanel(){BackColor=SystemInformation.HighContrast?SystemColors.Window:Color.FromArgb(28,33,42);DoubleBuffered=true;}
        protected override void OnSystemColorsChanged(EventArgs e){base.OnSystemColorsChanged(e);BackColor=SystemInformation.HighContrast?SystemColors.Window:Color.FromArgb(28,33,42);foreach(Control child in Controls)child.BackColor=BackColor;Invalidate(true);}
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent==null?BackColor:Parent.BackColor);
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var shape=SetupDrawing.Rounded(new RectangleF(.5f,.5f,Width-1,Height-1),13*DeviceDpi/96f)){
                using(var fill=new SolidBrush(SystemInformation.HighContrast?SystemColors.Window:BackColor))e.Graphics.FillPath(fill,shape);
                using(var edge=new Pen(SystemInformation.HighContrast?SystemColors.WindowText:Color.FromArgb(43,51,64)))e.Graphics.DrawPath(edge,shape);
            }
        }
    }

    internal sealed class SmoothButton : Button
    {
        private readonly Timer timer=new Timer {Interval=16};
        private readonly Stopwatch clock=Stopwatch.StartNew();
        private double hover,press,last;
        private bool inside,pressed;
        internal bool Primary {get;set;}
        internal bool Quiet {get;set;}
        internal SmoothButton()
        {
            Height=40;FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Margin=new Padding(8,0,0,0);Cursor=Cursors.Hand;UseVisualStyleBackColor=false;
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            timer.Tick+=delegate {
                double now=clock.Elapsed.TotalSeconds,dt=Math.Min(.05,Math.Max(0,now-last));last=now;
                double h=Enabled&&inside?1:0,p=Enabled&&pressed?1:0;
                hover+=(h-hover)*(1-Math.Exp(-dt*17));press+=(p-press)*(1-Math.Exp(-dt*24));
                if(Math.Abs(h-hover)<.002&&Math.Abs(p-press)<.002){hover=h;press=p;timer.Stop();}Invalidate();
            };
        }
        private void Animate(){if(Visible&&SetupProgress.Motion){last=clock.Elapsed.TotalSeconds;timer.Start();}else{timer.Stop();hover=Enabled&&inside?1:0;press=Enabled&&pressed?1:0;Invalidate();}}
        protected override void OnMouseEnter(EventArgs e){base.OnMouseEnter(e);inside=true;Animate();}
        protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);inside=false;pressed=false;Animate();}
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left){pressed=true;Animate();}}
        protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);pressed=false;Animate();}
        protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode==Keys.Space){pressed=true;Animate();}}
        protected override void OnKeyUp(KeyEventArgs e){base.OnKeyUp(e);pressed=false;Animate();}
        protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
        protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);pressed=false;Animate();}
        protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);if(!Enabled){inside=false;pressed=false;}Cursor=Enabled?Cursors.Hand:Cursors.Default;Animate();}
        protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);if(!Visible){timer.Stop();hover=press=0;inside=pressed=false;}}
        // Never let the native rectangular button background leak around the rounded face.
        protected override void OnPaintBackground(PaintEventArgs e){e.Graphics.Clear(Parent==null?BackColor:Parent.BackColor);}
        protected override void OnPaint(PaintEventArgs e)
        {
            if(SystemInformation.HighContrast){base.OnPaint(e);return;}
            // Button's native paint path can skip OnPaintBackground entirely.
            e.Graphics.Clear(Parent==null?BackColor:Parent.BackColor);
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            float scale=DeviceDpi/96f,inset=(float)press*scale;
            var bounds=new RectangleF(1+inset,1+inset,Width-3-2*inset,Height-3-2*inset);
            Color rest=Primary?Color.FromArgb(143,195,249):Parent==null?Color.FromArgb(20,23,29):Parent.BackColor;
            if(!Primary&&!Quiet)rest=Color.FromArgb(32,38,48);
            Color fill=SetupDrawing.Mix(rest,Primary?Color.FromArgb(174,213,252):Color.FromArgb(48,58,73),hover);
            fill=SetupDrawing.Mix(fill,Primary?Color.FromArgb(108,165,226):Color.FromArgb(55,67,84),press*.6);
            if(!Enabled)fill=Color.FromArgb(29,34,42);
            using(var shape=SetupDrawing.Rounded(bounds,10*scale)){
                using(var brush=new SolidBrush(fill))e.Graphics.FillPath(brush,shape);
                if(Focused||(!Quiet&&!Primary))using(var pen=new Pen(Focused?Color.FromArgb(177,215,255):Color.FromArgb(59,70,86),Focused?2*scale:scale))e.Graphics.DrawPath(pen,shape);
            }
            var textBounds=Rectangle.Round(bounds);textBounds.Y+=(int)Math.Round(press*scale);
            TextRenderer.DrawText(e.Graphics,Text,Font,textBounds,!Enabled?Color.FromArgb(130,143,161):Primary?Color.FromArgb(17,31,47):Color.FromArgb(230,236,245),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
        }
        protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
    }

    internal sealed class AnimatedLabel : Label
    {
        private readonly Timer timer=new Timer {Interval=16};
        private readonly Stopwatch clock=new Stopwatch();
        private string previous="",current="";
        internal AnimatedLabel()
        {
            SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
            timer.Tick+=delegate{if(clock.Elapsed.TotalMilliseconds>=180){timer.Stop();previous="";}Invalidate();};
        }
        protected override void OnTextChanged(EventArgs e)
        {
            previous=current;current=Text;base.OnTextChanged(e);
            if(IsHandleCreated&&Visible&&SetupProgress.Motion&&previous.Length>0){clock.Restart();timer.Start();}
            else{previous="";timer.Stop();}Invalidate();
        }
        protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);if(!Visible){timer.Stop();previous="";}}
        protected override void OnPaint(PaintEventArgs e)
        {
            Color background=Parent==null?BackColor:Parent.BackColor;
            e.Graphics.Clear(background);
            var flags=TextFormatFlags.Top|TextFormatFlags.Left|TextFormatFlags.WordBreak|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding;
            Color foreground=SystemInformation.HighContrast?SystemColors.WindowText:ForeColor;
            double t=timer.Enabled?Math.Min(1,clock.Elapsed.TotalMilliseconds/180):1;
            // Update accessibility text immediately; only its visual colour changes.
            if(t<1)TextRenderer.DrawText(e.Graphics,previous,Font,ClientRectangle,SetupDrawing.Mix(background,foreground,Math.Max(0,1-t*2)),flags);
            TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,SetupDrawing.Mix(background,foreground,Math.Min(1,t*2)),flags);
        }
        protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
    }

    internal enum SetupProgressState {Working,Complete,Attention}
    internal sealed class ProgressTrack : Control
    {
        private readonly Timer timer=new Timer {Interval=16};
        private readonly Stopwatch clock=Stopwatch.StartNew();
        private SetupProgressState state;
        internal SetupProgressState State {
            get{return state;}
            set{bool changed=state!=value;state=value;timer.Enabled=state==SetupProgressState.Working&&Visible&&SetupProgress.Motion;if(changed)Invalidate();}
        }
        internal ProgressTrack(){AccessibleRole=AccessibleRole.ProgressBar;AccessibleName="Setup activity";AccessibleDescription="Installation activity. No percentage is reported.";SetStyle(ControlStyles.OptimizedDoubleBuffer|ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw,true);timer.Tick+=delegate{Invalidate();};}
        protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);State=state;}
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent==null?BackColor:Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            float scale=DeviceDpi/96f,height=Math.Min(7*scale,Height);
            var bounds=new RectangleF(0,(Height-height)/2,Width,height);
            if(Width<1||height<1)return;
            using(var shape=SetupDrawing.Rounded(bounds,height/2)){
                using(var brush=new SolidBrush(SystemInformation.HighContrast?SystemColors.ControlDark:Color.FromArgb(43,52,66)))e.Graphics.FillPath(brush,shape);
                if(state==SetupProgressState.Attention)return;
                var saved=e.Graphics.Save();
                try{
                    e.Graphics.SetClip(shape);
                    if(state==SetupProgressState.Complete){using(var brush=new SolidBrush(SystemInformation.HighContrast?SystemColors.Highlight:Color.FromArgb(143,195,249)))e.Graphics.FillPath(brush,shape);return;}
                    float width=Math.Min(Width,Math.Max(42*scale,Width*.3f));
                    float position=SetupProgress.Motion?(float)(.5-.5*Math.Cos(clock.Elapsed.TotalSeconds*Math.PI/1.7)):0;
                    var band=new RectangleF(position*(Width-width),bounds.Top,width,height);
                    if(!SetupProgress.Motion||SystemInformation.HighContrast){using(var brush=new SolidBrush(SystemColors.Highlight))e.Graphics.FillRectangle(brush,band);}
                    else using(var brush=new LinearGradientBrush(band,Color.FromArgb(43,52,66),Color.FromArgb(159,207,255),LinearGradientMode.Horizontal)){
                        brush.InterpolationColors=new ColorBlend{Colors=new[]{Color.FromArgb(43,52,66),Color.FromArgb(113,177,239),Color.FromArgb(180,221,255),Color.FromArgb(113,177,239),Color.FromArgb(43,52,66)},Positions=new[]{0f,.2f,.5f,.8f,1f}};
                        e.Graphics.FillRectangle(brush,band);
                    }
                }finally{e.Graphics.Restore(saved);}
            }
        }
        protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
    }
}
