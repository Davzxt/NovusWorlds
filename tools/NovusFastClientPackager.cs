using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

internal static class NovusFastClientPackager
{
    [STAThread]
    private static void Main()
    {
        var toolsDir = AppDomain.CurrentDomain.BaseDirectory;
        var script = Path.Combine(toolsDir, "fast-client-download.ps1");
        var logPath = Path.Combine(Path.GetTempPath(), "novus-fast-client-package.log");
        if (!File.Exists(script))
        {
            MessageBox.Show("fast-client-download.ps1 nao foi encontrado na pasta tools.", "Novus Fast Client Packager", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        try
        {
            var toolsParent = Directory.GetParent(toolsDir.TrimEnd(Path.DirectorySeparatorChar));
            var info = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = toolsParent == null ? toolsDir : toolsParent.FullName
            };

            var output = new StringBuilder();
            using (var process = Process.Start(info))
            {
                if (process == null) throw new InvalidOperationException("Nao foi possivel iniciar PowerShell.");
                output.Append(process.StandardOutput.ReadToEnd());
                output.Append(process.StandardError.ReadToEnd());
                process.WaitForExit();
                File.WriteAllText(logPath, output.ToString());
                if (process.ExitCode != 0)
                {
                    MessageBox.Show("Falhou. Log salvo em:\n" + logPath, "Novus Fast Client Packager", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
            }

            MessageBox.Show("Download do client atualizado.\nLog salvo em:\n" + logPath, "Novus Fast Client Packager", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            File.WriteAllText(logPath, ex.ToString());
            MessageBox.Show("Falhou. Log salvo em:\n" + logPath, "Novus Fast Client Packager", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
