using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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
                string script = Path.Combine(Path.GetTempPath(), "NovusLauncherSetup.ps1");
                using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream("NovusLauncherSetup.ps1"))
                {
                    if (input == null)
                    {
                        throw new InvalidOperationException("Installer resource NovusLauncherSetup.ps1 was not embedded.");
                    }
                    using (var output = File.Create(script))
                    {
                        input.CopyTo(output);
                    }
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
