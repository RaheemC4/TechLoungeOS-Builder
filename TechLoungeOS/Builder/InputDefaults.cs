namespace TechLoungeOS;

public static class InputDefaults
{
    public const string Script = """
        Invoke-Step 'input-defaults' {
          if(-not ('TechLoungeOSInput' -as [type])) {
            Add-Type @'
        using System;using System.Runtime.InteropServices;
        public static class TechLoungeOSInput {
          [DllImport("user32.dll",EntryPoint="SystemParametersInfoW",SetLastError=true)]
          private static extern bool Spi(uint action,uint parameter,[In,Out]int[] value,uint flags);
          private static void Check(bool ok){if(!ok)throw new System.ComponentModel.Win32Exception();}
          public static int[] Mouse(){var v=new int[3];Check(Spi(3,0,v,0));return v;}
          public static int Wheel(){var v=new int[1];Check(Spi(104,0,v,0));return v[0];}
          public static void Apply(int[] mouse,int wheel){Check(Spi(4,0,mouse,3));Check(Spi(105,(uint)wheel,null,3));}
        }
        '@
          }
          $mouse=[TechLoungeOSInput]::Mouse()
          $wheel=[TechLoungeOSInput]::Wheel()
          $backup=Join-Path $state input-before.xml
          if(!(Test-Path -LiteralPath $backup)){[pscustomobject]@{Mouse=$mouse.Clone();Wheel=$wheel}|Export-Clixml -LiteralPath $backup}
          $mouse[2]=0
          [TechLoungeOSInput]::Apply($mouse,6)
          if([TechLoungeOSInput]::Mouse()[2] -ne 0 -or [TechLoungeOSInput]::Wheel() -ne 6){throw 'Windows did not confirm pointer acceleration off and six-line scrolling.'}
        }
        """;
}
