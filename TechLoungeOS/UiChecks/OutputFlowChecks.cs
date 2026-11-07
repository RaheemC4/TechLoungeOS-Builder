using System.Windows;
using System.Windows.Controls;
using TechLoungeOS;

internal static class OutputFlowChecks
{
    internal static void Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"TLOS-output-flow-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var target=Path.Combine(root,"source.iso");File.WriteAllText(target,"original inert fixture");
            var original=IsoOutput.Capture(target);var staged=IsoOutput.StagingPath(target);File.WriteAllText(staged,"verified replacement fixture");
            IsoOutput.Commit(staged,original);
            Require(File.ReadAllText(target)=="verified replacement fixture" && !File.Exists(staged) && Directory.GetFiles(root,"*.iso").Length==1,"Atomic replacement keeps one chosen ISO and removes its own backup after readback");
            var changed=IsoOutput.Capture(target);staged=IsoOutput.StagingPath(target);File.WriteAllText(staged,"second fixture");File.WriteAllText(target,"user changed the destination");
            Reject(()=>IsoOutput.Commit(staged,changed));
            Require(File.ReadAllText(target)=="user changed the destination" && File.Exists(staged),"A changed destination is preserved and the new staged file retained");
            var newTarget=Path.Combine(root,"new.iso");var absent=IsoOutput.Capture(newTarget);var next=IsoOutput.StagingPath(newTarget);File.WriteAllText(next,"new fixture");File.WriteAllText(newTarget,"new user file");
            Reject(()=>IsoOutput.Commit(next,absent));Require(File.ReadAllText(newTarget)=="new user file" && File.Exists(next),"A destination appearing during the build is never overwritten");
            var saved=Path.Combine(root,"saved.iso");var empty=IsoOutput.Capture(saved);var fresh=IsoOutput.StagingPath(saved);File.WriteAllText(fresh,"saved fixture");IsoOutput.Commit(fresh,empty);
            Require(File.ReadAllText(saved)=="saved fixture" && !File.Exists(fresh),"A new output is committed without overwriting another file");
            var temporary=IsoOutput.StagingPath(saved);File.WriteAllText(temporary,"temporary USB-only fixture");
            Reject(()=>IsoOutput.RemoveTemporary(temporary,new string('0',64)));Require(File.Exists(temporary),"A changed temporary ISO is retained");
            Reject(()=>IsoOutput.RemoveTemporary(saved,BootableDrive.Hash(saved)));Require(File.Exists(saved),"Cleanup cannot delete a user-named saved ISO");
            IsoOutput.RemoveTemporary(temporary,BootableDrive.Hash(temporary));Require(!File.Exists(temporary),"USB-only cleanup deletes only its exact owned temporary ISO");
            var linked=Path.Combine(root,"linked.iso");
            try
            {
                File.CreateSymbolicLink(linked,target);Reject(()=>IsoOutput.Capture(linked));
                Require(File.ReadAllText(target)=="user changed the destination","Linked ISO targets are rejected without changing the underlying file");
            }
            catch(UnauthorizedAccessException){Console.WriteLine("File-link fixture unavailable without developer mode/elevation; no settings changed.");}
            catch(IOException error) when((error.HResult&0xffff)==1314){Console.WriteLine("File-link fixture unavailable without the symbolic-link privilege; no settings changed.");}
            Require(BootableDrive.VolumeLabel("raheem pc")=="RAHEEM PC","USB label normalisation");
            foreach(var label in new[]{""," ","ABCDEFGHIJKL","bad\nlabel","bad'", "bad;format","USB💻"})Reject(()=>BootableDrive.VolumeLabel(label));
            var disk=new UsbDisk(9999,"fixture-only","fixture-only","fixture-only","Inert fixture",16_000_000_000,7,512,false,false,false,false,false,[]);
            Require(BootableDrive.FormatScript(disk,8L<<30,"raheem pc").Contains("-NewFileSystemLabel 'RAHEEM PC'"),"The validated custom label reaches the format script as a literal");
            var window=new BootDriveWindow(target,(_,_,_,_)=>throw new Exception("Inert UI test must not build media"),saved);
            try
            {
                Require(window.FindName("Source") is TextBlock && ((TextBlock)window.FindName("Source")).Text=="source.iso","USB creation displays its imported source without an editable ISO picker");
                Require(window.FindName("SaveIsoEnabled")==null && window.FindName("SaveIsoPath")==null,"The USB picker has no duplicate ISO-saving controls");
                Require(window.FindName("WorkingFolder")==null && ((TextBox)window.FindName("UsbLabel")).Text=="TLOS_SETUP","USB temporary storage is automatic and the label is editable");
            }
            finally{window.Close();}
            Console.WriteLine("Inert-file save/replace/race/cleanup and USB UI checks passed. No real ISO servicing, mount, USB write or VM execution.");
        }
        finally
        {
            if(Path.GetDirectoryName(root)!=Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar) || !Path.GetFileName(root).StartsWith("TLOS-output-flow-",StringComparison.Ordinal))throw new IOException("Fixture cleanup boundary changed");
            Directory.Delete(root,true);
        }
    }
    private static void Require(bool result,string message){if(!result)throw new IOException(message);Console.WriteLine("PASS: "+message);}
    private static void Reject(Action action){try{action();throw new Exception("Expected protected operation rejection");}catch(IOException){}}
}
