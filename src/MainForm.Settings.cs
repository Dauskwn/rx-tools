using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

public partial class MainForm
{
    Panel settingsHost;
    Panel[] settingsCards = new Panel[0];
    int[] settingsCardHeights = new int[0];

    RxSegmentedControl segSettingsTheme;
    CheckBox chkSettingsNavCollapsed;
    CheckBox chkSettingsHotkeysOff;
    Button btnSettingsHotkeys;
    Label lblSettingsHotkeySummary;
    Label lblSettingsThemeHint;
    Label lblSettingsAutoUpdHint;
    Button btnSettingsViewLog;
    Button btnSettingsClearLog;
    Button btnSettingsOpenDir;
    Label lblSettingsLogInfo;
    bool suppressSettingsUi = false;

    internal static int ModeToSettingsThemeIndex(int mode)
    {
        if (mode == 0) return 0; // 跟随系统
        if (mode == 1) return 2; // 夜间深色
        return 1;                // 护眼浅色 (默认 mode == 2)
    }

    internal static int SettingsThemeIndexToMode(int idx)
    {
        if (idx == 0) return 0; // 跟随系统
        if (idx == 2) return 1; // 夜间深色
        return 2;               // 护眼浅色
    }

    static string FormatSettingsByteSize(long bytes)
    {
        if (bytes <= 0) return "0 B（暂无日志）";
        if (bytes < 1024) return bytes + " B";
        if (bytes < 1024 * 1024) return string.Format("{0:F1} KB", bytes / 1024.0);
        return string.Format("{0:F2} MB", bytes / (1024.0 * 1024.0));
    }


    Panel BuildSettingsPage()
    {
        var page = new Panel();
        page.Text = "设置";
        page.BackColor = Color.FromArgb(250, 251, 253);

        settingsHost = new Panel();
        settingsHost.Dock = DockStyle.Fill;
        settingsHost.AutoScroll = true;
        settingsHost.BackColor = Color.FromArgb(250, 251, 253);
        page.Controls.Add(settingsHost);

        Font fCardTitle = TrackFont("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
        Font fBody = TrackFont("Microsoft YaHei UI", 9.5f, FontStyle.Regular);
        Font fSmall = TrackFont("Microsoft YaHei UI", 9f, FontStyle.Regular);
        Color cTitle = Color.FromArgb(26, 58, 104);
        Color cBody = Color.FromArgb(30, 41, 59);
        Color cMuted = Color.FromArgb(108, 116, 128);
        var tip = new ToolTip(uiComponents);

        PaintEventHandler drawCardBorder = delegate(object s, PaintEventArgs pe)
        {
            var pnl = s as Panel;
            if (pnl == null || pnl.Width < 4 || pnl.Height < 4) return;
            pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(ThemePalette.BorderSubtle, 1f))
                Native.DrawRoundedRect(pe.Graphics, pen, new Rectangle(0, 0, pnl.Width - 1, pnl.Height - 1), 8);
        };

        var cardList = new List<Panel>();
        var cardHeights = new List<int>();

        Func<string, string, int, Panel> makeCard = delegate(string title, string subtitle, int height)
        {
            var card = new Panel();
            card.Tag = "rx-surface-card";
            card.BackColor = ThemePalette.SurfaceAlt;
            card.SetBounds(20, 16, 660, height);
            card.Paint += drawCardBorder;

            var lblT = new Label();
            lblT.Text = title;
            lblT.Font = fCardTitle;
            lblT.ForeColor = cTitle;
            lblT.BackColor = Color.Transparent;
            lblT.SetBounds(16, 11, 190, 24);
            lblT.UseMnemonic = false;
            card.Controls.Add(lblT);

            var lblSub = new Label();
            lblSub.Text = subtitle;
            lblSub.Font = fSmall;
            lblSub.ForeColor = cMuted;
            lblSub.BackColor = Color.Transparent;
            lblSub.SetBounds(210, 13, 430, 20);
            lblSub.UseMnemonic = false;
            lblSub.AutoEllipsis = true;
            card.Controls.Add(lblSub);

            settingsHost.Controls.Add(card);
            cardList.Add(card);
            cardHeights.Add(height);
            return card;
        };

        // 1. 外观与窗口
        Panel cardAppear = makeCard("外观与窗口", "主题配色、侧边栏与托盘设置", 148);

        var lblTheme = new Label();
        lblTheme.Text = "界面主题：";
        lblTheme.Font = fBody;
        lblTheme.ForeColor = cBody;
        lblTheme.BackColor = Color.Transparent;
        lblTheme.SetBounds(16, 48, 86, 22);
        lblTheme.UseMnemonic = false;
        cardAppear.Controls.Add(lblTheme);

        segSettingsTheme = new RxSegmentedControl();
        segSettingsTheme.Items = new string[] { "跟随系统", "护眼浅色", "夜间深色" };
        segSettingsTheme.SetBounds(106, 43, 258, 30);
        segSettingsTheme.SetSelectedSilent(ModeToSettingsThemeIndex(themeMode));
        segSettingsTheme.SelectedIndexChanged += delegate
        {
            if (suppressSettingsUi || suppressSettingsSave) return;
            int nm = SettingsThemeIndexToMode(segSettingsTheme.SelectedIndex);
            if (nm != themeMode)
            {
                themeMode = nm;
                ApplyTheme();
                SaveSettings();
            }
        };
        cardAppear.Controls.Add(segSettingsTheme);

        lblSettingsThemeHint = new Label();
        lblSettingsThemeHint.Text = "也可以点右下角主题直接切换";
        lblSettingsThemeHint.Font = fSmall;
        lblSettingsThemeHint.ForeColor = cMuted;
        lblSettingsThemeHint.BackColor = Color.Transparent;
        lblSettingsThemeHint.SetBounds(376, 48, 260, 22);
        lblSettingsThemeHint.UseMnemonic = false;
        lblSettingsThemeHint.AutoEllipsis = true;
        cardAppear.Controls.Add(lblSettingsThemeHint);

        chkSettingsNavCollapsed = new CheckBox();
        chkSettingsNavCollapsed.Text = "收起左侧导航栏（只显示图标）";
        chkSettingsNavCollapsed.Font = fBody;
        chkSettingsNavCollapsed.ForeColor = cBody;
        chkSettingsNavCollapsed.BackColor = Color.Transparent;
        chkSettingsNavCollapsed.SetBounds(16, 84, 340, 24);
        chkSettingsNavCollapsed.Checked = navRailCollapsed;
        chkSettingsNavCollapsed.CheckedChanged += delegate
        {
            if (suppressSettingsUi || suppressSettingsSave) return;
            bool nextCol = chkSettingsNavCollapsed.Checked;
            if (navRail != null && navRail.Collapsed != nextCol)
            {
                navRail.SetCollapsed(nextCol);
            }
            else if (navRailCollapsed != nextCol)
            {
                navRailCollapsed = nextCol;
                LayoutHeaderBar();
                SaveSettings();
            }
        };
        cardAppear.Controls.Add(chkSettingsNavCollapsed);

        chkCloseToTray = new CheckBox();
        chkCloseToTray.Text = "点关闭 (×) 时缩进系统托盘（不退出程序，右键托盘图标可彻底退出）";
        chkCloseToTray.Font = fSmall;
        chkCloseToTray.ForeColor = cBody;
        chkCloseToTray.BackColor = Color.Transparent;
        chkCloseToTray.SetBounds(16, 114, 560, 24);
        chkCloseToTray.Checked = closeToTrayOn;
        chkCloseToTray.CheckedChanged += delegate
        {
            if (suppressSettingsUi || suppressSettingsSave) return;
            closeToTrayOn = chkCloseToTray.Checked;
            UpdateTrayIconVisibility();
            SaveSettings();
        };
        cardAppear.Controls.Add(chkCloseToTray);


        // 3. 全局热键
        Panel cardHotkeys = makeCard("全局热键", "一键暂停快捷键或自定义按键", 112);

        chkSettingsHotkeysOff = new CheckBox();
        chkSettingsHotkeysOff.Text = "暂停全部快捷键（暂时把 F5/F6 等按键交还给游戏）";
        chkSettingsHotkeysOff.Font = fBody;
        chkSettingsHotkeysOff.ForeColor = cBody;
        chkSettingsHotkeysOff.BackColor = Color.Transparent;
        chkSettingsHotkeysOff.SetBounds(16, 44, 440, 24);
        chkSettingsHotkeysOff.Checked = hotkeysOff;
        chkSettingsHotkeysOff.CheckedChanged += delegate
        {
            if (suppressSettingsUi || suppressSettingsSave) return;
            if (chkSettingsHotkeysOff.Checked != hotkeysOff) ToggleHotkeys();
        };
        cardHotkeys.Controls.Add(chkSettingsHotkeysOff);

        btnSettingsHotkeys = new Button();
        btnSettingsHotkeys.Text = "设置热键…";
        btnSettingsHotkeys.FlatStyle = FlatStyle.Flat;
        btnSettingsHotkeys.FlatAppearance.BorderColor = Color.FromArgb(180, 205, 238);
        btnSettingsHotkeys.SetBounds(526, 40, 114, 30);
        btnSettingsHotkeys.Click += delegate { ShowHotkeyDialog(); };
        cardHotkeys.Controls.Add(btnSettingsHotkeys);

        lblSettingsHotkeySummary = new Label();
        lblSettingsHotkeySummary.Font = fSmall;
        lblSettingsHotkeySummary.ForeColor = cMuted;
        lblSettingsHotkeySummary.BackColor = Color.Transparent;
        lblSettingsHotkeySummary.SetBounds(16, 78, 624, 22);
        lblSettingsHotkeySummary.UseMnemonic = false;
        lblSettingsHotkeySummary.AutoEllipsis = true;
        cardHotkeys.Controls.Add(lblSettingsHotkeySummary);

        // 4. 更新与备份（玩家版合并为「更新与日志」）
        Panel cardUpdHost = makeCard("更新与日志", "自动检查更新、运行日志与配置目录", 122);

        chkAutoUpdate = new CheckBox();
        chkAutoUpdate.Text = "启动时自动检查更新";
        chkAutoUpdate.Font = fBody;
        chkAutoUpdate.ForeColor = cBody;
        chkAutoUpdate.BackColor = Color.Transparent;
        chkAutoUpdate.SetBounds(16, 43, 194, 24);
        chkAutoUpdate.Checked = autoUpdateOn;
        chkAutoUpdate.CheckedChanged += delegate
        {
            if (suppressSettingsUi || suppressSettingsSave) return;
            autoUpdateOn = chkAutoUpdate.Checked;
            SaveSettings();
        };
        cardUpdHost.Controls.Add(chkAutoUpdate);

        lblSettingsAutoUpdHint = new Label();
        lblSettingsAutoUpdHint.Text = "也可以在「关于与更新」手动检查新版";
        lblSettingsAutoUpdHint.Font = fSmall;
        lblSettingsAutoUpdHint.ForeColor = cMuted;
        lblSettingsAutoUpdHint.BackColor = Color.Transparent;
        lblSettingsAutoUpdHint.SetBounds(218, 45, 360, 22);
        lblSettingsAutoUpdHint.UseMnemonic = false;
        lblSettingsAutoUpdHint.AutoEllipsis = true;
        cardUpdHost.Controls.Add(lblSettingsAutoUpdHint);

        Panel cardLogHost = cardUpdHost;
        int logBtnY = 78;
        string openDirTip = "打开存放配置、收藏与自定义文案的文件夹";

        btnSettingsViewLog = new Button();
        btnSettingsViewLog.Text = "查看日志";
        btnSettingsViewLog.FlatStyle = FlatStyle.Flat;
        btnSettingsViewLog.FlatAppearance.BorderColor = Color.FromArgb(180, 205, 238);
        btnSettingsViewLog.SetBounds(16, logBtnY, 92, 30);
        btnSettingsViewLog.Click += delegate
        {
            try
            {
                if (!string.IsNullOrEmpty(Native.LogPath) && File.Exists(Native.LogPath))
                {
                    using (var proc = Process.Start(Native.LogPath)) { }
                }
                else
                {
                    toast.ShowText("还没有日志文件");
                }
            }
            catch (Exception ex) { toast.ShowText("打不开日志：" + ex.Message); }
        };
        cardLogHost.Controls.Add(btnSettingsViewLog);

        btnSettingsClearLog = new Button();
        btnSettingsClearLog.Text = "清空日志";
        btnSettingsClearLog.FlatStyle = FlatStyle.Flat;
        btnSettingsClearLog.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
        btnSettingsClearLog.SetBounds(116, logBtnY, 92, 30);
        btnSettingsClearLog.Click += delegate
        {
            long prevBytes = Native.GetLogSizeBytes();
            if (prevBytes <= 0 && (string.IsNullOrEmpty(Native.LogPath) || !File.Exists(Native.LogPath)))
            {
                RefreshSettingsLogSize();
                toast.ShowText("当前没有日志文件");
                return;
            }
            if (Native.ClearLog())
            {
                RefreshSettingsLogSize();
                toast.ShowText("已清理本地日志文件");
            }
            else
            {
                toast.ShowText("日志正在被其它程序占用，暂时无法删除");
            }
        };
        cardLogHost.Controls.Add(btnSettingsClearLog);

        btnSettingsOpenDir = new Button();
        btnSettingsOpenDir.Text = "打开数据目录";
        btnSettingsOpenDir.FlatStyle = FlatStyle.Flat;
        btnSettingsOpenDir.FlatAppearance.BorderColor = Color.FromArgb(180, 205, 238);
        btnSettingsOpenDir.SetBounds(216, logBtnY, 114, 30);
        btnSettingsOpenDir.Click += delegate
        {
            try
            {
                string dir = !string.IsNullOrEmpty(dataRootDir)
                    ? dataRootDir
                    : Path.GetDirectoryName(Application.ExecutablePath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    using (var proc = Process.Start(dir)) { }
                }
                else
                {
                    toast.ShowText("找不到数据目录");
                }
            }
            catch (Exception ex) { toast.ShowText("无法打开目录：" + ex.Message); }
        };
        tip.SetToolTip(btnSettingsOpenDir, openDirTip);
        cardLogHost.Controls.Add(btnSettingsOpenDir);

        lblSettingsLogInfo = new Label();
        lblSettingsLogInfo.Font = fSmall;
        lblSettingsLogInfo.ForeColor = cMuted;
        lblSettingsLogInfo.BackColor = Color.Transparent;
        lblSettingsLogInfo.SetBounds(342, logBtnY + 5, 300, 22);
        lblSettingsLogInfo.UseMnemonic = false;
        lblSettingsLogInfo.AutoEllipsis = true;
        cardLogHost.Controls.Add(lblSettingsLogInfo);


        var bottomSpacer = new Panel();
        bottomSpacer.BackColor = Color.Transparent;
        bottomSpacer.SetBounds(20, 740, 100, 18);
        settingsHost.Controls.Add(bottomSpacer);

        settingsCards = cardList.ToArray();
        settingsCardHeights = cardHeights.ToArray();

        settingsHost.Resize += delegate { LayoutSettingsPage(); };
        RefreshSettingsLogSize();
        SyncSettingsPageUi();
        LayoutSettingsPage();
        return page;
    }

    void RefreshSettingsLogSize()
    {
        if (lblSettingsLogInfo == null) return;
        long bytes = Native.GetLogSizeBytes();
        lblSettingsLogInfo.Text = "日志占用：" + FormatSettingsByteSize(bytes);
    }

    void LayoutSettingsPage()
    {
        if (settingsHost == null || settingsCards.Length == 0) return;
        int availW = settingsHost.ClientSize.Width;
        if (availW <= 100) return;

        int x = 20;
        int cardW = Math.Max(560, Math.Min(820, availW - 40));
        int y = 16;
        for (int i = 0; i < settingsCards.Length; i++)
        {
            Panel c = settingsCards[i];
            int h = (i < settingsCardHeights.Length) ? settingsCardHeights[i] : c.Height;
            c.SetBounds(x, y, cardW, h);
            c.Invalidate();
            y += h + 12;
        }

        // 卡片内右侧自适应控件
        if (lblSettingsThemeHint != null)
            lblSettingsThemeHint.Width = Math.Max(140, cardW - 392);
        if (chkCloseToTray != null)
            chkCloseToTray.Width = Math.Max(400, cardW - 32);
        if (btnSettingsHotkeys != null)
            btnSettingsHotkeys.Left = Math.Max(420, cardW - 130);
        if (chkSettingsHotkeysOff != null && btnSettingsHotkeys != null)
            chkSettingsHotkeysOff.Width = Math.Max(300, btnSettingsHotkeys.Left - 28);
        if (lblSettingsHotkeySummary != null)
            lblSettingsHotkeySummary.Width = Math.Max(300, cardW - 32);
        if (lblSettingsAutoUpdHint != null)
            lblSettingsAutoUpdHint.Width = Math.Max(160, cardW - 234);
        if (lblSettingsLogInfo != null)
            lblSettingsLogInfo.Width = Math.Max(140, cardW - 358);

        // 底部留白控件跟随最后一个卡片之后，避免滚动条贴底
        Control last = settingsHost.Controls[settingsHost.Controls.Count - 1];
        if (last != null && last.Tag == null && last.Controls.Count == 0 && last.Height == 18)
            last.SetBounds(x, y, 40, 18);
    }

    void SyncSettingsPageUi()
    {
        if (settingsPage == null) return;
        suppressSettingsUi = true;
        try
        {
            if (segSettingsTheme != null)
                segSettingsTheme.SetSelectedSilent(ModeToSettingsThemeIndex(themeMode));
            if (chkSettingsNavCollapsed != null)
                chkSettingsNavCollapsed.Checked = navRailCollapsed;
            if (chkCloseToTray != null)
                chkCloseToTray.Checked = closeToTrayOn;


            if (chkSettingsHotkeysOff != null)
                chkSettingsHotkeysOff.Checked = hotkeysOff;
            if (lblSettingsHotkeySummary != null)
            {
                string statePrefix = hotkeysOff ? "【已暂停】" : "当前热键：";
                lblSettingsHotkeySummary.Text = statePrefix
                    + "样式 " + KeyName(1)
                    + " · 颜色 " + KeyName(3)
                    + " · 准星 " + KeyName(4)
                    + " · 挤服 " + KeyName(2)
                    + " · 倒计时 " + KeyName(6)
                    + " · 开关 " + ToggleKeyName();
            }

            if (chkAutoUpdate != null)
                chkAutoUpdate.Checked = autoUpdateOn;
            RefreshSettingsLogSize();
        }
        finally
        {
            suppressSettingsUi = false;
        }
    }
}
