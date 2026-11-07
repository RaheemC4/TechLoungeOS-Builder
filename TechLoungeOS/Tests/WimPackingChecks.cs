using TechLoungeOS;

internal static class WimPackingChecks
{
    internal static async Task Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"TLOS-packing-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var source=Path.Combine(root,"selected.wim");File.WriteAllText(source,"accepted input remains unchanged");
            var media=Path.Combine(root,"media");Directory.CreateDirectory(Path.Combine(media,"sources"));
            var oldImage=Path.Combine(media,@"sources\install.wim");File.WriteAllBytes(oldImage,new byte[3]);File.WriteAllBytes(Path.Combine(media,"office.dat"),new byte[2]);
            var replacement=Path.Combine(root,"accepted-fixture.wim");File.WriteAllBytes(replacement,new byte[9]);
            if(BootableDrive.ProjectedUsbBytes(media,oldImage,replacement)!=570_425_355L || new FileInfo(oldImage).Length!=3)
                throw new Exception("Projected media must replace the original image's size, retain other payloads and change no files.");
            var runner=new PackingRunner(source,Path.Combine(root,"packed-install.esd"));
            var packed=await WimPacking.Compact(runner,source,root,new Version(10,0,26300,9725),_=>{},CancellationToken.None);
            if(File.ReadAllText(packed)!="packed output fixture"||File.ReadAllText(source)!="accepted input remains unchanged")throw new Exception("Packing did not preserve its input and return its output.");
            try{await WimPacking.Compact(runner,source,root,new Version(10,0,26300,9725),_=>{},CancellationToken.None);throw new Exception("Pre-existing image was overwritten.");}
            catch(IOException error) when(error.Message.Contains("fresh packed-image")){if(File.ReadAllText(packed)!="packed output fixture"||runner.Exports!=1)throw new Exception("Rejected packing changed an existing output.");}
            var failed=Path.Combine(root,"bad-metadata");Directory.CreateDirectory(failed);
            var wrong=new PackingRunner(source,Path.Combine(failed,"packed-install.esd")){WrongVersion=true};
            try{await WimPacking.Compact(wrong,source,failed,new Version(10,0,26300,9725),_=>{},CancellationToken.None);throw new Exception("Wrong Windows version accepted.");}
            catch(IOException error) when(error.Message.Contains("readback differs")){if(File.ReadAllText(source)!="accepted input remains unchanged"||File.Exists(Path.Combine(failed,"image-packing.json")))throw new Exception("Failed packing lost the input or recorded success.");}
            var cancelled=Path.Combine(root,"cancelled");Directory.CreateDirectory(cancelled);
            using var cancellation=new CancellationTokenSource();cancellation.Cancel();
            try{await WimPacking.Compact(runner,source,cancelled,new Version(10,0,26300,9725),_=>{},cancellation.Token);throw new Exception("Cancelled packing continued.");}
            catch(OperationCanceledException){if(Directory.EnumerateFileSystemEntries(cancelled).Any()||runner.Exports!=1)throw new Exception("Cancelled packing changed files.");}
            Console.WriteLine("Packing fixtures passed: solid ESD command, immutable compressor files, retained source, existing-output and wrong-version rejection, and cancellation before image access. No real WIM was serviced.");
        }
        finally{Directory.Delete(root,true);}
    }
    private sealed class PackingRunner(string source,string destination):ICommandRunner
    {
        internal int Exports;internal bool WrongVersion;
        public async Task<string> Run(string exe,IEnumerable<string> args,CancellationToken ct=default)
        {
            if(Path.GetFileName(exe)!="wimlib-imagex.exe" || !args.SequenceEqual(new[]{"export",source,"1",destination,"--solid","--threads=2","--check"}))throw new Exception("Unexpected packing operation.");
            foreach(var name in new[]{"wimlib-imagex.exe","libwim-15.dll"})
            {
                try{using var overwrite=new FileStream(Path.Combine(Path.GetDirectoryName(exe)!,name),FileMode.Open,FileAccess.Write,FileShare.ReadWrite);throw new Exception("Compressor changed while executing.");}
                catch(IOException){ }
            }
            var identity=await new ProcessRunner(_=>{}).Run(exe,["--version"],ct);
            if(!identity.Contains("wimlib-imagex 1.14.5",StringComparison.Ordinal))throw new Exception("Pinned compressor did not launch under its read leases.");
            File.WriteAllText(destination,"packed output fixture");Exports++;return "The operation completed successfully.";
        }
        public Task<string> PowerShell(string script,CancellationToken ct=default)
        {
            if(!script.Contains("Get-WindowsImage -ImagePath '"+destination+"' -Index 1",StringComparison.Ordinal))
                throw new Exception("Packing must read the destination image's metadata.");
            return Task.FromResult(WrongVersion?"10.0.26200.9457":"10.0.26300.9725");
        }
        public Task<string> Dism(params string[] args)=>throw new Exception("Unexpected servicing during archive packing.");
    }
}
