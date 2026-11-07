using System;
using System.IO;

namespace TechLoungeOS.Personalisation {
    public static class Catalog {
        public static readonly string[] Designs={"waterfall","coastal-arch","dune-horizon","lighthouse-point","alpine-lake"};
        public static readonly string[] Titles={"Waterfall","Coastal Arch","Dune Horizon","Lighthouse Point","Alpine Lake"};
        public static readonly string[] Colours={"ice","violet","amber"};
        public const string Default="waterfall.ice";
        public static string Library(string application){string nested=Path.Combine(application,"Wallpapers");return Directory.Exists(nested)?nested:application;}
        public static bool Valid(string id) {
            if(id==null)return false;
            string[] parts=id.Split('.');
            return parts.Length==2 && Array.IndexOf(Designs,parts[0])>=0 && Array.IndexOf(Colours,parts[1])>=0;
        }
        public static string FileName(string id) {
            if(!Valid(id))throw new ArgumentException("Choose a known wallpaper design and colour.");
            return id.Replace('.','-')+".png";
        }
        public static string PathFor(string root,string id) {
            string path=Path.Combine(root,FileName(id));
            if(!File.Exists(path) || (File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Wallpaper preset missing or linked: "+id);
            return path;
        }
    }
}
