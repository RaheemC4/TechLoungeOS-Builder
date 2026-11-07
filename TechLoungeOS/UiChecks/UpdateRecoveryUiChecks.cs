using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using TechLoungeOS;

internal static class UpdateRecoveryUiChecks
{
    internal static void Run(MainWindow window)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var type=typeof(MainWindow);
        var recovery=type.GetField("recoveryRequired",flags)!;
        void Invoke(string name,params object[] args)=>type.GetMethod(name,flags)!.Invoke(window,args);
        recovery.SetValue(window,true);
        try
        {
            // Finishing an async action must not unlock a partially updated app.
            Invoke("SetBusy",false);
            foreach(var name in new[]{"Inputs","Navigation","PreviousStepButton","NextStepButton","BuildButton"})
                if(((UIElement)window.FindName(name)).IsEnabled)
                    throw new Exception("Pending update recovery must disable "+name+".");
            foreach(var step in new[]{1,4})
            {
                Invoke("ShowStep",step);
                if(((Button)window.FindName("NextStepButton")).IsEnabled)
                    throw new Exception("Changing pages unlocked navigation during update recovery.");
                Invoke("NextStepClick",window,new RoutedEventArgs());
                Invoke("PreviousStepClick",window,new RoutedEventArgs());
                if((int)type.GetField("currentStep",flags)!.GetValue(window)! !=step)
                    throw new Exception("Wizard handlers must refuse navigation during update recovery.");
            }
            var before=Application.Current.Windows.Count;
            Invoke("OpenUsbPlan");
            Invoke("ImportClick",window,new RoutedEventArgs());
            if(Application.Current.Windows.Count!=before)
                throw new Exception("Recovery opened a new media workflow.");
        }
        finally
        {
            recovery.SetValue(window,false);
            Invoke("SetBusy",false);
            Invoke("ShowStep",2);
        }
        Console.WriteLine("Pending update recovery blocks all media controls, direct handlers and idle/page transitions; no media was opened or serviced.");
    }
}
