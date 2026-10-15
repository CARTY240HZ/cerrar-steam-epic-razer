using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

// AXE · Cerrar lanzadores. Ventana WPF + WebView2 con el mismo panel que AXE, en rojo.
// Los DLL de WebView2 van incrustados en el .exe: se descarga un solo archivo.
static class Program
{
    public static readonly string ExePath = Assembly.GetExecutingAssembly().Location;
    public static readonly string ExeDir = Path.GetDirectoryName(ExePath);
    public static readonly string LogPath = Path.Combine(ExeDir, "log.txt");
    public static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AXE", "CerrarLanzadores");

    static readonly Dictionary<string, Assembly> loaded = new Dictionary<string, Assembly>();

    [STAThread]
    static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbedded;

        try { Shortcut.Ensure(); }
        catch { /* sin acceso directo no se bloquea el cierre de procesos */ }

        if (args.Any(a => string.Equals(a, "--consola", StringComparison.OrdinalIgnoreCase)))
            return ConsoleMode.Run();

        return RunGui();
    }

    // Separado de Main para que los tipos de WebView2 se resuelvan despues de registrar AssemblyResolve.
    [MethodImpl(MethodImplOptions.NoInlining)]
    static int RunGui()
    {
        var app = new Application();
        app.ShutdownMode = ShutdownMode.OnMainWindowClose;
        return app.Run(new MainWindow());
    }

    static Assembly ResolveEmbedded(object sender, ResolveEventArgs e)
    {
        string file = new AssemblyName(e.Name).Name + ".dll";
        lock (loaded)
        {
            Assembly asm;
            if (loaded.TryGetValue(file, out asm)) return asm;
            byte[] bytes = Embedded.Bytes(file);
            if (bytes == null) return null;
            asm = Assembly.Load(bytes);
            loaded[file] = asm;
            return asm;
        }
    }
}

static class Embedded
{
    public static byte[] Bytes(string name)
    {
        using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
        {
            if (s == null) return null;
            var ms = new MemoryStream();
            s.CopyTo(ms);
            return ms.ToArray();
        }
    }

    public static string Text(string name)
    {
        return Encoding.UTF8.GetString(Bytes(name));
    }

    // Copia un recurso a disco si falta o ha cambiado de tamaño, y devuelve la ruta.
    public static string Extract(string name, string dir)
    {
        byte[] bytes = Bytes(name);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, name);
        if (!File.Exists(path) || new FileInfo(path).Length != bytes.Length)
            File.WriteAllBytes(path, bytes);
        return path;
    }
}

class MainWindow : Window
{
    readonly WebView2 web = new WebView2();
    readonly JavaScriptSerializer json = new JavaScriptSerializer();
    bool scanning;

    public MainWindow()
    {
        Title = "AXE · Cerrar lanzadores";
        Width = 1080; Height = 720;
        MinWidth = 900; MinHeight = 600;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x10, 0x13));

        web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x0E, 0x10, 0x13);
        web.CreationProperties = new CoreWebView2CreationProperties { UserDataFolder = Path.Combine(Program.DataDir, "wv2") };
        Content = web;

        SourceInitialized += delegate { DarkTitleBar(); };
        Loaded += OnLoaded;
    }

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            string loaderDir = Path.GetDirectoryName(Embedded.Extract("WebView2Loader.dll", Path.Combine(Program.DataDir, "bin")));
            CoreWebView2Environment.SetLoaderDllFolderPath(loaderDir);
            await web.EnsureCoreWebView2Async(null);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this,
                "No se pudo abrir la ventana (falta WebView2 o no arranca).\nSe abrira el modo consola.\n\n" + ex.Message,
                "AXE · Cerrar lanzadores", MessageBoxButton.OK, MessageBoxImage.Warning);
            ConsoleMode.Start();
            Close();
            return;
        }

        CoreWebView2Settings s = web.CoreWebView2.Settings;
        s.AreDefaultContextMenusEnabled = false;
        s.AreDevToolsEnabled = false;
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = false;
        s.AreBrowserAcceleratorKeysEnabled = false;

        web.CoreWebView2.WebMessageReceived += OnMessage;
        string html = Embedded.Text("ui.html").Replace("%%BRANDMARK%%", Embedded.Text("brandmark.svg"));
        web.NavigateToString(html);
    }

    async void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        Dictionary<string, object> msg;
        try { msg = json.Deserialize<Dictionary<string, object>>(e.TryGetWebMessageAsString()); }
        catch { return; }

        object cmdObj;
        string cmd = msg.TryGetValue("cmd", out cmdObj) ? cmdObj as string : null;

        if (cmd == "scan")
        {
            if (scanning) return;
            scanning = true;
            try { Post(await Task.Run(() => Launchers.Scan())); }
            finally { scanning = false; }
        }
        else if (cmd == "close")
        {
            object g;
            string group = msg.TryGetValue("group", out g) ? g as string : "all";
            Post(await Task.Run(() => Launchers.Close(group ?? "all")));
        }
        else if (cmd == "elevate")
        {
            Elevate();
        }
        else if (cmd == "openLog")
        {
            if (File.Exists(Program.LogPath)) Process.Start(Program.LogPath);
        }
    }

    void Post(object data)
    {
        if (web.CoreWebView2 != null) web.CoreWebView2.PostWebMessageAsJson(json.Serialize(data));
    }

    void Elevate()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Program.ExePath) { Verb = "runas", UseShellExecute = true, WorkingDirectory = Program.ExeDir });
            Close();
        }
        catch (Win32Exception) { /* el usuario ha cancelado el UAC */ }
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    void DarkTitleBar()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int on = 1;
        DwmSetWindowAttribute(hwnd, 20, ref on, 4);      // DWMWA_USE_IMMERSIVE_DARK_MODE
        int caption = 0x0013100E;                        // #0E1013 en COLORREF (0x00BBGGRR)
        DwmSetWindowAttribute(hwnd, 35, ref caption, 4); // DWMWA_CAPTION_COLOR (Windows 11)
    }
}

// Deteccion y cierre. Mismas reglas que el .bat: prefijos steam/epic/razer/rz,
// y los procesos Razer con "Manager" se dejan vivos porque controlan los perifericos.
static class Launchers
{
    class Group
    {
        public string Id; public string[] Prefixes;
        public Group(string id, params string[] prefixes) { Id = id; Prefixes = prefixes; }
    }

    static readonly Group[] Groups =
    {
        new Group("steam", "steam"),
        new Group("epic", "epic"),
        new Group("razer", "razer", "rz"),
    };

    class Proc { public int Pid; public string Name; public long Mem; public bool Protected; public string Group; }

    static List<Proc> Snapshot()
    {
        var list = new List<Proc>();
        foreach (Process p in Process.GetProcesses())
        {
            using (p)
            {
                string name = p.ProcessName;
                foreach (Group g in Groups)
                {
                    if (!g.Prefixes.Any(x => name.StartsWith(x, StringComparison.OrdinalIgnoreCase))) continue;
                    long mem = 0;
                    try { mem = p.WorkingSet64; } catch { }
                    list.Add(new Proc
                    {
                        Pid = p.Id, Name = name + ".exe", Mem = mem, Group = g.Id,
                        Protected = g.Id == "razer" && name.IndexOf("manager", StringComparison.OrdinalIgnoreCase) >= 0,
                    });
                    break;
                }
            }
        }
        return list;
    }

    public static bool IsAdmin()
    {
        using (WindowsIdentity id = WindowsIdentity.GetCurrent())
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static object Scan()
    {
        List<Proc> all = Snapshot();
        var groups = new List<object>();
        foreach (Group g in Groups)
        {
            List<Proc> mine = all.Where(p => p.Group == g.Id).ToList();
            var procs = mine
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(x => new { name = x.First().Name, count = x.Count(), prot = x.First().Protected })
                .OrderBy(x => x.prot).ThenByDescending(x => x.count).ThenBy(x => x.name)
                .ToList();
            groups.Add(new
            {
                id = g.Id,
                procs = procs,
                total = mine.Count,
                closable = mine.Count(p => !p.Protected),
                mem = mine.Where(p => !p.Protected).Sum(p => p.Mem),
            });
        }
        return new { type = "scan", admin = IsAdmin(), groups = groups };
    }

    public static object Close(string group)
    {
        List<Proc> targets = Snapshot().Where(p => group == "all" || p.Group == group).ToList();
        bool admin = IsAdmin();
        var rows = new List<KeyValuePair<Proc, string[]>>(); // proceso -> {status, motivo}

        Log("--- Inicio (ventana) ---");
        foreach (Proc p in targets)
        {
            string status, why;
            if (p.Protected)
            {
                status = "skip"; why = "periférico, se mantiene activo";
                Log("OMITIDO (periferico): " + p.Name);
            }
            else if (!Alive(p.Pid))
            {
                // Ya cayo junto a otro proceso del arbol (taskkill /T): no es un error.
                status = "ok"; why = "ya se había cerrado";
                Log("CERRADO: " + p.Name);
            }
            else if (TaskKill(p.Pid) || !Alive(p.Pid))
            {
                status = "ok"; why = "";
                Log("CERRADO: " + p.Name);
            }
            else
            {
                status = "err"; why = admin ? "no se pudo cerrar" : "requiere administrador";
                Log("ERROR al cerrar: " + p.Name);
            }
            rows.Add(new KeyValuePair<Proc, string[]>(p, new[] { status, why }));
        }

        // Una fila por nombre y resultado: "steamwebhelper.exe ×7" en vez de siete filas iguales.
        var items = rows
            .GroupBy(r => r.Key.Name.ToLowerInvariant() + "|" + r.Value[0] + "|" + r.Value[1])
            .Select(x =>
            {
                int n = x.Count();
                var first = x.First();
                return new { name = first.Key.Name + (n > 1 ? " ×" + n : ""), status = first.Value[0], why = first.Value[1], n = n };
            })
            .ToList();

        return new { type = "result", items = items };
    }

    static bool Alive(int pid)
    {
        try
        {
            using (Process p = Process.GetProcessById(pid)) return !p.HasExited;
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
        catch (Win32Exception) { return true; } // existe pero sin permiso para consultarlo
    }

    static bool TaskKill(int pid)
    {
        var psi = new ProcessStartInfo("taskkill.exe", "/F /T /PID " + pid)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using (Process tk = Process.Start(psi))
        {
            tk.StandardOutput.ReadToEnd();
            tk.StandardError.ReadToEnd();
            tk.WaitForExit();
            return tk.ExitCode == 0;
        }
    }

    static readonly object logLock = new object();

    static void Log(string line)
    {
        try
        {
            lock (logLock)
                File.AppendAllText(Program.LogPath,
                    "[" + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss,ff") + "] " + line + " \r\n", Encoding.Default);
        }
        catch { /* carpeta sin permiso de escritura: se sigue sin log */ }
    }
}

// Modo clasico: el .bat de siempre en una consola. Se usa con --consola o si WebView2 falla.
static class ConsoleMode
{
    static string ExtractBat()
    {
        string path = Path.Combine(Path.GetTempPath(), "axe-cerrar-" + Guid.NewGuid().ToString("N") + ".bat");
        File.WriteAllBytes(path, Embedded.Bytes("script.bat"));
        Environment.SetEnvironmentVariable("AXE_LOG", Program.LogPath);
        return path;
    }

    public static int Run()
    {
        string bat = ExtractBat();
        try
        {
            using (Process p = Process.Start(new ProcessStartInfo("cmd.exe", "/c \"\"" + bat + "\"\"") { UseShellExecute = true }))
            {
                p.WaitForExit();
                return p.ExitCode;
            }
        }
        finally { try { File.Delete(bat); } catch { } }
    }

    public static void Start()
    {
        Task.Run(() => Run());
    }
}

static class Shortcut
{
    const string Name = "Cerrar Steam Epic Razer.lnk";

    // Crea el acceso directo del escritorio, o lo corrige si el .exe se ha movido de carpeta.
    public static void Ensure()
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string lnkPath = Path.Combine(desktop, Name);

        Type shellType = Type.GetTypeFromProgID("WScript.Shell");
        object shell = Activator.CreateInstance(shellType);
        object lnk = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
        Type t = lnk.GetType();

        string current = (string)t.InvokeMember("TargetPath", BindingFlags.GetProperty, null, lnk, null);
        if (File.Exists(lnkPath) && string.Equals(current, Program.ExePath, StringComparison.OrdinalIgnoreCase))
            return;

        t.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { Program.ExePath });
        t.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Program.ExeDir });
        t.InvokeMember("IconLocation", BindingFlags.SetProperty, null, lnk, new object[] { Program.ExePath + ",0" });
        t.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "Cierra Steam, Epic Games y Razer" });
        t.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
    }
}
