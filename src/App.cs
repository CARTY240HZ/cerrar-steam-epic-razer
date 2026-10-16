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
        app.ShutdownMode = ShutdownMode.OnLastWindowClose;
        return app.Run(new SplashWindow());
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

// Registro de depuracion: solo con la variable de entorno AXE_DEBUG=1 (%LOCALAPPDATA%\AXE\CerrarLanzadores\debug.log).
static class Dbg
{
    static readonly bool on = Environment.GetEnvironmentVariable("AXE_DEBUG") == "1";
    static readonly Stopwatch clock = Stopwatch.StartNew();
    public static void Log(string msg)
    {
        if (!on) return;
        try
        {
            Directory.CreateDirectory(Program.DataDir);
            File.AppendAllText(Path.Combine(Program.DataDir, "debug.log"), clock.ElapsedMilliseconds.ToString().PadLeft(6) + "  " + msg + Environment.NewLine);
        }
        catch { }
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

// Arranque comun de los WebView2 de la pantalla de carga y del panel.
static class WebHost
{
    static bool loaderReady;
    // Una instancia por control: compartirla entre dos controles hace fallar el segundo (0x8007139F).
    static CoreWebView2CreationProperties Props()
    {
        return new CoreWebView2CreationProperties { UserDataFolder = Path.Combine(Program.DataDir, "wv2") };
    }

    public static WebView2CompositionControl Create()
    {
        var web = new WebView2CompositionControl();
        web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x0E, 0x10, 0x13);
        web.CreationProperties = Props();
        return web;
    }

    static void PrepareLoader()
    {
        if (loaderReady) return;
        string loaderDir = Path.GetDirectoryName(Embedded.Extract("WebView2Loader.dll", Path.Combine(Program.DataDir, "bin")));
        CoreWebView2Environment.SetLoaderDllFolderPath(loaderDir);
        loaderReady = true;
    }

    static void Lock(CoreWebView2 core)
    {
        CoreWebView2Settings s = core.Settings;
        s.AreDefaultContextMenusEnabled = false;
        s.AreDevToolsEnabled = false;
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = false;
        s.AreBrowserAcceleratorKeysEnabled = false;
    }

    public static async Task Init(WebView2CompositionControl web)
    {
        PrepareLoader();
        await web.EnsureCoreWebView2Async(null);
        Lock(web.CoreWebView2);
    }

    public static string Page(string name)
    {
        return Embedded.Text(name)
            .Replace("%%BRANDMARK%%", Embedded.Text("brandmark.svg"))
            .Replace("%%XMARK%%", Embedded.Text("xmark.svg"));
    }

    // Sin WebView2 no hay ventana: se avisa y se abre el .bat clasico.
    public static void Fail(Window owner, Exception ex)
    {
        // Sin propietario: el panel puede estar fuera de pantalla y el aviso quedaria oculto.
        MessageBox.Show(
            "No se pudo abrir la ventana (falta WebView2 o no arranca).\nSe abrira el modo consola.\n\n" + ex.Message,
            "AXE · Cerrar lanzadores", MessageBoxButton.OK, MessageBoxImage.Warning);
        ConsoleMode.Launch();
        Application.Current.Shutdown();
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    // DWMWA_CLOAK: la ventana sigue pintandose pero no se ve ni recibe clics. Se usa para preparar el
    // panel mientras se ve el loader (WPF quita WS_EX_LAYERED, asi que el alfa 0 no servia y el panel
    // tapaba el loader; moverla desde fuera de pantalla daba un fotograma en blanco).
    public static void Hide(Window w)
    {
        int v = 1;
        DwmSetWindowAttribute(new WindowInteropHelper(w).Handle, 13, ref v, 4);
    }

    public static void Show(Window w)
    {
        int v = 0;
        DwmSetWindowAttribute(new WindowInteropHelper(w).Handle, 13, ref v, 4);
    }

    public static void DarkFrame(Window w, bool roundCorners = false)
    {
        IntPtr hwnd = new WindowInteropHelper(w).Handle;
        int on = 1;
        DwmSetWindowAttribute(hwnd, 20, ref on, 4);      // DWMWA_USE_IMMERSIVE_DARK_MODE
        int caption = 0x0013100E;                        // #0E1013 en COLORREF (0x00BBGGRR)
        DwmSetWindowAttribute(hwnd, 35, ref caption, 4); // DWMWA_CAPTION_COLOR (Windows 11)
        if (roundCorners)
        {
            int round = 2;                                 // DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, 33, ref round, 4); // DWMWA_WINDOW_CORNER_PREFERENCE
        }
    }
}

// Loader en ventana pequeña con la animacion del de Flow. Los pasos de la barra son reales:
// busca procesos y carga el panel oculto; al terminar se funde y aparece el panel.
class SplashWindow : Window
{
    readonly WebView2CompositionControl web = WebHost.Create();
    readonly TaskCompletionSource<bool> introDone = new TaskCompletionSource<bool>();
    readonly TaskCompletionSource<bool> finished = new TaskCompletionSource<bool>();
    bool started;

    public SplashWindow()
    {
        Title = "AXE";
        Width = 520; Height = 320;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(0x11, 0x10, 0x13));
        Content = web;

        SourceInitialized += delegate { WebHost.DarkFrame(this, true); };
        Loaded += OnLoaded;
    }

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Dbg.Log("splash loaded");
        try { await WebHost.Init(web); }
        catch (Exception ex) { Dbg.Log("splash init fail " + ex); WebHost.Fail(this, ex); return; }
        Dbg.Log("splash webview ok");

        // El control se ve cuando la pagina ya ha pintado: sin destello negro al abrir.
        ((UIElement)web).Opacity = 0;
        web.CoreWebView2.NavigationCompleted += delegate { ((UIElement)web).Opacity = 1; };
        web.CoreWebView2.WebMessageReceived += OnMessage;
        web.CoreWebView2.NavigateToString(WebHost.Page("splash.html"));
    }

    void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string msg = e.TryGetWebMessageAsString();
        Dbg.Log("splash msg " + msg);
        if (msg == "ready" && !started) { started = true; Run(); }
        else if (msg == "intro-done") introDone.TrySetResult(true);
        else if (msg == "flown") finished.TrySetResult(true);
        else if (msg == "skip") introDone.TrySetResult(true);
        else if (msg == "drag")
        {
            try { DragMove(); } catch (InvalidOperationException) { /* el boton ya se habia soltado */ }
        }
    }

    async void Run()
    {
        Step("Iniciando…", 12);
        await Task.Delay(900);

        Step("Buscando procesos…", 34);
        int open = await Task.Run(() => Launchers.OpenCount());
        await Task.Delay(500);
        Step(open == 0 ? "Nada abierto" : open + (open == 1 ? " proceso abierto" : " procesos abiertos"), 52);

        var main = new MainWindow(true);
        var ready = new TaskCompletionSource<bool>();
        main.Ready += delegate { ready.TrySetResult(true); };
        main.Show();
        Dbg.Log("main shown");
        await Task.Delay(400);
        Step("Cargando interfaz…", 76);
        await Task.WhenAll(Task.WhenAny(ready.Task, Task.Delay(15000)), Task.WhenAny(introDone.Task, Task.Delay(8000)));
        await Task.Delay(500);

        Dbg.Log("waits done ready=" + ready.Task.IsCompleted + " intro=" + introDone.Task.IsCompleted);
        Step("Listo", 100);
        await Task.Delay(650);

        Post(new { type = "fly" });
        await Task.WhenAny(finished.Task, Task.Delay(2000));

        main.Reveal();
        main.IntroDone();
        main.Activate();
#if UITEST
        // Solo en la compilacion de prueba (/define:UITEST): ejecuta JS para comprobar la interfaz.
        string testJs = Environment.GetEnvironmentVariable("AXE_TEST_JS");
        if (!string.IsNullOrEmpty(testJs)) main.TestScript(testJs);
#endif
        Close();
    }

    void Step(string text, int pct)
    {
        Post(new { type = "step", text = text, pct = pct });
    }

    void Post(object data)
    {
        if (web.CoreWebView2 != null) web.CoreWebView2.PostWebMessageAsJson(new JavaScriptSerializer().Serialize(data));
    }
}

class MainWindow : Window
{
    readonly WebView2CompositionControl web = WebHost.Create();
    readonly JavaScriptSerializer json = new JavaScriptSerializer();
    bool scanning;
    bool readyRaised;

    // Se lanza cuando la interfaz ya esta pintada (la pantalla de carga espera a esto).
    public event Action Ready;

    public MainWindow(bool hidden)
    {
        Title = "AXE · Cerrar lanzadores";
        Width = 1080; Height = 720;
        MinWidth = 900; MinHeight = 600;
        Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x10, 0x13));
        Content = web;

        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (hidden)
        {
            // Se crea ya en su sitio pero encubierta (ver WebHost.Hide).
            ShowActivated = false;
            ShowInTaskbar = false;
        }

        SourceInitialized += delegate
        {
            WebHost.DarkFrame(this);
            if (hidden) WebHost.Hide(this);
        };
        Loaded += OnLoaded;
    }

    public void Reveal()
    {
        ShowInTaskbar = true;
        WebHost.Show(this);
    }

#if UITEST
    public async void TestScript(string js)
    {
        await Task.Delay(1500);
        if (web.CoreWebView2 != null) await web.CoreWebView2.ExecuteScriptAsync(js);
    }
#endif

    public void IntroDone()
    {
        if (web.CoreWebView2 != null) web.CoreWebView2.ExecuteScriptAsync("window.axeIntroDone&&window.axeIntroDone()");
    }

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Dbg.Log("main loaded");
        try { await WebHost.Init(web); }
        catch (Exception ex) { Dbg.Log("main init fail " + ex); WebHost.Fail(this, ex); return; }
        Dbg.Log("main webview ok");

        web.CoreWebView2.WebMessageReceived += OnMessage;
        web.CoreWebView2.NavigationCompleted += delegate
        {
            Dbg.Log("main nav completed");
            if (readyRaised) return;
            readyRaised = true;
            if (Ready != null) Ready();
        };
        web.CoreWebView2.NavigateToString(WebHost.Page("ui.html"));
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

    public static int OpenCount()
    {
        return Snapshot().Count(p => !p.Protected);
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

    // Abre la consola sin esperar (la ventana se va a cerrar). El .bat temporal queda en %TEMP%.
    public static void Launch()
    {
        string bat = ExtractBat();
        Process.Start(new ProcessStartInfo("cmd.exe", "/c \"\"" + bat + "\"\"") { UseShellExecute = true });
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
