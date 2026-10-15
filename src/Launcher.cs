using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

// Lanzador: lleva el .bat incrustado, lo ejecuta y crea el acceso directo del escritorio.
static class Launcher
{
    const string ShortcutName = "Cerrar Steam Epic Razer.lnk";

    static int Main()
    {
        string exePath = Assembly.GetExecutingAssembly().Location;
        string exeDir = Path.GetDirectoryName(exePath);

        try { EnsureDesktopShortcut(exePath, exeDir); }
        catch { /* sin acceso directo no se bloquea el cierre de procesos */ }

        string batPath = Path.Combine(Path.GetTempPath(), "axe-cerrar-" + Guid.NewGuid().ToString("N") + ".bat");
        using (Stream res = Assembly.GetExecutingAssembly().GetManifestResourceStream("script.bat"))
        using (FileStream outFile = File.Create(batPath))
        {
            res.CopyTo(outFile);
        }

        try
        {
            var psi = new ProcessStartInfo("cmd.exe", "/c \"\"" + batPath + "\"\"");
            psi.UseShellExecute = false;
            psi.EnvironmentVariables["AXE_LOG"] = Path.Combine(exeDir, "log.txt");
            using (Process p = Process.Start(psi))
            {
                p.WaitForExit();
                return p.ExitCode;
            }
        }
        finally
        {
            try { File.Delete(batPath); } catch { }
        }
    }

    // Crea el acceso directo, o lo corrige si el .exe se ha movido de carpeta.
    static void EnsureDesktopShortcut(string exePath, string exeDir)
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string lnkPath = Path.Combine(desktop, ShortcutName);

        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        object shell = Activator.CreateInstance(shellType);
        object lnk = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
        Type lnkType = lnk.GetType();

        string current = (string)lnkType.InvokeMember("TargetPath", BindingFlags.GetProperty, null, lnk, null);
        if (File.Exists(lnkPath) && string.Equals(current, exePath, StringComparison.OrdinalIgnoreCase))
            return;

        lnkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { exePath });
        lnkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { exeDir });
        lnkType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { exePath + ",0" });
        lnkType.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "Cierra Steam, Epic Games y Razer" });
        lnkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
    }
}
