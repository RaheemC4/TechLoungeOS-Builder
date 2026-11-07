using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TechLoungeOS.Personalisation {
    [StructLayout(LayoutKind.Sequential)]public struct MonitorRect {public int Left,Top,Right,Bottom;}
    [ComImport,Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDesktopWallpaper {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)]string monitor,[MarshalAs(UnmanagedType.LPWStr)]string wallpaper);
        void GetWallpaper([MarshalAs(UnmanagedType.LPWStr)]string monitor,[MarshalAs(UnmanagedType.LPWStr)]out string wallpaper);
        void GetMonitorDevicePathAt(uint index,[MarshalAs(UnmanagedType.LPWStr)]out string monitor);
        void GetMonitorDevicePathCount(out uint count);
        [PreserveSig]int GetMonitorRECT([MarshalAs(UnmanagedType.LPWStr)]string monitor,out MonitorRect rect);
        void SetBackgroundColor(uint colour);
        void GetBackgroundColor(out uint colour);
        void SetPosition(int position);
        void GetPosition(out int position);
    }
    public static class Desktop {
        sealed class Frame {public string Device,Previous,Destination;}
        public static void CheckPolicy() {
            foreach(RegistryKey hive in new[]{Registry.CurrentUser,Registry.LocalMachine}) {
                using(RegistryKey key=hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\System"))
                    if(key!=null && !String.IsNullOrEmpty(Convert.ToString(key.GetValue("Wallpaper"))))throw new InvalidOperationException("Desktop background is managed by policy. Your existing policy was preserved.");
                using(RegistryKey key=hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\ActiveDesktop"))
                    if(key!=null && Convert.ToInt32(key.GetValue("NoChangingWallPaper",0))==1)throw new InvalidOperationException("Desktop background changes are managed by policy.");
            }
        }
        public static void Apply(string library,string preset) {
            CheckPolicy();
            string state=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TechLoungeOS","Wallpapers");
            Directory.CreateDirectory(state);
            object instance=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")));
            try {ApplyTo((IDesktopWallpaper)instance,library,preset,state);}
            finally {Marshal.FinalReleaseComObject(instance);}
        }
        public static void Refresh(string library) {
            string state=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TechLoungeOS","Wallpapers");
            string selected=Path.Combine(state,"selected.txt");
            if(!File.Exists(selected) || new FileInfo(selected).Length>128)return;
            string preset=File.ReadAllText(selected).Trim();if(!Catalog.Valid(preset))return;
            CheckPolicy();
            object instance=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD")));
            try {ApplyTo((IDesktopWallpaper)instance,library,preset,state,true);}
            finally {Marshal.FinalReleaseComObject(instance);}
        }
        static bool HasSize(string path,int width,int height) {
            try {
                using(var stream=File.OpenRead(path)) {
                    var header=new byte[24];int read=0;
                    while(read<header.Length){int length=stream.Read(header,read,header.Length-read);if(length==0)return false;read+=length;}
                    byte[] signature={137,80,78,71,13,10,26,10};
                    for(int i=0;i<signature.Length;i++)if(header[i]!=signature[i])return false;
                    int w=(header[16]<<24)|(header[17]<<16)|(header[18]<<8)|header[19];
                    int h=(header[20]<<24)|(header[21]<<16)|(header[22]<<8)|header[23];
                    return w==width && h==height;
                }
            }catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
        }
        static bool ManagedFrame(string path,string state) {
            return !String.IsNullOrEmpty(path) && String.Equals(Path.GetDirectoryName(path),state,StringComparison.OrdinalIgnoreCase) &&
                Regex.IsMatch(Path.GetFileName(path),@"\Adisplay-[0-9a-f]{32}-[ab]\.png\z",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        }
        internal static void ApplyTo(IDesktopWallpaper desktop,string library,string preset,string state,bool refreshOnly=false) {
            string source=Catalog.PathFor(library,preset);Directory.CreateDirectory(state);
            using(var locked=File.Open(Path.Combine(state,"apply.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
                uint count;desktop.GetMonitorDevicePathCount(out count);
                if(count==0 || count>4096)throw new InvalidOperationException("No supported display list was found.");
                var frames=new List<Frame>();int previousPosition;uint previousColour;
                desktop.GetPosition(out previousPosition);desktop.GetBackgroundColor(out previousColour);
                // Windows can select Fill during first-profile theme setup. Both
                // Centre and Fill display our native-size frames without scaling.
                // Keep the chosen placement; refresh only frames we still own.
                if(refreshOnly && ((previousPosition!=0 && previousPosition!=4) || previousColour!=0))return;
                for(uint i=0;i<count;i++) {
                    string device;MonitorRect rect;desktop.GetMonitorDevicePathAt(i,out device);
                    // The list includes detached displays. S_FALSE leaves their rectangle undefined.
                    int result=desktop.GetMonitorRECT(device,out rect);
                    if(result==1)continue;
                    if(result!=0)throw new COMException("Windows could not read the display bounds.",result);
                    int width=rect.Right-rect.Left,height=rect.Bottom-rect.Top;
                    if(width==0 || height==0)continue;
                    if(frames.Count==32)throw new InvalidOperationException("More than 32 active displays are not supported.");
                    string current;desktop.GetWallpaper(device,out current);
                    string name;using(var sha=SHA256.Create())name=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(device))).Replace("-","").Substring(0,32);
                    string first=Path.Combine(state,"display-"+name+"-a.png"),second=Path.Combine(state,"display-"+name+"-b.png");
                    if(refreshOnly && (!ManagedFrame(current,state) || HasSize(current,width,height)))continue;
                    string destination=String.Equals(current,first,StringComparison.OrdinalIgnoreCase)?second:first;
                    string temporary=Path.Combine(state,"render-"+Guid.NewGuid().ToString("N")+".tmp");
                    try{Renderer.Render(source,width,height,temporary);if(File.Exists(destination))File.Delete(destination);File.Move(temporary,destination);}
                    finally{if(File.Exists(temporary))File.Delete(temporary);}
                    frames.Add(new Frame{Device=device,Previous=current,Destination=destination});
                }
                if(frames.Count==0){if(refreshOnly)return;throw new InvalidOperationException("No active display was found.");}
                // Render every frame before changing Windows. Each monitor receives
                // its own image, and the active cache slot is never overwritten.
                var attempted=new List<Frame>();
                try {
                    if(!refreshOnly){desktop.SetPosition(0);desktop.SetBackgroundColor(0);}
                    foreach(var frame in frames) {
                        if(refreshOnly) {
                            string current;desktop.GetWallpaper(frame.Device,out current);
                            if(!String.Equals(current,frame.Previous,StringComparison.OrdinalIgnoreCase))continue;
                        }
                        attempted.Add(frame);desktop.SetWallpaper(frame.Device,frame.Destination);
                    }
                    if(!refreshOnly)File.WriteAllText(Path.Combine(state,"selected.txt"),preset);
                }catch(Exception applyError){
                    var errors=new List<Exception>();errors.Add(applyError);
                    foreach(var frame in attempted)try{desktop.SetWallpaper(frame.Device,frame.Previous);}catch(Exception ex){errors.Add(ex);}
                    if(!refreshOnly)try{desktop.SetPosition(previousPosition);desktop.SetBackgroundColor(previousColour);}catch(Exception ex){errors.Add(ex);}
                    throw new AggregateException("Wallpaper change failed; previous display settings were restored where possible.",errors);
                }
            }
        }
    }
}
