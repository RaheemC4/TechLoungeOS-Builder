using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TechLoungeOS.Personalisation {
    static class Program {
        [STAThread]static int Main(string[] args) {
            try {
                if(args.Length==3 && args[0]=="--prepare"){Renderer.Prepare(args[1],args[2]);return 0;}
                if(args.Length==6 && args[0]=="--render"){Renderer.Render(Catalog.PathFor(args[1],args[2]),Int32.Parse(args[3]),Int32.Parse(args[4]),args[5]);return 0;}
                if(args.Length==2 && args[0]=="--apply"){Desktop.Apply(Catalog.Library(AppDomain.CurrentDomain.BaseDirectory),args[1]);return 0;}
                if(args.Length==1 && args[0]=="--refresh"){Desktop.Refresh(Catalog.Library(AppDomain.CurrentDomain.BaseDirectory));return 0;}
                if(args.Length!=0)throw new ArgumentException("Unknown wallpaper action.");
                Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Picker());return 0;
            } catch(Exception ex) {
                if(args.Length==0)MessageBox.Show(ex.Message,"TechLoungeOS Wallpapers",MessageBoxButtons.OK,MessageBoxIcon.Information);
                else {
                    string state=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TechLoungeOS");
                    Directory.CreateDirectory(state);File.WriteAllText(Path.Combine(state,"wallpaper-error.txt"),ex.ToString());
                }
                return 1;
            }
        }
    }
    sealed class Picker:Form {
        readonly ComboBox design=new ComboBox(),colour=new ComboBox();
        readonly PictureBox preview=new PictureBox();readonly Label status=new Label();readonly Button apply=new Button();
        public Picker() {
            Text="TechLoungeOS Wallpapers";BackColor=Color.FromArgb(20,20,22);ForeColor=Color.FromArgb(240,240,244);
            Font=new Font("Segoe UI",11);ClientSize=new Size(720,570);MinimumSize=new Size(520,460);StartPosition=FormStartPosition.CenterScreen;AutoScaleMode=AutoScaleMode.Dpi;
            var panel=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=2,RowCount=6};
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,60));panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,40));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute,44));panel.RowStyles.Add(new RowStyle(SizeType.Absolute,28));panel.RowStyles.Add(new RowStyle(SizeType.Absolute,44));panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));panel.RowStyles.Add(new RowStyle(SizeType.Absolute,44));
            var heading=new Label{Text="Make it yours",Font=new Font(Font.FontFamily,20),AutoSize=true};panel.Controls.Add(heading,0,0);panel.SetColumnSpan(heading,2);
            panel.Controls.Add(new Label{Text="Design",AutoSize=true},0,1);panel.Controls.Add(new Label{Text="Colour",AutoSize=true},1,1);
            foreach(var box in new[]{design,colour}){box.Dock=DockStyle.Fill;box.DropDownStyle=ComboBoxStyle.DropDownList;box.BackColor=Color.FromArgb(38,38,42);box.ForeColor=ForeColor;box.FlatStyle=FlatStyle.Flat;}
            design.Items.AddRange(Catalog.Titles);colour.Items.AddRange(new[]{"Ice","Violet","Amber"});panel.Controls.Add(design,0,2);panel.Controls.Add(colour,1,2);
            preview.Dock=DockStyle.Fill;preview.BackColor=Color.Black;preview.SizeMode=PictureBoxSizeMode.Zoom;preview.Margin=new Padding(0,14,0,12);panel.Controls.Add(preview,0,3);panel.SetColumnSpan(preview,2);
            status.Text="Centred on each connected display. Artwork stays at native sharpness; larger screens may show a smaller motif. Your custom backgrounds remain available in Windows Settings.";status.AutoSize=true;status.Dock=DockStyle.Fill;panel.Controls.Add(status,0,4);panel.SetColumnSpan(status,2);
            apply.Text="Apply to all displays";apply.AutoSize=true;apply.Dock=DockStyle.Right;apply.FlatStyle=FlatStyle.Flat;apply.BackColor=Color.FromArgb(55,100,190);panel.Controls.Add(apply,1,5);
            Controls.Add(panel);design.SelectedIndexChanged+=delegate{RefreshPreview();};colour.SelectedIndexChanged+=delegate{RefreshPreview();};
            apply.Click+=delegate{try{apply.Enabled=false;Desktop.Apply(Catalog.Library(AppDomain.CurrentDomain.BaseDirectory),Selected());status.Text="Applied to each connected display. You can close this window; nothing keeps running.";}catch(Exception ex){status.Text=ex.Message;}finally{apply.Enabled=true;}};
            string selected=Catalog.Default;
            string selection=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TechLoungeOS","Wallpapers","selected.txt");
            if(File.Exists(selection)){string saved=File.ReadAllText(selection).Trim();if(Catalog.Valid(saved))selected=saved;}
            string[] parts=selected.Split('.');design.SelectedIndex=Array.IndexOf(Catalog.Designs,parts[0]);colour.SelectedIndex=Array.IndexOf(Catalog.Colours,parts[1]);
            FormClosed+=delegate{if(preview.Image!=null)preview.Image.Dispose();};
        }
        string Selected(){return Catalog.Designs[design.SelectedIndex]+"."+Catalog.Colours[colour.SelectedIndex];}
        void RefreshPreview() {
            if(design.SelectedIndex<0 || colour.SelectedIndex<0)return;
            string temp=Path.Combine(Path.GetTempPath(),"TLOS-wallpaper-"+Guid.NewGuid().ToString("N")+".png");
            try {
                Renderer.Render(Catalog.PathFor(Catalog.Library(AppDomain.CurrentDomain.BaseDirectory),Selected()),960,540,temp);
                using(Image image=Image.FromFile(temp)){Image previous=preview.Image;preview.Image=new Bitmap(image);if(previous!=null)previous.Dispose();}
            }catch(Exception ex){status.Text=ex.Message;}finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
