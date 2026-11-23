using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using WSize = Windows.Foundation.Size;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

public static class Native
{
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern void keybd_event(byte b, byte s, uint f, UIntPtr e);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr h, uint acc, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool GetTokenInformation(IntPtr token, int cls, out uint info, int len, out int ret);
    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr h);

    // 当前进程是否以管理员身份运行
    public static bool IsElevated()
    {
        IntPtr token = IntPtr.Zero;
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), 0x0008 /*TOKEN_QUERY*/, out token)) return false;
            uint elev;
            int ret;
            if (!GetTokenInformation(token, 20 /*TokenElevation*/, out elev, 4, out ret)) return false;
            return elev != 0;
        }
        catch { return false; }
        finally { if (token != IntPtr.Zero) CloseHandle(token); }
    }

    [DllImport("kernel32.dll")] static extern void Sleep(uint ms);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int n);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern uint MapVirtualKeyW(uint code, uint mapType);

    // ===== 输入法切换（避免中文输入法吞键）=====
    [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint idThread);
    [DllImport("user32.dll")] static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[] lpList);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadKeyboardLayoutW(string id, uint flags);
    [DllImport("user32.dll")] static extern bool UnloadKeyboardLayout(IntPtr hkl);

    const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;

    static IntPtr FindInstalledEnglishLayout()
    {
        try
        {
            int n = GetKeyboardLayoutList(0, null);
            if (n <= 0 || n > 64) return IntPtr.Zero;
            var list = new IntPtr[n];
            int got = GetKeyboardLayoutList(n, list);
            for (int i = 0; i < got; i++)
            {
                uint langId = unchecked((uint)list[i].ToInt64()) & 0xFFFFu;
                if (langId == 0x0409u) return list[i];
            }
        }
        catch { }
        return IntPtr.Zero;
    }

    public static IntPtr SwitchToEnglish(IntPtr hwnd)
    {
        IntPtr dummy;
        return SwitchToEnglish(hwnd, out dummy);
    }

    public static IntPtr SwitchToEnglish(IntPtr hwnd, out IntPtr tempLoaded)
    {
        IntPtr old = IntPtr.Zero;
        tempLoaded = IntPtr.Zero;
        try
        {
            uint pid;
            uint tid = GetWindowThreadProcessId(hwnd, out pid);
            old = GetKeyboardLayout(tid);
            IntPtr en = FindInstalledEnglishLayout();
            if (en == IntPtr.Zero)
            {
                en = LoadKeyboardLayoutW("04090409", 0x00000001);
                if (en != IntPtr.Zero && en != old) tempLoaded = en;
            }
            if (en != IntPtr.Zero) PostMessage(hwnd, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, en);
        }
        catch { }
        return old;
    }

    public static void RestoreLayout(IntPtr hwnd, IntPtr layout)
    {
        RestoreLayout(hwnd, layout, IntPtr.Zero);
    }

    public static void RestoreLayout(IntPtr hwnd, IntPtr layout, IntPtr tempLoaded)
    {
        if (hwnd != IntPtr.Zero && layout != IntPtr.Zero)
        {
            try { PostMessage(hwnd, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, layout); }
            catch { }
        }
        if (tempLoaded != IntPtr.Zero && tempLoaded != layout)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    System.Threading.Thread.Sleep(120);
                    UnloadKeyboardLayout(tempLoaded);
                }
                catch { }
            });
        }
    }

    public static void PressCtrlV()
    {
        byte ctrl = (byte)(MapVirtualKeyW(0x11, 0) & 0xFF);
        byte v = (byte)(MapVirtualKeyW(0x56, 0) & 0xFF);
        keybd_event(0x11, ctrl, 0, UIntPtr.Zero);
        Sleep(4);
        keybd_event(0x56, v, 0, UIntPtr.Zero);
        Sleep(4);
        keybd_event(0x56, v, 2, UIntPtr.Zero);
        Sleep(4);
        keybd_event(0x11, ctrl, 2, UIntPtr.Zero);
    }

    public delegate bool EnumWndProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr h, EnumWndProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint msg, IntPtr w, StringBuilder l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessageW(IntPtr h, uint msg, IntPtr w, string l);
    public static void SetCueBanner(IntPtr h, string text)
    {
        try { SendMessageW(h, 0x1501, (IntPtr)1, text); } catch { }   // EM_SETCUEBANNER
    }
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
    public delegate bool EnumProc(IntPtr h, IntPtr p);

    public struct RECT { public int L, T, R, B; }

    public const int WM_RX_WAKE = 0x8001;
    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    static readonly object logLock = new object();

    public static void MakeTopmost(IntPtr h)
    {
        SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }

    public static void ForceForegroundWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        IntPtr fg = GetForegroundWindow();
        uint curTid = GetCurrentThreadId();
        uint fgPid;
        uint fgTid = (fg != IntPtr.Zero) ? GetWindowThreadProcessId(fg, out fgPid) : 0;
        bool attached = false;
        try
        {
            if (fgTid != 0 && fgTid != curTid)
                attached = AttachThreadInput(curTid, fgTid, true);
            ShowWindow(hwnd, 5); // SW_SHOW
            MakeTopmost(hwnd);
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        catch { }
        finally
        {
            if (attached)
            {
                try { AttachThreadInput(curTid, fgTid, false); } catch { }
            }
        }
    }

    public static Rectangle GetTargetScreenBounds()
    {
        try
        {
            IntPtr gw = FindGameWindow();
            if (gw != IntPtr.Zero) return Screen.FromHandle(gw).Bounds;
        }
        catch { }
        try { return Screen.PrimaryScreen.Bounds; }
        catch { return new Rectangle(0, 0, GetSystemMetrics(0), GetSystemMetrics(1)); }
    }

    public static void CenterPhysical(IntPtr h)
    {
        RECT r;
        if (!GetWindowRect(h, out r)) return;
        int w = r.R - r.L, ht = r.B - r.T;
        Rectangle scr = GetTargetScreenBounds();
        SetWindowPos(h, IntPtr.Zero, scr.X + (scr.Width - w) / 2, scr.Y + (scr.Height - ht) / 2, 0, 0, 0x0001 | 0x0004 | 0x0010);
    }

    public static string LogPath = "";

    static readonly Dictionary<string, long> warnTicks = new Dictionary<string, long>();

    public static void LogWarn(string tag, Exception ex)
    {
        if (ex == null || string.IsNullOrEmpty(LogPath)) return;
        string msg = (ex.Message ?? "").Replace('\r', ' ').Replace('\n', ' ');
        if (msg.Length > 200) msg = msg.Substring(0, 200);
        string t = string.IsNullOrEmpty(tag) ? "app" : tag;
        string key = t + "|" + ex.GetType().Name + "|" + msg;
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        long window = System.Diagnostics.Stopwatch.Frequency * 5L;
        lock (logLock)
        {
            long prev;
            if (warnTicks.TryGetValue(key, out prev) && (now - prev) < window) return;
            if (warnTicks.Count >= 64) warnTicks.Clear();
            warnTicks[key] = now;
        }
        Log("[WARN] " + t + ": " + ex.GetType().Name + " - " + msg);
    }

    public static void Log(string s)
    {
        if (string.IsNullOrEmpty(LogPath)) return;
        try
        {
            lock (logLock)
            {
                try
                {
                    var fi = new FileInfo(LogPath);
                    if (fi.Exists && fi.Length > 2 * 1024 * 1024)
                    {
                        string bak = LogPath + ".old";
                        if (File.Exists(bak)) File.Delete(bak);
                        File.Move(LogPath, bak);
                    }
                }
                catch { }
                File.AppendAllText(LogPath, DateTime.Now.ToString("MM-dd HH:mm:ss.fff") + "  " + s + "\r\n", Encoding.UTF8);
            }
        }
        catch { }
    }

    public static long GetLogSizeBytes()
    {
        if (string.IsNullOrEmpty(LogPath)) return 0;
        try
        {
            lock (logLock)
            {
                long total = 0;
                var fi = new FileInfo(LogPath);
                if (fi.Exists) total += fi.Length;
                var fiOld = new FileInfo(LogPath + ".old");
                if (fiOld.Exists) total += fiOld.Length;
                return total;
            }
        }
        catch { return 0; }
    }

    public static bool ClearLog()
    {
        if (string.IsNullOrEmpty(LogPath)) return false;
        try
        {
            lock (logLock)
            {
                if (File.Exists(LogPath)) File.Delete(LogPath);
                string bak = LogPath + ".old";
                if (File.Exists(bak)) File.Delete(bak);
                return true;
            }
        }
        catch { return false; }
    }

    public static int PidOf(IntPtr h)
    {
        uint pid;
        GetWindowThreadProcessId(h, out pid);
        return (int)pid;
    }

    public static bool Down(int vk) { return (GetAsyncKeyState(vk) & 0x8000) != 0; }

    public static void PressEnter()
    {
        byte sc = (byte)(MapVirtualKeyW(0x0D, 0) & 0xFF);
        keybd_event(0x0D, sc, 0, UIntPtr.Zero);
        Sleep(6);
        keybd_event(0x0D, sc, 2, UIntPtr.Zero);
    }

    // 找 Garry's Mod 主窗口（class Valve001）
    public static IntPtr FindGameWindow()
    {
        IntPtr found = IntPtr.Zero;
        try
        {
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (!IsWindowVisible(h)) return true;
                var cls = new StringBuilder(64);
                GetClassNameW(h, cls, 64);
                if (cls.ToString() != "Valve001") return true;
                RECT r;
                if (GetWindowRect(h, out r) && (r.R - r.L) > 400) { found = h; return false; }
                return true;
            }, IntPtr.Zero);
        }
        catch { }
        return found;
    }

    public static string ForegroundProcess()
    {
        IntPtr fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return "";
        uint pid;
        GetWindowThreadProcessId(fg, out pid);
        if (pid == 0) return "";
        try
        {
            using (var proc = System.Diagnostics.Process.GetProcessById((int)pid))
            {
                return proc.ProcessName ?? "";
            }
        }
        catch { return ""; }
    }

    public static bool ActivateExistingInstance()
    {
        try
        {
            int selfPid;
            string selfName;
            using (var cur = System.Diagnostics.Process.GetCurrentProcess())
            {
                selfPid = cur.Id;
                selfName = cur.ProcessName;
            }
            System.Diagnostics.Process[] list = System.Diagnostics.Process.GetProcessesByName(selfName);
            bool activated = false;
            for (int i = 0; i < list.Length; i++)
            {
                using (System.Diagnostics.Process p = list[i])
                {
                    if (!activated && p.Id != selfPid)
                    {
                        IntPtr targetHwnd = p.MainWindowHandle;
                        if (targetHwnd == IntPtr.Zero)
                        {
                            int targetPid = p.Id;
                            IntPtr visibleTitleMatch = IntPtr.Zero;
                            IntPtr visibleAny = IntPtr.Zero;
                            IntPtr hiddenTitleMatch = IntPtr.Zero;
                            EnumWindows(delegate(IntPtr h, IntPtr l)
                            {
                                uint wpid;
                                GetWindowThreadProcessId(h, out wpid);
                                if ((int)wpid != targetPid) return true;
                                var titleSb = new StringBuilder(128);
                                GetWindowTextW(h, titleSb, titleSb.Capacity);
                                string wt = titleSb.ToString();
                                bool isMainTitle = wt.StartsWith("RX 工具箱", StringComparison.Ordinal);
                                if (IsWindowVisible(h))
                                {
                                    if (isMainTitle && visibleTitleMatch == IntPtr.Zero) visibleTitleMatch = h;
                                    if (visibleAny == IntPtr.Zero) visibleAny = h;
                                }
                                else if (isMainTitle && hiddenTitleMatch == IntPtr.Zero)
                                {
                                    hiddenTitleMatch = h;
                                }
                                return true;
                            }, IntPtr.Zero);
                            targetHwnd = visibleTitleMatch != IntPtr.Zero ? visibleTitleMatch
                                : (visibleAny != IntPtr.Zero ? visibleAny : hiddenTitleMatch);
                        }
                        if (targetHwnd != IntPtr.Zero)
                        {
                            PostMessage(targetHwnd, (uint)WM_RX_WAKE, IntPtr.Zero, IntPtr.Zero);
                            if (IsWindowVisible(targetHwnd))
                            {
                                if (IsIconic(targetHwnd)) ShowWindow(targetHwnd, 9);
                                else ShowWindow(targetHwnd, 5);
                                SetForegroundWindow(targetHwnd);
                            }
                            activated = true;
                        }
                    }
                }
            }
            return activated;
        }
        catch { return false; }
    }

    // ===== 主题与圆角绘制辅助 =====
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int l, int t, int r, int b, int w, int h);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObj);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool ValidateRect(IntPtr hWnd, IntPtr lpRect);
    [DllImport("user32.dll")] public static extern bool InvalidateRect(IntPtr hWnd, IntPtr lpRect, bool bErase);
    [DllImport("user32.dll")] public static extern IntPtr GetWindowDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public IntPtr hdc;
        public bool fErase;
        public RECT rcPaint;
        public bool fRestore;
        public bool fIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] rgbReserved;
    }

    [DllImport("user32.dll")] public static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT ps);
    [DllImport("user32.dll")] public static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT ps);

    public static void ApplyDarkScrollBar(IntPtr hwnd, bool dark)
    {
        if (hwnd == IntPtr.Zero) return;
        try { SetWindowTheme(hwnd, dark ? "DarkMode_Explorer" : "Explorer", null); }
        catch { }
    }

    public static bool IsSystemDarkMode()
    {
        try
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                if (key != null)
                {
                    object val = key.GetValue("AppsUseLightTheme");
                    if (val != null) return Convert.ToInt32(val) == 0;
                }
            }
        }
        catch { }
        return false;
    }

    public static void SetDarkTitleBar(IntPtr hwnd, bool dark)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            int useDark = dark ? 1 : 0;
            // DWMWA_USE_IMMERSIVE_DARK_MODE: 20 (Win10 2004+ / Win11), 19 (旧版 Win10)
            if (DwmSetWindowAttribute(hwnd, 20, ref useDark, 4) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref useDark, 4);
        }
        catch { }
    }

    public static bool TrySetDwmRoundedCorners(IntPtr hwnd, Color borderColor)
    {
        if (hwnd == IntPtr.Zero) return false;
        try
        {
            int pref = 2; // DWMWCP_ROUND (Win11 原生 8px 抗锯齿圆角与阴影)
            if (DwmSetWindowAttribute(hwnd, 33, ref pref, 4) != 0)
                return false;
            int colorRef = (borderColor.B << 16) | (borderColor.G << 8) | borderColor.R;
            DwmSetWindowAttribute(hwnd, 34, ref colorRef, 4); // DWMWA_BORDER_COLOR
            return true;
        }
        catch { return false; }
    }

    public static void ApplyRoundRegion(Control c, int radius)
    {
        if (c == null || c.IsDisposed || c.Width <= 0 || c.Height <= 0) return;
        IntPtr hrgn = IntPtr.Zero;
        try
        {
            int d = radius * 2;
            hrgn = CreateRoundRectRgn(0, 0, c.Width + 1, c.Height + 1, d, d);
            if (hrgn != IntPtr.Zero)
            {
                Region old = c.Region;
                c.Region = Region.FromHrgn(hrgn);
                if (old != null) old.Dispose();
            }
        }
        catch { }
        finally
        {
            if (hrgn != IntPtr.Zero) DeleteObject(hrgn);
        }
    }

    public static GraphicsPath RoundedPath(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        if (r.Width <= 0 || r.Height <= 0) return path;
        int rad = Math.Max(0, Math.Min(radius, Math.Min(r.Width, r.Height) / 2));
        if (rad <= 0)
        {
            path.AddRectangle(r);
            return path;
        }
        int d = rad * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRoundedRect(Graphics g, Brush b, Rectangle r, int radius)
    {
        if (g == null || b == null || r.Width <= 0 || r.Height <= 0) return;
        SmoothingMode old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = RoundedPath(r, radius))
            g.FillPath(b, path);
        g.SmoothingMode = old;
    }

    public static void DrawRoundedRect(Graphics g, Pen p, Rectangle r, int radius)
    {
        if (g == null || p == null || r.Width <= 0 || r.Height <= 0) return;
        SmoothingMode old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var path = RoundedPath(r, radius))
            g.DrawPath(p, path);
        g.SmoothingMode = old;
    }

    // ===== 32-bit Per-Pixel Alpha 分层窗口（UpdateLayeredWindow）=====
    [StructLayout(LayoutKind.Sequential)]
    struct POINT_API { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    struct SIZE_API { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("gdi32.dll", SetLastError = true)]
    static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll", SetLastError = true)]
    static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll", SetLastError = true)]
    static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool UpdateLayeredWindow(
        IntPtr hwnd, IntPtr hdcDst, ref POINT_API pptDst, ref SIZE_API psize,
        IntPtr hdcSrc, ref POINT_API pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

    const int ULW_ALPHA = 0x00000002;
    const byte AC_SRC_OVER = 0x00;
    const byte AC_SRC_ALPHA = 0x01;

    /// <summary>
    /// 把 32bpp Premultiplied ARGB 位图一次性提交到 WS_EX_LAYERED 窗口（每像素真 Alpha，零紫边、抗锯齿）。
    /// </summary>
    public static bool PushLayeredBitmap(IntPtr hwnd, Bitmap bmp, int left, int top, byte masterAlpha)
    {
        if (hwnd == IntPtr.Zero || bmp == null || bmp.Width <= 0 || bmp.Height <= 0) return false;
        IntPtr screenDc = IntPtr.Zero;
        IntPtr memDc = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        IntPtr oldBmp = IntPtr.Zero;
        try
        {
            screenDc = GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero) return false;
            memDc = CreateCompatibleDC(screenDc);
            if (memDc == IntPtr.Zero) return false;
            hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
            if (hBitmap == IntPtr.Zero) return false;
            oldBmp = SelectObject(memDc, hBitmap);

            var size = new SIZE_API { cx = bmp.Width, cy = bmp.Height };
            var srcPt = new POINT_API { x = 0, y = 0 };
            var dstPt = new POINT_API { x = left, y = top };
            var blend = new BLENDFUNCTION
            {
                BlendOp = AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = masterAlpha,
                AlphaFormat = AC_SRC_ALPHA
            };
            return UpdateLayeredWindow(hwnd, screenDc, ref dstPt, ref size, memDc, ref srcPt, 0, ref blend, ULW_ALPHA);
        }
        catch { return false; }
        finally
        {
            if (oldBmp != IntPtr.Zero && memDc != IntPtr.Zero) SelectObject(memDc, oldBmp);
            if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap);
            if (memDc != IntPtr.Zero) DeleteDC(memDc);
            if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
    public static extern void CopyMemory(IntPtr dest, IntPtr src, UIntPtr count);
}

[ComImport]
[Guid("5b0d3235-4dba-4d44-865e-8f1d0e4fd04d")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMemoryBufferByteAccess
{
    void GetBuffer(out IntPtr buffer, out uint capacity);
}
