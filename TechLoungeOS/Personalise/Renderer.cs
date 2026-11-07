using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace TechLoungeOS.Personalisation {
    public static class Renderer {
        public static Bitmap Load(string path) {
            using(Image image=Image.FromFile(path)) {
                if((long)image.Width*image.Height>60000000)throw new IOException("Wallpaper exceeds the pixel budget.");
                Bitmap copy=new Bitmap(image.Width,image.Height,PixelFormat.Format32bppArgb);
                copy.SetResolution(96,96);
                using(Graphics g=Graphics.FromImage(copy))g.DrawImage(image,new Rectangle(0,0,image.Width,image.Height),0,0,image.Width,image.Height,GraphicsUnit.Pixel);
                return copy;
            }
        }
        public static Bitmap Colour(Bitmap source,string colour) {
            if(Array.IndexOf(Catalog.Colours,colour)<0)throw new ArgumentException("Unknown colour.");
            Bitmap result=(Bitmap)source.Clone();
            BitmapData data=result.LockBits(new Rectangle(0,0,result.Width,result.Height),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
            try {
                int stride=Math.Abs(data.Stride);byte[] pixels=new byte[stride*result.Height];Marshal.Copy(data.Scan0,pixels,0,pixels.Length);
                double[] accent=colour=="violet"?new double[]{246,65,143}:new double[]{255,186,73};
                double[] land=colour=="violet"?new double[]{103,38,188}:new double[]{169,83,40};
                for(int y=0;y<result.Height;y++)for(int x=0;x<result.Width;x++) {
                    int i=y*stride+x*4;double b=pixels[i],g=pixels[i+1],r=pixels[i+2];double max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b));
                    if(max<=1){pixels[i]=pixels[i+1]=pixels[i+2]=0;pixels[i+3]=255;continue;}
                    if(colour=="ice")continue;
                    double delta=max-min;
                    double hue=delta==0?230:(max==b?240+60*(r-g)/delta:max==g?120+60*(b-r)/delta:60*(g-b)/delta);
                    if(hue<0)hue+=360;
                    double mix=Math.Max(0,Math.Min(1,(hue-195)/35));
                    double brightness=Math.Min(1,max/(245*(1-mix)+210*mix));
                    for(int channel=0;channel<3;channel++)pixels[i+2-channel]=(byte)Math.Round((accent[channel]*(1-mix)+land[channel]*mix)*brightness);
                }
                Marshal.Copy(pixels,0,data.Scan0,pixels.Length);
            } finally {result.UnlockBits(data);}
            return result;
        }
        public static Rectangle InkBounds(Bitmap image) {
            int left=image.Width,top=image.Height,right=-1,bottom=-1;
            BitmapData data=image.LockBits(new Rectangle(0,0,image.Width,image.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            try {
                int stride=Math.Abs(data.Stride);byte[] bytes=new byte[stride*image.Height];Marshal.Copy(data.Scan0,bytes,0,bytes.Length);
                for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++) {
                    int i=y*stride+x*4;
                    if(Math.Max(bytes[i],Math.Max(bytes[i+1],bytes[i+2]))<100)continue;
                    left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);
                }
            } finally {image.UnlockBits(data);}
            if(right<left)throw new IOException("Wallpaper contains no visible artwork.");
            return Rectangle.FromLTRB(left,top,right+1,bottom+1);
        }
        public static Rectangle Placement(int width,int height,Size source,Rectangle ink) {
            if(width<320 || height<320 || (long)width*height>60000000 || source.Width<1 || source.Height<1 || ink.Width<1 || ink.Height<1)throw new ArgumentException("Unsupported display size or artwork bounds.");
            // Never enlarge raster pixels. Larger desktops retain sharp, compact art.
            double scale=Math.Min(1,Math.Min(width*.24/ink.Width,height*.36/ink.Height));
            int w=Math.Max(1,(int)Math.Round(source.Width*scale)),h=Math.Max(1,(int)Math.Round(source.Height*scale));
            return new Rectangle((int)Math.Round(width*.5-(ink.Left+ink.Width*.5)*scale),(int)Math.Round(height*.5-(ink.Top+ink.Height*.5)*scale),w,h);
        }
        public static void Render(string source,int width,int height,string destination) {
            using(Bitmap image=Load(source)) {
                Rectangle place=Placement(width,height,image.Size,InkBounds(image));
                using(Bitmap output=new Bitmap(width,height,PixelFormat.Format24bppRgb)) {
                    output.SetResolution(96,96);
                    using(Graphics g=Graphics.FromImage(output)) {
                        g.Clear(Color.Black);g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                        g.DrawImage(image,place,0,0,image.Width,image.Height,GraphicsUnit.Pixel);
                    }
                    output.Save(destination,ImageFormat.Png);
                }
            }
        }
        public static void Prepare(string source,string destination) {
            if(Directory.Exists(destination) || File.Exists(destination))throw new IOException("Choose a new preset output directory.");
            Directory.CreateDirectory(destination);
            foreach(string design in Catalog.Designs)using(Bitmap master=Load(File.Exists(Path.Combine(source,design+".png"))?Path.Combine(source,design+".png"):Catalog.PathFor(source,design+".ice"))) {
                foreach(string colour in Catalog.Colours)using(Bitmap result=Colour(master,colour))result.Save(Path.Combine(destination,Catalog.FileName(design+"."+colour)),ImageFormat.Png);
            }
        }
    }
}
