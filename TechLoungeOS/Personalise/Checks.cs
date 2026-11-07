using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace TechLoungeOS.Personalisation {
    static class Checks {
        static int count;
        static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;Console.WriteLine("PASS: "+name);}
        static int Main(string[] args) {
            try{return Run(args);}catch(Exception error){Console.Error.WriteLine(error.Message);return 1;}
        }
        static int Run(string[] args) {
            string root=Path.GetFullPath(args[0]);if(Directory.Exists(root))throw new IOException("New fixture directory required");Directory.CreateDirectory(root);
            using(var pattern=new Bitmap(12,8,PixelFormat.Format32bppArgb)) {
                pattern.SetResolution(96,96);
                using(var graphics=Graphics.FromImage(pattern)){graphics.Clear(Color.Black);graphics.FillRectangle(Brushes.Cyan,5,2,4,3);}
                string file=Path.Combine(root,"pixel-grid.png");pattern.Save(file,ImageFormat.Png);
                using(var loaded=Renderer.Load(file)) {
                    bool exact=loaded.Width==12 && loaded.Height==8;
                    for(int y=0;y<8;y++)for(int x=0;x<12;x++)exact&=loaded.GetPixel(x,y).ToArgb()==pattern.GetPixel(x,y).ToArgb();
                    Check(exact,"DPI-aware loading retains every source pixel without scaling or clipping");
                }
            }
            using(var source=new Bitmap(2,2,PixelFormat.Format32bppArgb)) {
                source.SetPixel(0,0,Color.FromArgb(0,225,245));source.SetPixel(1,0,Color.FromArgb(0,0,210));source.SetPixel(0,1,Color.FromArgb(1,1,1));source.SetPixel(1,1,Color.Black);
                using(var violet=Renderer.Colour(source,"violet"))Check(violet.GetPixel(0,0).ToArgb()==Color.FromArgb(246,65,143).ToArgb() && violet.GetPixel(1,0).ToArgb()==Color.FromArgb(103,38,188).ToArgb(),"two-colour mapping preserves separate accent and land roles");
                using(var ice=Renderer.Colour(source,"ice"))Check(ice.GetPixel(0,0)==source.GetPixel(0,0) && ice.GetPixel(0,1).ToArgb()==Color.Black.ToArgb(),"Ice preserves original colour and the outer black stays exact");
                source.Save(Path.Combine(root,Catalog.FileName(Catalog.Default)),ImageFormat.Png);
            }
            Check(Catalog.Valid("waterfall.ice") && !Catalog.Valid("../ice") && !Catalog.Valid("unknown.ice") && !Catalog.Valid("waterfall.red"),"catalog rejects unknown/traversal selections");
            foreach(var screen in new[]{new Size(800,600),new Size(1920,1080),new Size(1080,1920),new Size(3440,1440),new Size(3840,2160),new Size(7680,4320)}) {
                var source=new Size(1672,941);var ink=new Rectangle(400,180,800,600);Rectangle placement=Renderer.Placement(screen.Width,screen.Height,source,ink);
                double scale=(double)placement.Width/source.Width;
                Check(scale<=1.001 && Math.Abs(placement.Left+(ink.Left+ink.Width*.5)*scale-screen.Width*.5)<2 && Math.Abs(placement.Top+(ink.Top+ink.Height*.5)*scale-screen.Height*.5)<2,"native-pixel cap and centred artwork at "+screen);
            }
            var fake=new FakeDesktop();string state=Path.Combine(root,"state");
            Desktop.ApplyTo(fake,root,Catalog.Default,state);
            Check(fake.Position==0 && fake.Colour==0 && fake.Current[0]!=fake.Current[1],"mocked mixed-orientation displays receive independent centred frames");
            for(int i=0;i<2;i++)using(var image=Renderer.Load(fake.Current[i]))Check(image.Size==(i==0?new Size(1920,1080):new Size(1080,1920)) && image.GetPixel(0,0).ToArgb()==Color.Black.ToArgb(),"frame matches its physical monitor, including negative desktop coordinates");
            string active=fake.Current[0];byte[] original=File.ReadAllBytes(active);
            Desktop.ApplyTo(fake,root,Catalog.Default,state);
            Check(fake.Current[0]!=active && File.ReadAllBytes(active).Length==original.Length,"next apply uses the inactive cache slot");
            Desktop.ApplyTo(fake,root,Catalog.Default,state);Check(Directory.GetFiles(state,"display-*.png").Length==4,"cache is bounded to two images per monitor");
            string unchanged=fake.Current[0];DateTime stamp=new DateTime(2001,1,1,0,0,0,DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(unchanged,stamp);
            Desktop.ApplyTo(fake,root,Catalog.Default,state,true);
            Check(fake.Current[0]==unchanged && File.GetLastWriteTimeUtc(unchanged)==stamp,"matching native-size backgrounds are not rendered again at sign-in");
            fake.Grow=16;fake.Current[1]="user-custom.jpg";
            Desktop.ApplyTo(fake,root,Catalog.Default,state,true);
            using(var image=Renderer.Load(fake.Current[0]))Check(image.Size==new Size(1936,1096) && fake.Current[1]=="user-custom.jpg","resolution refresh updates only the managed display and keeps a custom second background");
            fake.Prefix="driver-";fake.Grow=32;unchanged=fake.Current[0];
            Desktop.ApplyTo(fake,root,Catalog.Default,state,true);
            using(var image=Renderer.Load(fake.Current[0]))Check(image.Size==new Size(1952,1112) && fake.Current[0]!=unchanged && fake.Current[1]=="user-custom.jpg","a graphics-driver display identity change can refresh its inherited managed background");
            fake.Position=4;fake.Grow=32;unchanged=fake.Current[0];
            Desktop.ApplyTo(fake,root,Catalog.Default,state,true);
            Check(fake.Position==4 && fake.Current[0]==unchanged,"a user-selected wallpaper placement is preserved during automatic refresh");
            fake.Grow=48;unchanged=fake.Current[0];
            Desktop.ApplyTo(fake,root,Catalog.Default,state,true);
            using(var image=Renderer.Load(fake.Current[0]))Check(image.Size==new Size(1968,1128) && fake.Position==4 && fake.Current[0]!=unchanged && fake.Current[1]=="user-custom.jpg","Fill mode refreshes managed frames at the new native resolution without changing placement or a custom second display");
            fake.Position=2;fake.Grow=64;unchanged=fake.Current[0];
            Desktop.ApplyTo(fake,root,Catalog.Default,state,true);
            Check(fake.Position==2 && fake.Current[0]==unchanged,"an explicitly stretched wallpaper is left unchanged during refresh");
            var docked=new FakeDesktop();docked.Count=35;
            Desktop.ApplyTo(docked,root,Catalog.Default,Path.Combine(root,"docked"));
            Check(docked.Current[0]!="original-0" && docked.Current[1]!="original-1","detached monitor history does not prevent applying to two connected displays");
            var missing=new FakeDesktop();missing.Count=35;missing.ActiveCount=0;
            try{Desktop.ApplyTo(missing,root,Catalog.Default,Path.Combine(root,"detached"));throw new Exception("Expected no-display failure");}
            catch(InvalidOperationException){Check(missing.Position==5 && missing.Colour==0xABCDEF && missing.Current[0]=="original-0","all-detached displays preserve the existing desktop and report no active screen");}
            var unreadable=new FakeDesktop();unreadable.RectFailureAt=1;
            try{Desktop.ApplyTo(unreadable,root,Catalog.Default,Path.Combine(root,"unreadable"));throw new Exception("Expected rectangle failure");}
            catch(COMException ex){Check(ex.ErrorCode==unchecked((int)0x80070057) && unreadable.Current[0]=="original-0" && unreadable.Position==5,"a failed display query preserves every original background before apply");}
            var broken=new FakeDesktop();broken.FailOnce=true;
            try{Desktop.ApplyTo(broken,root,Catalog.Default,Path.Combine(root,"rollback"));throw new Exception("Expected failure");}
            catch(AggregateException){Check(broken.Current[0]=="original-0" && broken.Current[1]=="original-1" && broken.Position==5 && broken.Colour==0xABCDEF,"failed second-monitor update restores previous images and global placement");}
            Console.WriteLine(count+" personalisation checks passed. No host wallpaper API was called.");return 0;
        }
        sealed class FakeDesktop:IDesktopWallpaper {
            public string[] Current={"original-0","original-1"};public int Position=5;public uint Colour=0xABCDEF;public bool FailOnce;public uint Count=2;public int ActiveCount=2,RectFailureAt=-1,Grow;public string Prefix="";
            static int Index(string device){return Int32.Parse(device.Substring(device.LastIndexOf('-')+1));}
            public void SetWallpaper(string monitor,string path){int index=Index(monitor);if(index==1 && FailOnce){FailOnce=false;throw new IOException("Fixture second-monitor failure");}Current[index]=path;}
            public void GetWallpaper(string monitor,out string path){path=Current[Index(monitor)];}
            public void GetMonitorDevicePathAt(uint index,out string device){device=Prefix+index;}
            public void GetMonitorDevicePathCount(out uint count){count=Count;}
            public int GetMonitorRECT(string device,out MonitorRect rect){
                int index=Index(device);rect=new MonitorRect{Left=100,Top=100,Right=-100,Bottom=-100};
                if(index==RectFailureAt)return unchecked((int)0x80070057);
                if(index>=ActiveCount)return 1;
                rect=index==0?new MonitorRect{Left=-1920,Top=0,Right=Grow,Bottom=1080+Grow}:new MonitorRect{Left=0,Top=-200,Right=1080+Grow,Bottom=1720+Grow};return 0;
            }
            public void SetBackgroundColor(uint value){Colour=value;}public void GetBackgroundColor(out uint value){value=Colour;}
            public void SetPosition(int value){Position=value;}public void GetPosition(out int value){value=Position;}
        }
    }
}
