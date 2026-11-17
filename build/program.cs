using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("RX 工具箱")]
[assembly: AssemblyProduct("RX 工具箱")]
[assembly: AssemblyDescription("GMod 挤服助手 + 屏幕准星 + 对局倒计时 + 自定义常用文本")]
[assembly: AssemblyCompany("RXToolbox")]
[assembly: AssemblyVersion("1.8.0.0")]
[assembly: AssemblyFileVersion("1.8.0.0")]

static class Program
{
    public const string TARGET_EXE_NAME = "Rx工具箱.exe";
    static bool g_dev;      // --dev 启动：多实例 + 标题带 DEV
    static string g_migratedFrom = "";
    public static bool IsDev { get { return g_dev; } }
    public static string MigratedFrom { get { return g_migratedFrom; } }

    public static bool ShouldMigrateLegacyExeName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return false;
        return Regex.IsMatch(fileName.Trim(), @"^RX工具箱\s*V\d+(?:\.\d+)*\.exe$", RegexOptions.IgnoreCase);
    }

    public static string ParseMigratedFromArg(string[] args)
    {
        if (args == null) return "";
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (string.Equals(args[i], "--migrated-from", StringComparison.OrdinalIgnoreCase))
                return (args[i + 1] ?? "").Trim();
        }
        return "";
    }

    public static bool IsRxToolboxProcessName(string processName)
    {
        if (string.IsNullOrEmpty(processName)) return false;
        return processName.StartsWith("RX工具箱", StringComparison.OrdinalIgnoreCase)
            || processName.StartsWith("RX 工具箱", StringComparison.OrdinalIgnoreCase);
    }

    public static int ParseAfterUpdatePid(string[] args)
    {
        if (args == null) return 0;
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (string.Equals(args[i], "--after-update", StringComparison.OrdinalIgnoreCase))
            {
                int pid;
                if (int.TryParse(args[i + 1], out pid) && pid > 0) return pid;
            }
        }
        return 0;
    }

    static void WaitForOldProcessExit(int oldPid, int timeoutMs)
    {
        if (oldPid <= 0) return;
        try
        {
            using (var cur = System.Diagnostics.Process.GetCurrentProcess())
            using (var p = System.Diagnostics.Process.GetProcessById(oldPid))
            {
                if (p.Id == cur.Id) return;
                if ((string.Equals(p.ProcessName, cur.ProcessName, StringComparison.OrdinalIgnoreCase) || IsRxToolboxProcessName(p.ProcessName)) && !p.HasExited)
                    p.WaitForExit(timeoutMs);
            }
        }
        catch { }
    }

    static bool TryMigrateLegacyExeName(int selfPid)
    {
        try
        {
            Assembly asm = Assembly.GetEntryAssembly();
            string exePath = (asm != null && !string.IsNullOrEmpty(asm.Location))
                ? Path.GetFullPath(asm.Location)
                : Path.GetFullPath(Environment.GetCommandLineArgs()[0]);
            string oldName = Path.GetFileName(exePath);
            if (!ShouldMigrateLegacyExeName(oldName)) return false;

            string dir = Path.GetDirectoryName(exePath);
            if (string.IsNullOrEmpty(dir) || !MainForm.CanWriteDir(dir)) return false;

            string targetExe = Path.Combine(dir, TARGET_EXE_NAME);
            if (string.Equals(exePath, targetExe, StringComparison.OrdinalIgnoreCase)) return false;

            MainForm.CleanupUpdateArtifacts(exePath, "", false);

            string backedTarget = "";
            if (File.Exists(targetExe))
            {
                backedTarget = MainForm.PickBackupPath(targetExe);
                MainForm.MoveWithRetry(targetExe, backedTarget, 3);
            }

            try
            {
                MainForm.MoveWithRetry(exePath, targetExe, 4);
                var psi = new System.Diagnostics.ProcessStartInfo();
                psi.FileName = targetExe;
                psi.Arguments = "--after-update " + selfPid + " --migrated-from \"" + oldName.Replace("\"", "") + "\"";
                psi.WorkingDirectory = dir;
                psi.UseShellExecute = false;
                using (var launched = System.Diagnostics.Process.Start(psi)) { }
                return true;
            }
            catch
            {
                try
                {
                    if (!File.Exists(exePath) && File.Exists(targetExe))
                        MainForm.MoveWithRetry(targetExe, exePath, 3);
                    if (!string.IsNullOrEmpty(backedTarget) && File.Exists(backedTarget) && !File.Exists(targetExe))
                        MainForm.MoveWithRetry(backedTarget, targetExe, 3);
                }
                catch { }
                return false;
            }
        }
        catch
        {
            return false;
        }
    }

    [STAThread]
    static void Main()
    {
        // 必须在任何网络请求之前：.NET Framework 默认不启用 TLS 1.2+，
        // 会出现「未能创建 SSL/TLS 安全通道」，导致首次运行拉数据失败
        try
        {
            System.Net.ServicePointManager.SecurityProtocol |=
                (System.Net.SecurityProtocolType)3072 | (System.Net.SecurityProtocolType)12288;
        }
        catch { }

        // 开发用：自检模式，不建窗口，只跑断言并写 selftest.log
        if (Environment.GetEnvironmentVariable("RX_SELFTEST") == "1")
        {
            int fail = MainForm.SelfTest();
            Environment.Exit(fail == 0 ? 0 : 1);
            return;
        }
        string[] args = Environment.GetCommandLineArgs();
        int selfPid = System.Diagnostics.Process.GetCurrentProcess().Id;
        int afterUpdatePid = ParseAfterUpdatePid(args);
        g_migratedFrom = ParseMigratedFromArg(args);
        if (afterUpdatePid == selfPid) afterUpdatePid = 0;
        if (afterUpdatePid > 0) WaitForOldProcessExit(afterUpdatePid, 15000);

        bool createdNew;
        string mutexName = @"Local\RXToolbox_SingleInstance_Mutex";
        // 开发用：--dev 或 RX_MULTI=1 可以让多个实例并存（正式版开着也能测 DEV 版）
        bool isDev = Environment.GetEnvironmentVariable("RX_MULTI") == "1";
        foreach (string a in args)
        {
            if (string.Equals(a, "--dev", StringComparison.OrdinalIgnoreCase)) { isDev = true; break; }
        }
        g_dev = isDev;
        if (isDev)
            mutexName += "_" + selfPid;
        using (var mutex = new Mutex(true, mutexName, out createdNew))
        {
            bool ownsMutex = createdNew;
            if (!ownsMutex && afterUpdatePid > 0)
            {
                try { ownsMutex = mutex.WaitOne(5000); }
                catch (AbandonedMutexException) { ownsMutex = true; }
            }
            if (!ownsMutex)
            {
                if (!Native.ActivateExistingInstance())
                {
                    MessageBox.Show("RX 工具箱已在运行中。", "RX 工具箱", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                return;
            }
            try
            {
                RunApp(afterUpdatePid > 0);
            }
            finally
            {
                if (ownsMutex)
                {
                    try { mutex.ReleaseMutex(); } catch { }
                }
                GC.KeepAlive(mutex);
            }
        }
    }


    static void RunApp(bool afterUpdate)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e)
        {
            if (e != null && e.Exception != null)
                Native.Log("UI ThreadException: " + e.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
        {
            if (e != null && e.ExceptionObject != null)
                Native.Log("Domain UnhandledException: " + e.ExceptionObject);
        };

        string exeDir = AppDomain.CurrentDomain.BaseDirectory;
        string dataDir = exeDir;

        // 程序目录不可写（例如装在 Program Files）时回退到 %LOCALAPPDATA%
        try
        {
            string probe = Path.Combine(exeDir, ".wtest_" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "x");
            File.Delete(probe);
        }
        catch
        {
            dataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RXToolbox");
            try { Directory.CreateDirectory(dataDir); } catch { }
        }

        Native.LogPath = Path.Combine(dataDir, "rx.log");
        MainForm.CleanupUpdateArtifacts(Application.ExecutablePath, "", afterUpdate);

        var titles = new List<string>();
        var lists = new List<string[]>();


        string icoPath = Path.Combine(exeDir, "rx.ico");
        MainForm form = null;
        try
        {
            form = new MainForm(icoPath, titles.ToArray(), lists.ToArray(), dataDir);
        }
        catch (Exception ex)
        {
            Native.Log("startup EX: " + ex);
            MessageBox.Show("启动失败：" + ex.Message, "RX 工具箱", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        form.Text = "RX 工具箱 " + MainForm.APP_VERSION + (g_dev ? " · DEV" : "");

        Application.Run(form);
    }
}
