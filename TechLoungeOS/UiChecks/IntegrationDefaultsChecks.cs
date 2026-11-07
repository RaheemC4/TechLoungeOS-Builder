using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using TechLoungeOS;

internal static class IntegrationDefaultsChecks
{
    internal static void Run()
    {
        var folder=Path.Combine(AppContext.BaseDirectory,"integration");
        var fixture=!Directory.Exists(folder);
        if(fixture)Directory.CreateDirectory(folder);
        try
        {
            var window=new MainWindow(false);
            try
            {
                var path=(string)typeof(MainWindow).GetProperty("BundledTweaksFolder",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
                if(path!=folder)throw new Exception("The mandatory Tweaks support must resolve from the complete app package.");
                foreach(var name in new[]{"TweaksPath","CurrentTweaksInstaller","AnimatedPe","RuntimePackEnabled","StoreXboxEnabled","GameMode","Suggestions","NvidiaAutomatic","AmdIntelAutomatic"})
                    if(window.FindName(name)!=null)throw new Exception("Mandatory setting still exposes a user control: "+name);
            }
            finally{window.Close();}
        }
        finally{if(fixture)Directory.Delete(folder);}
        Console.WriteLine("Mandatory support resolves from the app package and has no UI opt-outs. An empty directory fixture was used without loading an executable.");
    }
}
