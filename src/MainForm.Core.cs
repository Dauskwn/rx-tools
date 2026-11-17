using System;
using System.Collections.Generic;
using System.Drawing;
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

public partial class MainForm : Form
{
    public const string APP_VERSION = "V1.8.0";

    Overlay overlay;
    Toast toast;
    System.Windows.Forms.Timer tick;
    Label lblStatus;
    ComboBox cmbSpeed;
    ComboBox cmbStyle;
    ComboBox cmbColor;
    NumericUpDown nThick, nLen, nGap, nDot;
    CrosshairPreview crossPreview;
    Button btnAuto;
    TabControl tabs;

    bool autoOn = false;
    bool typingOn = false;
    volatile bool isClosing = false;
    public static bool IsDark = false;
    int themeMode = 2;   // 0 = 跟随系统，1 = 夜间深色，2 = 护眼浅色（默认）
    Panel bottomBar;
    Label lblThemeSwitch;
    Panel pTimerPanel, pTimerDiv1, pTimerDiv2;
    CountdownOverlay timerOverlay;
    bool timerRunning = false, timerPaused = false;
    DateTime timerEndAt = DateTime.MinValue, timerStartedAt = DateTime.MinValue;
    double timerPausedRemainMs = 0;
    long timerPausedElapsedMs = 0;
    int timerTotalSec = 1080, timerRemainSec = 1080;
    int lastTimerDigits = 5;
    int timerFontSize = 28, timerColorIdx = 0, timerAnchorIdx = 1, timerOffX = 0, timerOffY = 24;
    List<TimerChainPreset> timerPresets = TimerChainPreset.CreateDefaultPresets();
    int activePresetIdx = 0;
    List<TimerChainNode> timerNodes = new List<TimerChainNode>();
    int timerActiveColorIdx = 0, timerActiveFlash = 0;
    string timerActiveLabel = "", lastTimerOverlayLabel = "";
    bool timerFlashDim = false;
    Button btnTimerStart, btnTimerPause, btnTimerVis, btnTimerAddPreset, btnTimerResetPos;
    Button btnChainAdd, btnChainEdit, btnChainDel, btnChainClear;
    Label lblTimerBig, lblTimerState, lblTimerHint, lblTimerChainTitle;
    NumericUpDown nTimerMin, nTimerSec, nTimerFont, nTimerOffX, nTimerOffY;
    ComboBox cmbTimerColor, cmbTimerAnchor;
    FlowLayoutPanel flpTimerPresets;
    BufferedListView lvTimerChain;
    bool suppressTimerCtrl = false;
    int lastAddedTriggerMode = 0;
    ToolTip timerPresetTip;
    DateTime lastTimerTopmost = DateTime.MinValue;
    Dictionary<Control, Color> origFore = new Dictionary<Control, Color>();
    Dictionary<Control, Color> origBack = new Dictionary<Control, Color>();
    Dictionary<Button, FlatStyle> origFlatStyle = new Dictionary<Button, FlatStyle>();
    Dictionary<Button, Color> origBorder = new Dictionary<Button, Color>();
    HashSet<Control> themeHooked = new HashSet<Control>();
    Dictionary<Control, TextBoxFrame> textFrames = new Dictionary<Control, TextBoxFrame>();
    int favVersion = 0;
    int ocrBusy = 0;
    int pasteBusy = 0;
    int ocrLoadingHits = 0;
    bool ocrHoldSend = false;
    string lastOcrScene = "";   // 已看到一次读条，先别再发 connect（等确认）
    OcrEngine cachedOcrEngine = null;
    bool ocrEngineInitTried = false;
    string savedClipboardText = null;

    bool suppressSettingsSave = false;
    System.ComponentModel.IContainer uiComponents = new System.ComponentModel.Container();
    List<IDisposable> ownedGdi = new List<IDisposable>();
    Dictionary<string, Font> fontPool = new Dictionary<string, Font>(StringComparer.Ordinal);

    int[] favCount = new int[0];
    string[] favName = new string[0];
    int[] favMax = new int[0];
    string favPath = "";
    // ===== 关于 / 自动更新 =====
    public const string UPDATE_URL_DEFAULT = "https://rx-tools.pages.dev/version.json";
    public const string APP_VERSION_NUM = "1.8.0";
    public const string APP_BUILD_DATE = "2026-11-17";
    string updateUrl = UPDATE_URL_DEFAULT;
    TabPage aboutPage, spacerPage, customPage;
    string dataRootDir = "";
    TabPage joinPage, crossPage, timerPage;
    Label lblPlayerBarTip;
    Panel changelogHost;
    Label lblUpdState;
    string updLatestNotes = "";
    Button btnUpdCheck, btnUpdOpen;
    CheckBox chkAutoUpdate;
    Thread updThread;
    volatile bool updBusy;
    string updLatestUrl = "", updLatestFallback = "", updLatestVersion = "";
    string updLatestSha = "";
    bool startupCheckDone;
    bool autoUpdateOn = true;

    static string Esc0(string s)
    {
        if (s == null) return "null";
        return s.Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
    }

    static string JsonEsc(string s)
    {
        if (s == null) return "";
        var sb = new StringBuilder(s.Length + 16);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"') sb.Append("\\\"");
            else if (c == '\\') sb.Append("\\\\");
            else if (c == '\n') sb.Append("\\n");
            else if (c == '\r') sb.Append("\\r");
            else if (c == '\t') sb.Append("\\t");
            else if (c < ' ') sb.Append(' ');
            else sb.Append(c);
        }
        return sb.ToString();
    }

    static string JsonUnesc(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\\' && i + 1 < s.Length)
            {
                char n = s[i + 1];
                if (n == 'n') { sb.Append('\n'); i++; continue; }
                if (n == 'r') { sb.Append('\r'); i++; continue; }
                if (n == 't') { sb.Append('\t'); i++; continue; }
                if (n == '"') { sb.Append('"'); i++; continue; }
                if (n == '\\') { sb.Append('\\'); i++; continue; }
                if (n == 'u' && i + 5 < s.Length)
                {
                    int code;
                    if (int.TryParse(s.Substring(i + 2, 4), System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out code))
                    {
                        sb.Append((char)code);
                        i += 5;
                        continue;
                    }
                }
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    string PromptText(string title, string label, string def)
    {
        return PromptText(title, label, def, false);
    }

    string PromptText(string title, string label, string def, bool multiline)
    {
        using (var f = new Form())
        {
            f.Text = title;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.StartPosition = FormStartPosition.CenterParent;
            f.MaximizeBox = false;
            f.MinimizeBox = false;
            f.ClientSize = multiline ? new Size(480, 220) : new Size(400, 130);
            var lb = new Label();
            lb.Text = label;
            lb.SetBounds(12, 12, f.ClientSize.Width - 24, 22);
            f.Controls.Add(lb);
            var tb = new TextBox();
            tb.Text = def ?? "";
            if (multiline)
            {
                tb.Multiline = true;
                tb.AcceptsReturn = true;
                tb.WordWrap = true;
                tb.ScrollBars = ScrollBars.Vertical;
                tb.SetBounds(12, 38, f.ClientSize.Width - 24, 122);
            }
            else
            {
                tb.SetBounds(12, 38, f.ClientSize.Width - 24, 24);
            }
            f.Controls.Add(tb);
            int btnY = multiline ? 174 : 80;
            var ok = new Button();
            ok.Text = "确定";
            ok.DialogResult = DialogResult.OK;
            ok.SetBounds(f.ClientSize.Width - 188, btnY, 80, 30);
            f.Controls.Add(ok);
            var cancel = new Button();
            cancel.Text = "取消";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.SetBounds(f.ClientSize.Width - 100, btnY, 80, 30);
            f.Controls.Add(cancel);
            if (!multiline) f.AcceptButton = ok;
            f.CancelButton = cancel;
            ThemeDialog(f);
            return f.ShowDialog(this) == DialogResult.OK ? tb.Text.Trim() : null;
        }
    }

    string settingsPath = "";
    string[] favList = new string[0];
    CheckBox chkOcr;

    // ===== V1.4 / V1.5 挤服页控件与状态 =====
    Panel pJoinPanel;
    RadioButton rbTapMode, rbOneIp, rbPollMode;
    BufferedListView lvSrv;
    Button btnRefreshFav, btnCheckAll, btnUncheckAll, btnAddFav, btnPasteFav, btnDelFav, btnEditFav;
    ContextMenuStrip cmsSrv;
    Label lblListHint, lblJoinHint, lblNewIp, lblNewAlias, lblTapGuide, lblCrossHint;
    TextBox txtNewIp, txtNewAlias;
    Button btnGuide;
    bool suppressListCheck = false;
    // 收藏轮询的勾选偏好（addr 集合）；chkPrefLoaded 区分"没存过"和"用户就是要全不选"
    System.Collections.Generic.HashSet<string> chkPref = new System.Collections.Generic.HashSet<string>();
    bool chkPrefLoaded = false;
    bool chkPrefAuthoritative = false;   // 刚读完 ini，这一次重建以文件偏好为准
    bool suppressModeEvent = false;
    string pendingSelAddr = "";
    List<string> targets = new List<string>();
    string currentTargetAddr = "";
    IntPtr savedLayout = IntPtr.Zero;
    int multiIdx = 0;
    DateTime tapOcrAt = DateTime.MinValue;
    int tapOcrFail = 0;
    int srvSortCol = -1;
    bool srvSortAsc = true;

    // ===== 可自定义热键：索引 = id-1 =====
    bool[] prevHkDown = new bool[8];
    bool[] hkRegistered = new bool[12];   // 1..8 = 功能热键，9 = 热键总开关
    bool hotkeysOff;                     // 用户临时关闭热键（F5 等交还系统）
    DateTime lastHkRetry = DateTime.MinValue;
    Button btnHotkeyOff;
    static readonly int[] HKDefVk = { 0x74, 0x75, 0x76, 0x77, 0x7A, 0x78, 0x79, 0x78 };
    static readonly int[] HKDefMod = { 0, 0, 0, 0, 0x0002, 0, 0, 0x0002 };
    static readonly string[] HKFunc = {
        "准星 换样式", "挤服 开始/停止", "准星 换颜色", "准星 显示/隐藏", "退出工具",
        "倒计时 开始/重置", "倒计时 显示/隐藏", "倒计时 暂停/继续"
    };
    static readonly string[] HKTip = {
        "在 6 种准星样式之间循环",
        "开始 / 停止挤服（按当前模式）",
        "在 6 种颜色之间循环",
        "把准星显示出来或收起来",
        "直接关掉工具箱",
        "按设定时长从头开始倒计时",
        "显示或隐藏屏幕倒计时悬浮窗",
        "暂停或继续当前倒计时"
    };
    int[] hkVk = new int[8];
    int[] hkMod = new int[8];
    bool capturingKey = false;
    Button btnCross, btnColor, btnHotkey, bLog, btnCrossImg, btnCrossImgClear, btnCrossImgPaste;
    Label lblCrossImg;
    string crossImgToLoad = "";

    DateTime lastEnter = DateTime.MinValue;
    int sent = 0;
    DateTime lastUi = DateTime.MinValue;

    static readonly string[] GameProcs = { "gmod", "gmod_win64" };
    static readonly object[] TapIntervals = { "100 ms", "200 ms", "300 ms", "500 ms", "900 ms" };

    Font TrackFont(string family, float size, FontStyle style)
    {
        string key = family + "|" + size.ToString("F2") + "|" + ((int)style).ToString();
        Font f;
        if (fontPool.TryGetValue(key, out f) && f != null)
            return f;
        f = new Font(family, size, style);
        fontPool[key] = f;
        ownedGdi.Add(f);
        return f;
    }

    void SafeBeginInvoke(MethodInvoker action)
    {
        if (isClosing || IsDisposed || !IsHandleCreated) return;
        try
        {
            BeginInvoke(new MethodInvoker(delegate
            {
                if (isClosing || IsDisposed) return;
                try { action(); } catch (Exception ex) { Native.Log("UI invoke EX: " + ex.Message); }
            }));
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
        catch { }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0312)   // WM_HOTKEY
        {
            int id = m.WParam.ToInt32();
            Native.Log("WM_HOTKEY id=" + id + " capturing=" + capturingKey + " off=" + hotkeysOff
                + " fg=" + ForegroundTitle());
            if (capturingKey) return;
            if (id == HK_TOGGLE_ID) { ToggleHotkeys(); return; }
            if (hotkeysOff) return;          // 已关闭：忽略其余热键（其实已注销，这里兜底）
            if (id >= 1 && id <= 8) { HandleHotkey(id); return; }
        }
        if (m.Msg == 0x001A && themeMode == 0)   // WM_SETTINGCHANGE（系统深浅色切换时联动）
        {
            base.WndProc(ref m);
            try
            {
                if (!isClosing && !IsDisposed && IsHandleCreated && IsDark != Native.IsSystemDarkMode())
                    SafeBeginInvoke(delegate { ApplyTheme(); });
            }
            catch { }
            return;
        }
        if (m.Msg == 0x007E)   // WM_DISPLAYCHANGE（切换分辨率后强制重排并重绘，防止双缓冲表面残留）
        {
            base.WndProc(ref m);
            try
            {
                if (!isClosing && !IsDisposed && IsHandleCreated)
                {
                    PerformLayout();
                    if (tabs != null && tabs.SelectedTab != null)
                        tabs.SelectedTab.PerformLayout();
                    Invalidate(true);
                }
            }
            catch { }
            return;
        }
        base.WndProc(ref m);
    }

    public MainForm(string icoPath, string[] tabTitles, string[][] tabItems, string dataDir)
    {
        AutoScaleMode = AutoScaleMode.None;
        Text = "RX 工具箱 " + APP_VERSION;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(960, 720);
        MinimumSize = new Size(820, 560);
        Font = new Font("Microsoft YaHei UI", 9f);
        BackColor = Color.FromArgb(248, 250, 252);

        try
        {
            if (!string.IsNullOrEmpty(icoPath) && File.Exists(icoPath))
                Icon = new Icon(icoPath);
            else
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch { }

        for (int i = 0; i < 8; i++)
        {
            hkVk[i] = HKDefVk[i];
            hkMod[i] = HKDefMod[i];
        }
        dataRootDir = dataDir;
        customPath = Path.Combine(dataDir, "custom.txt");
        favPath = Path.Combine(dataDir, "servers.txt");
        settingsPath = Path.Combine(dataDir, "settings.ini");
        Native.Log("==================== " + APP_VERSION + " 启动 ====================");
        Native.Log("exe: " + Application.ExecutablePath);
        Native.Log("os: " + Environment.OSVersion.VersionString + " 64bit=" + Environment.Is64BitProcess
            + " 日志: " + Native.LogPath);
        LoadCustom();

        overlay = new Overlay();
        timerOverlay = new CountdownOverlay();
        toast = new Toast();

        bottomBar = new Panel();
        bottomBar.Dock = DockStyle.Bottom;
        bottomBar.Height = 32;
        bottomBar.Padding = new Padding(0, 1, 0, 0);
        bottomBar.BackColor = Color.FromArgb(241, 245, 249);
        bottomBar.Paint += delegate(object snd, PaintEventArgs pe)
        {
            using (var pen = new Pen(IsDark ? Color.FromArgb(48, 54, 61) : Color.FromArgb(226, 232, 240)))
                pe.Graphics.DrawLine(pen, 0, 0, bottomBar.Width, 0);
        };

        lblThemeSwitch = new Label();
        lblThemeSwitch.Dock = DockStyle.Right;
        lblThemeSwitch.Width = 160;
        lblThemeSwitch.Padding = new Padding(0, 0, 10, 0);
        lblThemeSwitch.TextAlign = ContentAlignment.MiddleRight;
        lblThemeSwitch.UseMnemonic = false;
        lblThemeSwitch.Cursor = Cursors.Hand;
        lblThemeSwitch.BackColor = Color.FromArgb(241, 245, 249);
        lblThemeSwitch.ForeColor = Color.FromArgb(37, 99, 235);
        lblThemeSwitch.Font = TrackFont("Microsoft YaHei UI", 8.5f, FontStyle.Regular);
        lblThemeSwitch.Click += delegate
        {
            themeMode = (themeMode + 1) % 3;
            ApplyTheme();
            SaveSettings();
        };
        var tipTheme = new ToolTip(uiComponents);
        tipTheme.InitialDelay = 300;
        tipTheme.SetToolTip(lblThemeSwitch, "单击切换主题：护眼浅色 / 跟随系统 / 夜间深色");

        lblStatus = new Label();
        lblStatus.Dock = DockStyle.Fill;
        lblStatus.Padding = new Padding(10, 0, 0, 0);
        lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        lblStatus.AutoEllipsis = true;
        lblStatus.UseMnemonic = false;
        lblStatus.BackColor = Color.FromArgb(241, 245, 249);
        lblStatus.ForeColor = Color.FromArgb(51, 65, 85);

        bottomBar.Controls.Add(lblStatus);
        bottomBar.Controls.Add(lblThemeSwitch);
        lblStatus.BringToFront();

        tabs = new ThemedTabControl();
        tabs.Dock = DockStyle.Fill;
        Controls.Add(tabs);
        Controls.Add(bottomBar);

        BuildPlayerToolPages();
        customPage = BuildCustomPage();
        tabs.TabPages.Add(customPage);
        aboutPage = BuildAboutPage();
        spacerPage = new TabPage("");
        tabs.TabPages.Add(spacerPage);          // 只占宽度，把「关于」推到最右侧
        tabs.TabPages.Add(aboutPage);
        tabs.Selecting += delegate(object s2, TabControlCancelEventArgs e2)
        {
            if (e2.TabPage == spacerPage) e2.Cancel = true;   // 空白页不可选中
        };
        tabs.Resize += delegate { LayoutAboutTab(); };
        // TabPage 在首次显示前拿到的尺寸不准，切到工具页时重排一次
        tabs.SelectedIndexChanged += delegate
        {
            UpdatePlayerToolBar();
            LayoutToolBar();
            if (pJoinPanel != null && pJoinPanel.Visible)
            {
                try { LayoutJoin(); } catch { }
            }
            if (pTimerPanel != null && pTimerPanel.Visible)
            {
                try { LayoutTimer(); } catch { }
            }
        };

        LoadSettings();
        ApplyTheme();
        // 设置里带着勾选偏好，读完重建列表（否则界面还是构造时默认的全选）
        FillSrvList();
        ApplyModeUi();
        UpdateHotkeyLabels();
        if (!string.IsNullOrEmpty(crossImgToLoad)) LoadCrosshairImage(crossImgToLoad, true);
        // 构造期有控件抢焦点会让 TabControl 自己切换页签，这里强制回到首页
        try
        {
            if (tabs != null && tabs.TabPages.Count > 0
                && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RX_START_TAB")))
                tabs.SelectedIndex = 0;
        }
        catch { }

        string startTab = Environment.GetEnvironmentVariable("RX_START_TAB");
        if (!string.IsNullOrEmpty(startTab))
        {
            int si;
            if (int.TryParse(startTab, out si) && si >= 0 && si < tabs.TabPages.Count)
                tabs.SelectedIndex = si;
        }

        tick = new System.Windows.Forms.Timer();
        tick.Interval = 20;
        tick.Tick += OnTick;
        tick.Start();

        RefreshUi(true);
        Native.Log("main form ready");
    }

    static bool TrySetClipboardText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(text, false, 2, 10);
                return true;
            }
            catch
            {
                if (attempt < 2) Thread.Sleep(5);
            }
        }
        return false;
    }

    void CopyToClipboard(string title, string body)
    {
        try
        {
            if (string.IsNullOrEmpty(body)) return;
            if (TrySetClipboardText(body))
            {
                savedClipboardText = body;
                toast.ShowText("已复制：" + title);
                Native.Log("copy: " + title + " | " + body);
            }
            else
            {
                toast.ShowText("复制失败：剪贴板正被其它软件占用");
                Native.Log("copy busy: " + title);
            }
        }
        catch (Exception ex)
        {
            toast.ShowText("复制失败：" + ex.Message);
            Native.Log("copy EX: " + ex.Message);
        }
    }



    void ClearAndDisposeControls(Control parent)
    {
        if (parent == null) return;
        while (parent.Controls.Count > 0)
        {
            Control c = parent.Controls[0];
            parent.Controls.RemoveAt(0);
            ForgetOrigColors(c);
            c.Dispose();
        }
    }



    // ===================== 工具页（挤服助手 + 屏幕准星） =====================


    // ===================== 主题系统（跟随系统 / 夜间深色 / 护眼浅色） =====================
    void UpdateThemeSwitchLabel()
    {
        if (lblThemeSwitch == null) return;
        if (themeMode == 0) lblThemeSwitch.Text = IsDark ? "🌓 主题：跟随系统(暗)" : "🌓 主题：跟随系统(亮)";
        else if (themeMode == 1) lblThemeSwitch.Text = "🌙 主题：夜间深色";
        else lblThemeSwitch.Text = "☀ 主题：护眼浅色";
    }


    void HookDarkScrollBar(Control c)
    {
        if (c == null) return;
        if (c.IsHandleCreated) Native.ApplyDarkScrollBar(c.Handle, IsDark);
        if (!themeHooked.Add(c)) return;
        c.HandleCreated += delegate { try { if (!c.IsDisposed) Native.ApplyDarkScrollBar(c.Handle, IsDark); } catch { } };
        c.Disposed += delegate { themeHooked.Remove(c); };
    }

    void RecordOrigColors(Control c)
    {
        if (c == null || (origFore.ContainsKey(c) && origBack.ContainsKey(c))) return;
        Color of = c.ForeColor;
        Color ob = c.BackColor;
        if (!(c is CrosshairPreview))
        {
            if (ob != Color.Transparent && ob.A > 0 && ob.R < 100 && ob.G < 100 && ob.B < 115)
            {
                if (c is TabPage || c is TextBox || c is ListBox || c is ListView || c is NumericUpDown || c is ComboBox)
                    ob = Color.White;
                else if (c is Label || c is CheckBox || c is RadioButton)
                    ob = Color.Transparent;
                else
                    ob = Color.FromArgb(248, 250, 252);
            }
            if (of.R > 185 && of.G > 185 && of.B > 185)
                of = Color.FromArgb(30, 41, 59);
        }
        origFore[c] = of;
        origBack[c] = ob;
        var b = c as Button;
        if (b != null)
        {
            origFlatStyle[b] = b.FlatStyle;
            origBorder[b] = b.FlatAppearance.BorderColor;
        }
    }

    void SetDynamicLabelColor(Control c, Color fc)
    {
        if (c == null) return;
        if (origBack.ContainsKey(c))
        {
            origFore[c] = fc;
            ThemeControlTree(c);
        }
        else
        {
            c.ForeColor = fc;
        }
    }

    void CaptureOrigColorsTree(Control root)
    {
        if (root == null) return;
        for (int i = 0; i < root.Controls.Count; i++)
            CaptureOrigColorsTree(root.Controls[i]);
        RecordOrigColors(root);
    }

    void ThemeControlTree(Control root)
    {
        if (root == null) return;
        CaptureOrigColorsTree(root);
        ApplyThemeToNode(root);
    }

    void ApplyThemeToNode(Control root)
    {
        if (root == null) return;

        Color darkPageBg = Color.FromArgb(22, 27, 34);
        Color darkPanelBg = Color.FromArgb(28, 33, 40);
        Color darkInputBg = Color.FromArgb(13, 17, 23);
        Color darkBtnBg = Color.FromArgb(33, 38, 45);
        Color darkBtnBorder = Color.FromArgb(54, 59, 66);
        Color darkText = Color.FromArgb(230, 237, 243);
        Color darkMuted = Color.FromArgb(139, 148, 158);
        Color darkBlue = Color.FromArgb(88, 166, 255);
        Color darkDivider = Color.FromArgb(48, 54, 61);

        if (root is CrosshairPreview)
        {
            root.Invalidate();
            return;
        }
        if (root is WrapList)
        {
            root.Invalidate();
            return;
        }

        Color ob, of;
        if (!origBack.TryGetValue(root, out ob)) { ob = root.BackColor; origBack[root] = ob; }
        if (!origFore.TryGetValue(root, out of)) { of = root.ForeColor; origFore[root] = of; }

        if (root is TabPage)
        {
            root.BackColor = IsDark ? darkPageBg : Color.FromArgb(250, 251, 253);
            root.ForeColor = IsDark ? darkText : of;
        }
        else if (root is Panel && Convert.ToString(root.Tag) == "rx-warn-bar")
        {
            root.BackColor = IsDark ? Color.FromArgb(240, 173, 78) : Color.FromArgb(217, 119, 6);
        }
        else if (root is Panel && Convert.ToString(root.Tag) == "rx-warn")
        {
            root.BackColor = IsDark ? Color.FromArgb(46, 33, 18) : Color.FromArgb(255, 246, 229);
            root.ForeColor = IsDark ? Color.FromArgb(227, 179, 65) : Color.FromArgb(166, 56, 12);
        }
        else if (root is Panel && !(root is FlowLayoutPanel) && root.Height <= 2 && root.Controls.Count == 0)
        {
            root.BackColor = IsDark ? darkDivider : ob;
        }
        else if (root is Panel || root is GroupBox)
        {
            bool elevated = (ob.R >= 236 && ob.G >= 238 && ob.B >= 242 && ob.R < 253);
            root.BackColor = IsDark ? (elevated ? darkPanelBg : darkPageBg)
                                    : (ob == Color.White ? Color.FromArgb(250, 251, 253) : ob);
            root.ForeColor = IsDark ? darkText : of;
            var sc = root as ScrollableControl;
            if (sc != null && sc.AutoScroll) HookDarkScrollBar(root);
        }
        else if (root is TextBox || root is ListBox || root is ListView || root is NumericUpDown || root is ComboBox)
        {
            root.BackColor = IsDark ? darkInputBg : (ob == Color.White ? Color.White : ob);
            if (Convert.ToString(root.Tag) == "rx-preview")
            {
                var tbPrev = root as TextBox;
                bool emptyHint = tbPrev != null && tbPrev.Text.StartsWith("💡", StringComparison.Ordinal);
                root.ForeColor = IsDark ? (emptyHint ? darkMuted : darkText)
                                        : (emptyHint ? Color.FromArgb(140, 150, 165) : Color.FromArgb(20, 28, 40));
            }
            else
            {
                root.ForeColor = IsDark ? darkText : of;
            }
            if (root is TextBox || root is ListBox || root is ListView || root is ComboBox)
                HookDarkScrollBar(root);
            if (root is TextBox)
            {
                TextBoxFrame tf;
                if (!textFrames.TryGetValue(root, out tf))
                {
                    tf = new TextBoxFrame((TextBox)root);
                    textFrames[root] = tf;
                }
                tf.RedrawFrame();
            }
            var blv = root as BufferedListView;
            if (blv != null) blv.RefreshTheme();
        }
        else if (root is Button)
        {
            var b = (Button)root;
            if (b != btnAuto && b != btnHotkeyOff && b != btnTimerStart && b != btnTimerVis && (flpTimerPresets == null || b.Parent != flpTimerPresets))
            {
                string tagStr = Convert.ToString(b.Tag);
                if (IsDark)
                {
                    b.FlatStyle = FlatStyle.Flat;
                    b.FlatAppearance.MouseDownBackColor = Color.FromArgb(52, 62, 76);
                    if (tagStr == "rx-card")
                    {
                        b.BackColor = Color.FromArgb(28, 35, 46);
                        b.ForeColor = darkText;
                        b.FlatAppearance.BorderColor = Color.FromArgb(54, 65, 82);
                        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(36, 45, 58);
                    }
                    else if (of.R > of.B + 40 && of.R > 120)
                    {
                        b.BackColor = Color.FromArgb(54, 26, 31);
                        b.ForeColor = Color.FromArgb(255, 161, 152);
                        b.FlatAppearance.BorderColor = Color.FromArgb(140, 52, 60);
                        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(72, 32, 38);
                        b.FlatAppearance.MouseDownBackColor = Color.FromArgb(86, 38, 44);
                    }
                    else if (ob.B > ob.R + 12)
                    {
                        b.BackColor = Color.FromArgb(27, 47, 74);
                        b.ForeColor = Color.FromArgb(121, 192, 255);
                        b.FlatAppearance.BorderColor = Color.FromArgb(56, 139, 253);
                        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(35, 60, 94);
                        b.FlatAppearance.MouseDownBackColor = Color.FromArgb(42, 72, 112);
                    }
                    else
                    {
                        b.BackColor = darkBtnBg;
                        b.ForeColor = darkText;
                        b.FlatAppearance.BorderColor = darkBtnBorder;
                        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(44, 52, 63);
                    }
                }
                else
                {
                    b.FlatStyle = FlatStyle.Flat;
                    b.FlatAppearance.MouseOverBackColor = Color.Empty;
                    b.FlatAppearance.MouseDownBackColor = Color.Empty;
                    if (tagStr == "rx-card")
                    {
                        b.BackColor = ob;
                        b.ForeColor = Color.FromArgb(24, 34, 48);
                        b.FlatAppearance.BorderColor = Color.FromArgb(160, 190, 225);
                        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(224, 236, 252);
                    }
                    else if (of.R > of.B + 40 && of.R > 120)
                    {
                        b.BackColor = Color.FromArgb(254, 242, 242);
                        b.ForeColor = of;
                        b.FlatAppearance.BorderColor = Color.FromArgb(232, 180, 180);
                        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(254, 226, 226);
                    }
                    else if (ob.B > ob.R + 12)
                    {
                        b.BackColor = ob;
                        b.ForeColor = Color.FromArgb(24, 64, 128);
                        Color bc;
                        b.FlatAppearance.BorderColor = (origBorder.TryGetValue(b, out bc) && bc != Color.Empty && bc != Color.Black)
                            ? bc : Color.FromArgb(165, 198, 238);
                    }
                    else
                    {
                        b.BackColor = (ob.R >= 242 && ob.G >= 242 && ob.B >= 242) ? Color.FromArgb(248, 250, 252) : ob;
                        b.ForeColor = Color.FromArgb(30, 41, 59);
                        Color bc;
                        b.FlatAppearance.BorderColor = (origBorder.TryGetValue(b, out bc) && bc != Color.Empty && bc != Color.Black && bc != Color.Silver)
                            ? bc : Color.FromArgb(203, 213, 225);
                        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(238, 242, 248);
                    }
                }
            }
        }
        else if (root is Label || root is CheckBox || root is RadioButton)
        {
            var lnk = root as LinkLabel;
            if (IsDark)
            {
                if (root == lblTapGuide)
                {
                    root.BackColor = Color.FromArgb(38, 32, 22);
                    root.ForeColor = Color.FromArgb(230, 198, 135);
                }
                else
                {
                    if (root == lblStatus || root == lblThemeSwitch)
                        root.BackColor = darkPanelBg;
                    else
                        root.BackColor = Color.Transparent;

                    if (lnk != null)
                    {
                        lnk.LinkColor = darkBlue;
                        lnk.VisitedLinkColor = darkBlue;
                        lnk.ActiveLinkColor = Color.FromArgb(121, 192, 255);
                        lnk.ForeColor = darkText;
                    }
                    else if (root == lblThemeSwitch)
                    {
                        root.ForeColor = Color.FromArgb(121, 192, 255);
                    }
                    else if (of.R > 140 && of.B < 50)
                    {
                        root.ForeColor = Color.FromArgb(227, 179, 65);
                    }
                    else if (of.G > of.R + 40)
                    {
                        root.ForeColor = Color.FromArgb(63, 185, 80);
                    }
                    else if (of.B > of.R + 28)
                    {
                        root.ForeColor = darkBlue;
                    }
                    else if (of == Color.Gray || of == Color.DimGray || (of.R >= 85 && of.G >= 85 && of.B >= 85 && Math.Abs(of.R - of.B) < 26))
                    {
                        root.ForeColor = darkMuted;
                    }
                    else
                    {
                        root.ForeColor = darkText;
                    }
                }
            }
            else
            {
                if (root == lblTapGuide || root == lblStatus || root == lblThemeSwitch)
                    root.BackColor = ob;
                else if (ob == Color.White || ob == Color.FromArgb(250, 251, 253) || ob == Color.FromArgb(247, 249, 252)
                    || ob == Color.FromArgb(242, 246, 252) || ob == Color.Transparent)
                    root.BackColor = Color.Transparent;
                else
                    root.BackColor = ob;
                root.ForeColor = of;
                if (lnk != null)
                {
                    lnk.LinkColor = Color.FromArgb(37, 99, 235);
                    lnk.VisitedLinkColor = lnk.LinkColor;
                    lnk.ActiveLinkColor = Color.FromArgb(20, 70, 150);
                }
            }
        }

        for (int i = 0; i < root.Controls.Count; i++)
            ApplyThemeToNode(root.Controls[i]);
    }

    void ApplyTheme()
    {
        IsDark = (themeMode == 1) || (themeMode == 0 && Native.IsSystemDarkMode());
        if (IsHandleCreated) Native.SetDarkTitleBar(Handle, IsDark);

        BackColor = IsDark ? Color.FromArgb(13, 17, 23) : Color.FromArgb(241, 245, 249);
        if (bottomBar != null) bottomBar.BackColor = IsDark ? Color.FromArgb(28, 33, 40) : Color.FromArgb(241, 245, 249);

        ThemeControlTree(this);
        UpdatePlayerToolBar();

        UpdateStartButton();
        UpdateHotkeyToggleButton();
        UpdateTimerButtonsStyle();
        UpdateTimerUi();
        UpdateThemeSwitchLabel();

        if (lvSrv != null)
        {
            for (int i = 0; i < favList.Length && i < lvSrv.Items.Count; i++)
                UpdateSrvRow(i);
            lvSrv.RefreshTheme();
        }
        if (lvTimerChain != null)
        {
            UpdateTimerChainRowsState();
            lvTimerChain.RefreshTheme();
        }
        RenderChangelog();
        RefreshUi(true);
        Invalidate(true);
    }

    void ForgetOrigColors(Control c)
    {
        if (c == null) return;
        origFore.Remove(c);
        origBack.Remove(c);
        themeHooked.Remove(c);
        textFrames.Remove(c);
        var b = c as Button;
        if (b != null)
        {
            origFlatStyle.Remove(b);
            origBorder.Remove(b);
        }
        for (int i = 0; i < c.Controls.Count; i++)
            ForgetOrigColors(c.Controls[i]);
    }

    void ThemeDialog(Form dlg)
    {
        if (dlg == null) return;
        CaptureOrigColorsTree(dlg);
        if (IsDark)
        {
            dlg.BackColor = Color.FromArgb(22, 27, 34);
            dlg.ForeColor = Color.FromArgb(230, 237, 243);
            dlg.HandleCreated += delegate { Native.SetDarkTitleBar(dlg.Handle, true); };
            if (dlg.IsHandleCreated) Native.SetDarkTitleBar(dlg.Handle, true);
        }
        ThemeControlTree(dlg);
        dlg.Disposed += delegate { ForgetOrigColors(dlg); };
    }

    // 开发用：RX_DUMP_UI=1 把每页控件的文本/坐标/可见性写到 dump-ui.log，
    // 布局改动可以先看数字，不必截图。

    void ImportCrosshairImage()
    {
        using (var dlg = new OpenFileDialog())
        {
            dlg.Title = "选择准星图片（建议 PNG 透明底，黑底 JPG 也能扣）";
            dlg.Filter = "图片 (*.png;*.bmp;*.jpg;*.jpeg;*.gif)|*.png;*.bmp;*.jpg;*.jpeg;*.gif|所有文件 (*.*)|*.*";
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            LoadCrosshairImage(dlg.FileName, false);
        }
    }

    void LoadCrosshairImage(string path, bool silent)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            if (!silent) toast.ShowText("找不到这张图片：" + path);
            return;
        }
        Bitmap bmp = Overlay.PrepareCrosshairImage(path);
        if (bmp == null)
        {
            if (!silent) toast.ShowText("这张图片读不出来（换 PNG/BMP 试试）");
            return;
        }
        if (overlay.CustomImage != null) { try { overlay.CustomImage.Dispose(); } catch { } }
        overlay.CustomImage = bmp;
        overlay.CustomImagePath = path;
        overlay.Invalidate();
        if (crossPreview != null) crossPreview.Invalidate();
        if (lblCrossImg != null)
            lblCrossImg.Text = "自定义准星：" + System.IO.Path.GetFileName(path)
                + "（原图 " + bmp.Width + "×" + bmp.Height + "，可通过“臂长+间隙”调整大小）";
        SaveSettings();
        RefreshUi(true);
        Native.Log("crosshair image: " + path);
        if (!silent) toast.ShowText("已导入准星：" + System.IO.Path.GetFileName(path));
    }

    void PasteCrosshairImage()
    {
        try
        {
            if (!Clipboard.ContainsImage())
            {
                toast.ShowText("剪贴板里没有图片");
                return;
            }
            Image img = Clipboard.GetImage();
            if (img == null)
            {
                toast.ShowText("剪贴板里的图片读不出来");
                return;
            }
            string dir = System.IO.Path.GetDirectoryName(Application.ExecutablePath);
            string save = System.IO.Path.Combine(dir, "crosshair.png");
            string tmp = save + ".tmp";
            try
            {
                img.Save(tmp, System.Drawing.Imaging.ImageFormat.Png);   // 原图带 alpha 存盘
            }
            finally { img.Dispose(); }
            if (File.Exists(save)) File.Delete(save);
            File.Move(tmp, save);
            Native.Log("crosshair paste -> " + save);
            LoadCrosshairImage(save, false);
        }
        catch (Exception ex)
        {
            Native.Log("crosshair paste EX: " + ex.Message);
            toast.ShowText("粘贴失败：" + ex.Message);
        }
    }

    void ClearCrosshairImage()
    {
        if (overlay.CustomImage == null && string.IsNullOrEmpty(overlay.CustomImagePath))
        {
            toast.ShowText("当前没有导入图片");
            return;
        }
        if (overlay.CustomImage != null) { try { overlay.CustomImage.Dispose(); } catch { } }
        overlay.CustomImage = null;
        overlay.CustomImagePath = "";
        overlay.Invalidate();
        if (crossPreview != null) crossPreview.Invalidate();
        if (lblCrossImg != null) lblCrossImg.Text = "当前用内置矢量样式（可导入 PNG 做自定义准星）";
        SaveSettings();
        RefreshUi(true);
        toast.ShowText("已清除自定义准星图片");
    }

    void ToggleCross()
    {
        try
        {
            if (overlay.Visible)
            {
                overlay.Visible = false;
                toast.ShowText("准星：关闭");
                Native.Log("cross hide");
            }
            else
            {
                overlay.Visible = true;
                Native.MakeTopmost(overlay.Handle);
                Native.CenterPhysical(overlay.Handle);
                toast.ShowText("准星：开启");
                Native.Log("cross show");
            }
        }
        catch (Exception ex) { Native.Log("cross EX: " + ex.Message); }
        RefreshUi(true);
    }

    void RefreshUi(bool force)
    {
        if (!force && (DateTime.Now - lastUi).TotalMilliseconds < 400) return;
        lastUi = DateTime.Now;
        int m = JoinModeIdx;
        string modeName = (m == 0) ? "手动档" : ((m == 2) ? "收藏轮询" : "单一 IP");

        string tg;
        if (autoOn && typingOn && targets != null && targets.Count > 0)
        {
            tg = (targets.Count == 1)
                ? targets[0]
                : ("正在挤 " + (string.IsNullOrEmpty(currentTargetAddr) ? targets[0] : currentTargetAddr) + " (共" + targets.Count + "服)");
        }
        else if (m == 0)
        {
            tg = "连按回车";
        }
        else if (m == 2)
        {
            int nchk = 0;
            if (lvSrv != null) for (int i = 0; i < lvSrv.Items.Count; i++) if (lvSrv.Items[i].Checked) nchk++;
            tg = nchk > 0 ? ("已勾 " + nchk + " 个服") : "未勾选";
        }
        else
        {
            string selAddr = SelectedAddr();
            tg = selAddr.Length > 0 ? selAddr : "未选服";
        }

        string run;
        if (!autoOn) run = "已停止";
        else if (typingOn) run = "粘贴中";
        else run = "回车中";

        // 挤服进行中：顶部常驻提醒（含当前热键），停止后自动收起
        if (autoOn || typingOn)
        {
            string runningMode = typingOn
                ? ("粘贴 connect · " + tg)
                : "连按回车（游戏里点连接）";
            toast.ShowPersistent("正在挤服 · 按 " + KeyName(2) + " 停止　—　" + runningMode);
        }
        else
        {
            toast.HideIfPersistent();
        }

        string ocr = (chkOcr == null || chkOcr.Checked) ? "OCR开" : "OCR关";
        lblStatus.Text = string.Format("准星：{0} ({1})　|　挤服：{2} [{3} · {4} · {5}ms]　|　{6}　|　热键{7}　|　已发送 {8} 次",
            overlay.Visible ? "开启" : "关闭",
            Overlay.Styles[overlay.StyleIndex],
            run, modeName, tg, IntervalMs, ocr,
            hotkeysOff ? "关(" + ToggleKeyName() + "开)"
                : (HotkeysOk() ? "开" : "冲突"), sent);
    }

    void OnTick(object sender, EventArgs e)
    {
        // 窗口/面板尺寸变了就重排（构造期布局用的是临时尺寸，常常对不上）
        if (pJoinPanel != null && !isClosing
            && (pJoinPanel.ClientSize.Width != laidW || pJoinPanel.ClientSize.Height != laidH))
            LayoutJoin();

        if (pendingSelAddr.Length > 0 && lvSrv != null && lvSrv.Items.Count > 0)
        {
            string want = pendingSelAddr;
            pendingSelAddr = "";
            int wantIx = -1;
            for (int i = 0; i < lvSrv.Items.Count; i++)
                if (string.Equals(Convert.ToString(lvSrv.Items[i].Tag), want, StringComparison.OrdinalIgnoreCase)) { wantIx = i; break; }
            if (wantIx >= 0 && (lvSrv.SelectedIndices.Count != 1 || lvSrv.SelectedIndices[0] != wantIx))
            {
                lvSrv.SelectedIndices.Clear();
                lvSrv.Items[wantIx].Selected = true;
                lvSrv.Items[wantIx].Focused = true;
                lvSrv.EnsureVisible(wantIx);
                UpdateJoinHint();
            }
        }

        if (pJoinPanel != null && !isClosing
            && (pJoinPanel.ClientSize.Width != laidW || pJoinPanel.ClientSize.Height != laidH))
            LayoutJoin();

        // 注册失败时每 5 秒重试一次（可能被别的程序临时占用）
        if (!hotkeysOff && !capturingKey && (DateTime.Now - lastHkRetry).TotalSeconds > 5)
        {
            bool missing = false;
            for (int i = 1; i <= 8; i++) if (!hkRegistered[i]) { missing = true; break; }
            if (missing)
            {
                lastHkRetry = DateTime.Now;
                Native.Log("hotkeys retry (some missing)");
                ApplyHotkeys();
            }
        }

        // 动态热键轮询兜底（抓键弹窗期间严禁触发；用户关掉热键时也不许轮询）
        if (!capturingKey && !hotkeysOff)
        {
            for (int hi = 0; hi < 8; hi++)
            {
                int id = hi + 1;
                if (!hkRegistered[id])
                {
                    bool down = IsHotkeyDown(hkVk[hi], hkMod[hi]);
                    if (down && !prevHkDown[hi])
                    {
                        prevHkDown[hi] = true;
                        Native.Log("POLL_HOTKEY id=" + id + " fg=" + ForegroundTitle());
                        HandleHotkey(id);
                        if (id == 5) return;
                    }
                    else if (!down)
                    {
                        prevHkDown[hi] = false;
                    }
                }
                else
                {
                    prevHkDown[hi] = false;
                }
            }
        }

        if (timerRunning && !timerPaused)
        {
            int rem = (int)Math.Ceiling((timerEndAt - DateTime.Now).TotalSeconds);
            if (rem <= 0)
            {
                timerRunning = false;
                timerPaused = false;
                timerRemainSec = 0;
                timerFlashDim = false;
                EvaluateCurrentTimeline();
                SyncTimerOverlay(false);
                UpdateTimerButtonsStyle();
                UpdateTimerUi();
            }
            else
            {
                bool secChanged = (rem != timerRemainSec);
                if (secChanged)
                {
                    timerRemainSec = rem;
                    EvaluateCurrentTimeline();
                }
                bool wantDim = false;
                if (timerActiveFlash == 1)
                {
                    long elapsedMs = (long)(DateTime.Now - timerStartedAt).TotalMilliseconds;
                    if (elapsedMs >= 0) wantDim = ((elapsedMs % 2000) >= 1500);
                }
                else if (timerActiveFlash == 2 || timerActiveFlash == 3)
                {
                    int halfMs = (timerActiveFlash == 3) ? 180 : 450;
                    long elapsedMs = (long)(DateTime.Now - timerStartedAt).TotalMilliseconds;
                    if (elapsedMs >= 0) wantDim = ((elapsedMs / halfMs) % 2) == 1;
                }
                bool flashChanged = (wantDim != timerFlashDim);
                timerFlashDim = wantDim;

                if (secChanged)
                {
                    SyncTimerOverlay(false);
                    UpdateTimerUi();
                }
                else if (flashChanged)
                {
                    if (timerOverlay != null)
                    {
                        timerOverlay.FlashDim = timerFlashDim;
                        timerOverlay.Invalidate();
                    }
                    UpdateTimerFlashPreview();
                }
            }
        }

        if (timerOverlay != null && timerOverlay.Visible && (DateTime.Now - lastTimerTopmost).TotalSeconds >= 2.0)
        {
            lastTimerTopmost = DateTime.Now;
            try { Native.MakeTopmost(timerOverlay.Handle); } catch { }
        }

        if (autoOn)
        {
            if (!typingOn)
            {
                if ((DateTime.Now - lastEnter).TotalMilliseconds >= IntervalMs)
                {
                    string fgOld = Native.ForegroundProcess();
                    if (Array.IndexOf(GameProcs, fgOld) >= 0)
                    {
                        Native.PressEnter();
                        lastEnter = DateTime.Now;
                        sent++;
                    }
                }
            }
            else if (!ocrHoldSend && (DateTime.Now - lastEnter).TotalMilliseconds >= IntervalMs)
            {
                string fg = Native.ForegroundProcess();
                if (Array.IndexOf(GameProcs, fg) >= 0)
                {
                    int n2 = (targets == null) ? 0 : targets.Count;
                    if (n2 > 0)
                    {
                        if (multiIdx >= n2) multiIdx = 0;
                        string addr2 = targets[multiIdx];
                        currentTargetAddr = addr2;
                        multiIdx = (multiIdx + 1) % n2;
                        lastEnter = DateTime.Now;

                        if (Interlocked.CompareExchange(ref pasteBusy, 1, 0) == 0)
                        {
                            if (TrySetClipboardText("connect " + addr2))
                            {
                                sent++;
                                Native.Log("paste: connect " + addr2);
                                RefreshActiveTargetRowHighlight();
                                ThreadPool.QueueUserWorkItem(delegate
                                {
                                    try
                                    {
                                        Native.PressCtrlV();
                                        Thread.Sleep(24);
                                        Native.PressEnter();
                                    }
                                    catch { }
                                    finally { Interlocked.Exchange(ref pasteBusy, 0); }
                                });
                            }
                            else
                            {
                                Interlocked.Exchange(ref pasteBusy, 0);
                                Native.Log("paste skip: clipboard locked for " + addr2);
                            }
                        }
                    }
                }
            }

            // 每 2 秒后台异步看一眼画面，判断是不是已经挤进去了（0 毫秒阻塞 UI 线程）
            bool ocrOn = (chkOcr == null || chkOcr.Checked);
            if (ocrOn && (DateTime.Now - tapOcrAt).TotalSeconds >= 2.0)
            {
                TriggerOcrCheckAsync();
            }
        }
        RefreshUi(false);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Native.SetDarkTitleBar(Handle, IsDark);
        Native.ShowWindow(Handle, 5);
        try
        {
            if (tabs != null) { tabs.PerformLayout(); LayoutAboutTab(); }
            LayoutToolBar();
            if (pJoinPanel != null) LayoutJoin();
            if (pTimerPanel != null) LayoutTimer();
        }
        catch { }
        // 上次关过热键就保持关闭，只挂开关热键
        if (hotkeysOff) { ReleaseHotkeys(); RegisterToggleHotkey(); }
        else ApplyHotkeys();
        UpdateHotkeyToggleButton();
        // 非管理员只记一行日志，不弹任何对话框
        if (!Native.IsElevated())
            Native.Log("not elevated (asInvoker): if an elevated window has focus, hotkeys may be blocked by UIPI");

        if (!hotkeysOff && !HotkeysOk())
        {
            Native.Log("hotkeys conflict: another instance or app holds hotkeys");
            try { if (toast != null) toast.ShowText("部分热键被占用：可打开“热键设置”查看或修改按键"); } catch { }
        }
        // 启动后自动刷一次服务器状态（人数 + 真实名），失败的再串行补查一遍
        if (favList.Length > 0)
        {
            var bootTimer = new System.Windows.Forms.Timer();
            bootTimer.Interval = 1200;
            bootTimer.Tick += delegate
            {
                bootTimer.Stop();
                bootTimer.Dispose();
                if (isClosing || lvSrv == null || favList.Length == 0) return;
                Native.Log("startup refresh: " + favList.Length + " servers");
                bootRefresh = true;
                try { RefreshAllFavs(); }
                catch (Exception ex) { bootRefresh = false; Native.Log("startup refresh EX: " + ex.Message); }
            };
            bootTimer.Start();
        }
        if (Environment.GetEnvironmentVariable("RX_DUMP_UI") == "1") DumpUi();
        UpdatePlayerToolBar();
        // 启动后静默检查更新（失败不打扰）
        if (autoUpdateOn && !startupCheckDone)
        {
            startupCheckDone = true;
            var updTimer = new System.Windows.Forms.Timer();
            updTimer.Interval = 2000;
            updTimer.Tick += delegate
            {
                updTimer.Stop();
                updTimer.Dispose();
                if (isClosing) return;
                try { CheckUpdate(false); } catch { }
            };
            updTimer.Start();
        }
        Native.Log("shown");
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        isClosing = true;
        try
        {
            if (autoOn || typingOn)
                StopActiveJoin(null, "close: stop active join");
        }
        catch { }
        for (int id = 1; id <= 8; id++)
        {
            try { if (hkRegistered[id]) Native.UnregisterHotKey(Handle, id); } catch { }
        }
        try { if (hkRegistered[HK_TOGGLE_ID]) Native.UnregisterHotKey(Handle, HK_TOGGLE_ID); } catch { }
        SaveSettings();
        try { if (tick != null) { tick.Stop(); tick.Dispose(); tick = null; } } catch (Exception ex) { Native.Log("tick dispose: " + ex.Message); }
        try { if (uiComponents != null) { uiComponents.Dispose(); uiComponents = null; } } catch { }
        try { if (toast != null) { toast.Close(); toast.Dispose(); toast = null; } } catch { }
        try { if (overlay != null) { overlay.Close(); overlay.Dispose(); overlay = null; } } catch { }
        try { if (timerOverlay != null) { timerOverlay.Close(); timerOverlay.Dispose(); timerOverlay = null; } } catch { }
        for (int i = 0; i < ownedGdi.Count; i++)
        {
            try { if (ownedGdi[i] != null) ownedGdi[i].Dispose(); } catch { }
        }
        ownedGdi.Clear();
        fontPool.Clear();
        base.OnFormClosed(e);
    }
}
