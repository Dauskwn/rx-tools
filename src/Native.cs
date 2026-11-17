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
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern uint MapVirtualKeyW(uint code, uint mapType);

    // ===== 输入法切换（避免中文输入法吞键）=====
    [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint idThread);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadKeyboardLayoutW(string id, uint flags);

    const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;

    public static IntPtr SwitchToEnglish(IntPtr hwnd)
    {
        IntPtr old = IntPtr.Zero;
        try
        {
            uint pid;
            uint tid = GetWindowThreadProcessId(hwnd, out pid);
            old = GetKeyboardLayout(tid);
            IntPtr en = LoadKeyboardLayoutW("04090409", 0x00000001);
            if (en != IntPtr.Zero) PostMessage(hwnd, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, en);
        }
        catch { }
        return old;
    }

    public static void RestoreLayout(IntPtr hwnd, IntPtr layout)
    {
        if (hwnd == IntPtr.Zero || layout == IntPtr.Zero) return;
        try { PostMessage(hwnd, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, layout); }
        catch { }
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

    static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
    static readonly object logLock = new object();

    public static void MakeTopmost(IntPtr h)
    {
        SetWindowPos(h, HWND_TOPMOST, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }

    public static void CenterPhysical(IntPtr h)
    {
        RECT r;
        if (!GetWindowRect(h, out r)) return;
        int w = r.R - r.L, ht = r.B - r.T;
        int sw = GetSystemMetrics(0), sh = GetSystemMetrics(1);
        SetWindowPos(h, IntPtr.Zero, (sw - w) / 2, (sh - ht) / 2, 0, 0, 0x0001 | 0x0004 | 0x0010);
    }

    public static string LogPath = "";

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
                            EnumWindows(delegate(IntPtr h, IntPtr l)
                            {
                                uint wpid;
                                GetWindowThreadProcessId(h, out wpid);
                                if ((int)wpid == targetPid && IsWindowVisible(h))
                                {
                                    targetHwnd = h;
                                    return false;
                                }
                                return true;
                            }, IntPtr.Zero);
                        }
                        if (targetHwnd != IntPtr.Zero)
                        {
                            if (IsIconic(targetHwnd)) ShowWindow(targetHwnd, 9);
                            else ShowWindow(targetHwnd, 5);
                            SetForegroundWindow(targetHwnd);
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
}
