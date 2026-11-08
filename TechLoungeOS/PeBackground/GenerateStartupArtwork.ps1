$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Drawing
# The static first frame of Ambient.h, also used behind Windows Setup.
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class StartupArtwork {
 public static void Save(string folder) {
  const int w=1920,h=1080; using(var image=new Bitmap(w,h,PixelFormat.Format24bppRgb)) {
   var locked=image.LockBits(new Rectangle(0,0,w,h),ImageLockMode.WriteOnly,PixelFormat.Format24bppRgb);
   try {var data=new byte[locked.Stride*h];for(int y=0;y<h;y++)for(int x=0;x<w;x++) {
    double u=(double)x/w,v=(double)y/h,dx=(u-.46)*1.5,dy=v-.71,band=v-(.64+.09*Math.Sin(u*5));
    double glow=1/(1+18*(dx*dx+dy*dy)),edge=1/(1+1200*band*band);int p=y*locked.Stride+x*3;
    data[p]=(byte)(23+25*glow+19*edge);data[p+1]=(byte)(17+16*glow+11*edge);data[p+2]=(byte)(14+9*glow+5*edge);
   }Marshal.Copy(data,0,locked.Scan0,data.Length);}finally{image.UnlockBits(locked);}
   image.Save(System.IO.Path.Combine(folder,"startup.bmp"),ImageFormat.Bmp);
   var encoder=Array.Find(ImageCodecInfo.GetImageEncoders(),e=>e.MimeType=="image/jpeg");
   using(var parameters=new EncoderParameters(1)){parameters.Param[0]=new EncoderParameter(Encoder.Quality,100L);image.Save(System.IO.Path.Combine(folder,"startup.jpg"),encoder,parameters);}
  }
 }
}
'@
$folder=Join-Path $PSScriptRoot 'Artwork'
if(Test-Path -LiteralPath $folder){throw 'Artwork output already exists; review it before regenerating.'}
[IO.Directory]::CreateDirectory($folder) | Out-Null
[StartupArtwork]::Save($folder)
