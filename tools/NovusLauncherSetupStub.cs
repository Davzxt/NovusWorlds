using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Windows.Forms;

namespace NovusWorldsSetup
{
    internal static class Program
    {
        [STAThread]
        private static int Main()
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
                string url = "https://raw.githubusercontent.com/Davzxt/NovusWorlds/main/launcher/NovusLauncherSetup.ps1";
                string script = Path.Combine(Path.GetTempPath(), "NovusLauncherSetup.ps1");
                using (var web = new WebClient())
                {
                    web.Headers.Add("User-Agent", "NovusWorldsSetup/1.0");
                    web.DownloadFile(url, script);
                }

                var start = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"",
                    UseShellExecute = false,
                    CreateNoWindow = false
                };
                Process.Start(start);
                return 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Novus Launcher Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }
}
