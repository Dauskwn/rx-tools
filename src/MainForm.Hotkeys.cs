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

public partial class MainForm
{
    void HandleHotkey(int id)
    {
        switch (id)
        {
            case 1:
                overlay.CycleStyle();
                suppressSettingsSave = true;
                if (cmbStyle != null) cmbStyle.SelectedIndex = overlay.StyleIndex;
                suppressSettingsSave = false;
                if (crossPreview != null) crossPreview.Invalidate();
                SaveSettings();
                toast.ShowText("准星样式：" + Overlay.Styles[overlay.StyleIndex]);
                RefreshUi(true);
                break;
            case 2:
                ToggleStart();
                break;
            case 3:
                overlay.CycleColor();
                suppressSettingsSave = true;
                if (cmbColor != null) cmbColor.SelectedIndex = overlay.ColorIndex;
                suppressSettingsSave = false;
                if (crossPreview != null) crossPreview.Invalidate();
                SaveSettings();
                toast.ShowText("准星颜色：" + Overlay.ColorNames[overlay.ColorIndex]);
                RefreshUi(true);
                break;
            case 4:
                ToggleCross();
                break;
            case 5:
                Native.Log("quit via hotkey");
                Close();
                break;
            case 6:
                StartOrResetCountdown();
                break;
            case 7:
                ToggleCountdownOverlay();
                break;
            case 8:
                PauseOrResumeCountdown();
                break;
        }
    }

    static string ForegroundTitle()
    {
        try
        {
            IntPtr h = Native.GetForegroundWindow();
            if (h == IntPtr.Zero) return "(none)";
            var sb = new StringBuilder(256);
            Native.GetWindowTextW(h, sb, sb.Capacity);
            string t = sb.ToString();
            if (t.Length > 40) t = t.Substring(0, 40);
            return t.Length == 0 ? "(untitled)" : t;
        }
        catch { return "(?)"; }
    }

    // ================= 可自定义热键 =================

    static string VkName(int vk)
    {
        switch (vk)
        {
            case 0x30: return "0"; case 0x31: return "1"; case 0x32: return "2";
            case 0x33: return "3"; case 0x34: return "4"; case 0x35: return "5";
            case 0x36: return "6"; case 0x37: return "7"; case 0x38: return "8";
            case 0x39: return "9";
            case 0xC0: return "`";
            case 0xBD: return "-";
            case 0xBB: return "=";
            case 0xDB: return "[";
            case 0xDD: return "]";
            case 0xDC: return "\\";
            case 0xBA: return ";";
            case 0xDE: return "'";
            case 0xBC: return ",";
            case 0xBE: return ".";
            case 0xBF: return "/";
        }
        try { return ((Keys)vk).ToString(); }
        catch { return "?"; }
    }

    string KeyName(int id)
    {
        int i = id - 1;
        if (i < 0 || i >= HKFunc.Length) return "?";
        var sb = new StringBuilder();
        if ((hkMod[i] & 0x0002) != 0) sb.Append("Ctrl+");
        if ((hkMod[i] & 0x0001) != 0) sb.Append("Alt+");
        if ((hkMod[i] & 0x0004) != 0) sb.Append("Shift+");
        sb.Append(VkName(hkVk[i]));
        return sb.ToString();
    }

    static bool IsHotkeyDown(int vk, int mod)
    {
        if (vk <= 0 || !Native.Down(vk)) return false;
        bool ctrl = Native.Down(0x11);
        bool alt = Native.Down(0x12);
        bool shift = Native.Down(0x10);
        if (((mod & 0x0002) != 0) != ctrl) return false;
        if (((mod & 0x0001) != 0) != alt) return false;
        if (((mod & 0x0004) != 0) != shift) return false;
        return true;
    }

    // ================= 关闭热键（F5/F6… 交还给系统） =================
    const int HK_TOGGLE_ID = 9;
    const int HK_TOGGLE_VK = 0x79;   // F10
    const int HK_TOGGLE_MOD = 0x0002; // Ctrl

    string ToggleKeyName() { return "Ctrl+F10"; }

    bool HotkeysOk()
    {
        for (int i = 1; i <= 8; i++) if (!hkRegistered[i]) return false;
        return true;
    }

    void ReleaseHotkeys()
    {
        for (int id = 1; id <= 8; id++)
        {
            try { if (hkRegistered[id]) Native.UnregisterHotKey(Handle, id); } catch { }
            hkRegistered[id] = false;
        }
        Native.Log("hotkeys released (off)");
    }

    void ToggleHotkeys()
    {
        hotkeysOff = !hotkeysOff;
        if (hotkeysOff) ReleaseHotkeys();
        else ApplyHotkeys();
        UpdateHotkeyToggleButton();
        SaveSettings();
        try { if (toast != null) toast.ShowText(hotkeysOff ? "热键已关闭，" + ToggleKeyName() + " 可恢复" : "热键已恢复"); } catch { }
    }

    void UpdateHotkeyToggleButton()
    {
        if (btnHotkeyOff == null) return;
        btnHotkeyOff.Text = hotkeysOff ? ("开启热键 (" + ToggleKeyName() + ")") : ("关闭热键 (" + ToggleKeyName() + ")");
        if (IsDark)
        {
            btnHotkeyOff.FlatStyle = FlatStyle.Flat;
            btnHotkeyOff.BackColor = hotkeysOff ? Color.FromArgb(56, 40, 20) : Color.FromArgb(33, 38, 45);
            btnHotkeyOff.ForeColor = hotkeysOff ? Color.FromArgb(227, 179, 65) : Color.FromArgb(121, 192, 255);
            btnHotkeyOff.FlatAppearance.BorderColor = hotkeysOff ? Color.FromArgb(165, 112, 36) : Color.FromArgb(54, 59, 66);
            btnHotkeyOff.FlatAppearance.MouseOverBackColor = hotkeysOff ? Color.FromArgb(72, 50, 24) : Color.FromArgb(44, 52, 63);
        }
        else
        {
            btnHotkeyOff.FlatStyle = FlatStyle.Flat;
            btnHotkeyOff.BackColor = Color.FromArgb(248, 250, 252);
            btnHotkeyOff.ForeColor = hotkeysOff ? Color.FromArgb(180, 83, 9) : Color.FromArgb(24, 64, 128);
            btnHotkeyOff.FlatAppearance.BorderColor = hotkeysOff
                ? Color.FromArgb(232, 190, 120) : Color.FromArgb(203, 213, 225);
            btnHotkeyOff.FlatAppearance.MouseOverBackColor = Color.FromArgb(238, 242, 248);
        }
    }

    int RegisterToggleHotkey()
    {
        try { if (hkRegistered[HK_TOGGLE_ID]) Native.UnregisterHotKey(Handle, HK_TOGGLE_ID); } catch { }
        hkRegistered[HK_TOGGLE_ID] = false;
        if (!IsHandleCreated || isClosing) return 0;
        try
        {
            hkRegistered[HK_TOGGLE_ID] = Native.RegisterHotKey(Handle, HK_TOGGLE_ID, (uint)HK_TOGGLE_MOD, (uint)HK_TOGGLE_VK);
        }
        catch { hkRegistered[HK_TOGGLE_ID] = false; }
        return hkRegistered[HK_TOGGLE_ID] ? 0 : 1;
    }

    int ApplyHotkeys()
    {
        int fail = 0;
        for (int id = 1; id <= 8; id++)
        {
            try { if (hkRegistered[id]) { Native.UnregisterHotKey(Handle, id); } } catch { }
            hkRegistered[id] = false;
        }
        if (!IsHandleCreated || isClosing) return 0;
        for (int id = 1; id <= 8; id++)
        {
            try { hkRegistered[id] = Native.RegisterHotKey(Handle, id, (uint)hkMod[id - 1], (uint)hkVk[id - 1]); }
            catch { hkRegistered[id] = false; }
            if (!hkRegistered[id]) fail++;
        }
        var logItems = new string[8];
        for (int id = 1; id <= 8; id++) logItems[id - 1] = KeyName(id) + "=" + hkRegistered[id];
        Native.Log("hotkeys: " + string.Join(" ", logItems));
        RegisterToggleHotkey();
        return fail;
    }

    void UpdateHotkeyLabels()
    {
        try
        {
            if (btnAuto != null) UpdateStartButton();
            if (btnCross != null) btnCross.Text = "准星开关 (" + KeyName(4) + ")";
            if (btnColor != null) btnColor.Text = "换颜色 (" + KeyName(3) + ")";
            UpdateTimerButtonsStyle();
            if (lblTimerHint != null)
            {
                lblTimerHint.Text = "热键：" + KeyName(6) + " 开始/重置 · " + KeyName(8) + " 暂停/继续 · " + KeyName(7)
                    + " 显隐悬浮窗（右键预设可复制、重命名或删除）";
            }
            if (lblTapGuide != null)
            {
                string hk = KeyName(2);
                string[] tapGuideLines = new string[] {
                    "手动档：在游戏内点击“连接”后，工具自动连续发送回车键。",
                    "",
                    "1. 打开游戏：Play → 服务器列表，选中目标服务器并点击“连接”；",
                    "2. 若弹出“服务器人数已满”提示框，保持开启即可；",
                    "3. 直接在游戏内按 " + hk + "（全局热键，无需切回工具），",
                    "   工具会按所设定的间隔连续发送回车，关闭弹框并重新发起连接；",
                    "4. 顺利进服后按 " + hk + " 停止（开启 OCR 识别时会自动停止）。",
                    "",
                    "提示：请保持游戏处于前台，工具仅向前台游戏窗口发送按键。",
                    "",
                    "详细说明与原理解析可点击上方“?”查看使用指南。"
                };
                lblTapGuide.Text = string.Join(Environment.NewLine, tapGuideLines);
            }
            if (lblCrossHint != null)
            {
                lblCrossHint.Text = "快捷键：\r\n"
                    + "· " + KeyName(1) + " ：循环切换准星样式（6 种）\r\n"
                    + "· " + KeyName(3) + " ：循环切换准星颜色（6 色）\r\n"
                    + "· " + KeyName(4) + " ：显示 / 隐藏屏幕准星\r\n"
                    + "· " + KeyName(5) + " ：安全退出工具箱\r\n\r\n"
                    + "提示：准星为透明置顶叠加层，建议将游戏显示模式设为“无边框窗口化”以获得最佳体验。";
            }
            UpdateJoinHint();
        }
        catch { }
    }

    bool CaptureKey(out int vk, out int mod)
    {
        int cvk = 0, cmod = 0;
        bool got = false;
        capturingKey = true;
        int[] oldVk = (int[])hkVk.Clone();
        int[] oldMod = (int[])hkMod.Clone();
        for (int id = 1; id <= 8; id++)
        {
            try { if (hkRegistered[id]) Native.UnregisterHotKey(Handle, id); } catch { }
            hkRegistered[id] = false;
        }

        using (var f = new Form())
        {
            f.Text = "按下你要用的键";
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.StartPosition = FormStartPosition.CenterParent;
            f.MinimizeBox = false;
            f.MaximizeBox = false;
            f.ShowInTaskbar = false;
            f.KeyPreview = true;
            f.ClientSize = new Size(420, 150);
            f.BackColor = Color.White;

            var lb = new Label();
            lb.Text = "请按下要绑定的键（可带 Ctrl / Alt / Shift）\r\n按 Esc 取消";
            lb.Font = TrackFont("Microsoft YaHei UI", 10.5f, FontStyle.Regular);
            lb.TextAlign = ContentAlignment.MiddleCenter;
            lb.SetBounds(16, 26, 388, 60);
            f.Controls.Add(lb);

            var lbNow = new Label();
            lbNow.Text = "（等待按键…）";
            lbNow.Font = TrackFont("Microsoft YaHei UI", 12f, FontStyle.Bold);
            lbNow.ForeColor = Color.FromArgb(20, 80, 150);
            lbNow.TextAlign = ContentAlignment.MiddleCenter;
            lbNow.SetBounds(16, 90, 388, 34);
            f.Controls.Add(lbNow);

            f.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                int k = (int)e.KeyCode;
                if (k == 0x10 || k == 0x11 || k == 0x12 || k == 0x5B || k == 0x5C || k == 0xA0 || k == 0xA1 || k == 0xA2 || k == 0xA3 || k == 0xA4 || k == 0xA5)
                    return;
                if (k == 0x1B)
                {
                    f.DialogResult = DialogResult.Cancel;
                    f.Close();
                    return;
                }
                int m = 0;
                if (e.Control) m |= 0x0002;
                if (e.Alt) m |= 0x0001;
                if (e.Shift) m |= 0x0004;
                cvk = k;
                cmod = m;
                got = true;
                var sb = new StringBuilder();
                if ((m & 0x0002) != 0) sb.Append("Ctrl+");
                if ((m & 0x0001) != 0) sb.Append("Alt+");
                if ((m & 0x0004) != 0) sb.Append("Shift+");
                sb.Append(VkName(k));
                lbNow.Text = sb.ToString();
                f.DialogResult = DialogResult.OK;
                f.Close();
            };

            ThemeDialog(f);
            f.ShowDialog(this);
        }

        for (int hi = 0; hi < 8; hi++) prevHkDown[hi] = true; // 防止松键前边缘误触发
        capturingKey = false;
        vk = cvk;
        mod = cmod;

        if (!got)
        {
            Array.Copy(oldVk, hkVk, 8);
            Array.Copy(oldMod, hkMod, 8);
        }
        ApplyHotkeys();
        return got;
    }

    void ShowHotkeyDialog()
    {
        using (var f = new Form())
        {
            f.Text = "热键设置 · RX 工具箱";
            f.StartPosition = FormStartPosition.CenterParent;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MinimizeBox = false;
            f.MaximizeBox = false;
            f.ShowInTaskbar = false;
            f.BackColor = Color.White;
            f.ClientSize = new Size(680, 448);

            var lbTitle = new Label();
            lbTitle.Text = "全局热键在游戏前台同样生效。选中一行后点击“修改按键…”，直接按下目标按键即可。";
            lbTitle.Font = TrackFont("Microsoft YaHei UI", 9.5f, FontStyle.Regular);
            lbTitle.ForeColor = Color.FromArgb(60, 70, 85);
            lbTitle.SetBounds(16, 12, 648, 22);
            f.Controls.Add(lbTitle);

            var lv = new BufferedListView();
            lv.View = View.Details;
            lv.FullRowSelect = true;
            lv.MultiSelect = false;
            lv.HideSelection = false;
            lv.HeaderStyle = ColumnHeaderStyle.Nonclickable;
            lv.Font = TrackFont("Microsoft YaHei UI", 9.5f, FontStyle.Regular);
            lv.Columns.Add("功能", 150);
            lv.Columns.Add("当前按键", 110);
            lv.Columns.Add("状态", 110);
            lv.Columns.Add("说明", 270);
            lv.SetBounds(16, 42, 648, 314);
            f.Controls.Add(lv);

            Action refill = delegate
            {
                lv.BeginUpdate();
                lv.Items.Clear();
                for (int id = 1; id <= 8; id++)
                {
                    var it = new ListViewItem(HKFunc[id - 1]);
                    it.SubItems.Add(KeyName(id));
                    it.SubItems.Add(hkRegistered[id] ? "全局已注册" : "轮询模式");
                    it.SubItems.Add(HKTip[id - 1]);
                    it.ForeColor = hkRegistered[id]
                        ? (IsDark ? Color.FromArgb(230, 237, 243) : Color.FromArgb(30, 41, 59))
                        : (IsDark ? Color.FromArgb(227, 179, 65) : Color.FromArgb(180, 80, 20));
                    it.Tag = id;
                    lv.Items.Add(it);
                }
                lv.EndUpdate();
                if (lv.Items.Count > 0) lv.Items[0].Selected = true;
            };
            refill();

            var lbHint = new Label();
            lbHint.Text = "“轮询模式”表示系统热键已被其它软件占用，工具已自动切换为按键轮询，功能同样正常。\r\n如常用功能键冲突，可改为 Ctrl+ 或 Alt+ 组合键。";
            lbHint.ForeColor = Color.FromArgb(120, 126, 136);
            lbHint.SetBounds(16, 362, 520, 38);
            f.Controls.Add(lbHint);

            Action modify = delegate
            {
                if (lv.SelectedItems.Count == 0)
                {
                    toast.ShowText("请先选中需要修改的行");
                    return;
                }
                int id = Convert.ToInt32(lv.SelectedItems[0].Tag);
                int nvk, nmod;
                if (!CaptureKey(out nvk, out nmod)) return;

                for (int other = 1; other <= 8; other++)
                {
                    if (other == id) continue;
                    if (hkVk[other - 1] == nvk && hkMod[other - 1] == nmod)
                    {
                        toast.ShowText("“" + HKFunc[other - 1] + "”已占用 " + KeyName(other) + "，请更换其它按键");
                        return;
                    }
                }

                hkVk[id - 1] = nvk;
                hkMod[id - 1] = nmod;
                int fail = ApplyHotkeys();
                SaveSettings();
                UpdateHotkeyLabels();
                refill();
                for (int i = 0; i < lv.Items.Count; i++)
                    if (Convert.ToInt32(lv.Items[i].Tag) == id) { lv.Items[i].Selected = true; break; }
                Native.Log("hotkey " + HKFunc[id - 1] + " -> " + KeyName(id) + " (fail=" + fail + ")");
                toast.ShowText(HKFunc[id - 1] + " 现在是 " + KeyName(id));
            };

            var bMod = new Button();
            bMod.Text = "修改按键…";
            bMod.SetBounds(16, 404, 120, 32);
            bMod.Click += delegate { modify(); };
            f.Controls.Add(bMod);

            var bDef = new Button();
            bDef.Text = "恢复默认";
            bDef.SetBounds(146, 404, 100, 32);
            bDef.Click += delegate
            {
                if (MessageBox.Show(f, "把 8 个热键都恢复成默认（F5 / F6 / F7 / F8 / Ctrl+F11 / F9 / F10 / Ctrl+F9）？",
                        "RX 工具箱", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                    return;
                for (int i = 0; i < 8; i++)
                {
                    hkVk[i] = HKDefVk[i];
                    hkMod[i] = HKDefMod[i];
                }
                ApplyHotkeys();
                SaveSettings();
                UpdateHotkeyLabels();
                refill();
                toast.ShowText("已恢复默认热键");
            };
            f.Controls.Add(bDef);

            var bClose = new Button();
            bClose.Text = "关闭";
            bClose.SetBounds(564, 404, 100, 32);
            bClose.DialogResult = DialogResult.OK;
            f.Controls.Add(bClose);
            f.AcceptButton = bClose;
            f.CancelButton = bClose;

            lv.DoubleClick += delegate { modify(); };
            ThemeDialog(f);
            Native.Log("hotkey dialog shown");
            f.ShowDialog(this);
        }
        UpdateHotkeyLabels();
    }
}
