using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

public partial class MainForm
{
    // ===== 玩家版现代壳层（左侧可折叠导航栏 + 顶部信息栏 + 底部状态胶囊栏） =====
    NavRail navRail;
    Panel playerRightHost;
    Panel headerBar;
    Panel pageHost;
    StatusPillBar statusPillBar;

    Label lblHeaderTitle;
    Label lblHeaderSub;
    Label lblToolPageTip;

    Panel joinNavPage, crossNavPage, timerNavPage;

    Dictionary<string, Control> navPages = new Dictionary<string, Control>(StringComparer.Ordinal);
    string activeNavKey = "";
    bool navRailCollapsed = false;

    void BuildPlayerShell()
    {
        // 底部交互状态胶囊栏
        statusPillBar = new StatusPillBar();
        statusPillBar.Dock = DockStyle.Bottom;
        statusPillBar.Pills.Add(new StatusPillBar.Pill
        {
            Key = "cross", Text = "准星：关闭", DotColor = ThemePalette.TextMuted,
            Tooltip = "单击直接开启/关闭外置屏幕准星",
            OnClick = delegate { ToggleCross(); }
        });
        statusPillBar.Pills.Add(new StatusPillBar.Pill
        {
            Key = "join", Text = "挤服：待命", DotColor = ThemePalette.TextMuted,
            Tooltip = "单击跳转到「挤服助手」",
            OnClick = delegate { SwitchNavPage("join"); }
        });
        statusPillBar.Pills.Add(new StatusPillBar.Pill
        {
            Key = "timer", Text = "倒计时：18:00", DotColor = ThemePalette.TextMuted,
            Tooltip = "单击跳转到「倒计时」",
            OnClick = delegate { SwitchNavPage("timer"); }
        });
        statusPillBar.Pills.Add(new StatusPillBar.Pill
        {
            Key = "hk", Text = "热键：正常", DotColor = ThemePalette.Success,
            Tooltip = "单击打开「热键设置」查看或修改快捷键",
            OnClick = delegate { ShowHotkeyDialog(); }
        });
        statusPillBar.Pills.Add(new StatusPillBar.Pill
        {
            Key = "theme", Text = "护眼浅色", DotColor = ThemePalette.Accent, RightAlign = true,
            Tooltip = "单击循环切换主题：护眼浅色 / 跟随系统 / 夜间深色",
            OnClick = delegate { themeMode = (themeMode + 1) % 3; ApplyTheme(); SaveSettings(); }
        });

        // 左侧可折叠导航栏
        navRail = new NavRail();
        navRail.Dock = DockStyle.Left;
        navRail.SelectionChanged += delegate(string key) { SwitchNavPage(key); };
        navRail.CollapseToggled += delegate
        {
            navRailCollapsed = navRail.Collapsed;
            SaveSettings();
            LayoutHeaderBar();
            SyncSettingsPageUi();
        };

        // 右侧主工作区
        playerRightHost = new Panel();
        playerRightHost.Dock = DockStyle.Fill;
        playerRightHost.BackColor = ThemePalette.Canvas;

        // 顶部精简信息栏（当前页面标题 + 副标题）
        headerBar = new Panel();
        headerBar.Dock = DockStyle.Top;
        headerBar.Height = 50;
        headerBar.BackColor = ThemePalette.Surface;
        headerBar.Paint += delegate(object snd, PaintEventArgs pe)
        {
            using (var pen = new Pen(ThemePalette.BorderSubtle, 1f))
                pe.Graphics.DrawLine(pen, 0, headerBar.Height - 1, headerBar.Width, headerBar.Height - 1);
        };

        lblHeaderTitle = new Label();
        lblHeaderTitle.Font = TrackFont("Microsoft YaHei UI", 11.5f, FontStyle.Bold);
        lblHeaderTitle.ForeColor = ThemePalette.TextPrimary;
        lblHeaderTitle.BackColor = Color.Transparent;
        lblHeaderTitle.AutoEllipsis = true;
        lblHeaderTitle.UseMnemonic = false;
        lblHeaderTitle.SetBounds(16, 6, 420, 22);
        headerBar.Controls.Add(lblHeaderTitle);

        lblHeaderSub = new Label();
        lblHeaderSub.Font = TrackFont("Microsoft YaHei UI", 8.2f, FontStyle.Regular);
        lblHeaderSub.ForeColor = ThemePalette.TextMuted;
        lblHeaderSub.BackColor = Color.Transparent;
        lblHeaderSub.AutoEllipsis = true;
        lblHeaderSub.UseMnemonic = false;
        lblHeaderSub.SetBounds(16, 28, 420, 16);
        headerBar.Controls.Add(lblHeaderSub);

        headerBar.Resize += delegate { LayoutHeaderBar(); };

        pageHost = new Panel();
        pageHost.Dock = DockStyle.Fill;
        pageHost.BackColor = ThemePalette.Surface;

        playerRightHost.Controls.Add(pageHost);
        playerRightHost.Controls.Add(headerBar);

        Controls.Add(playerRightHost);
        Controls.Add(navRail);
        Controls.Add(statusPillBar);

        // 注册玩家版页面
        BuildPlayerToolNavPages();

        customPage = BuildCustomPage();
        RegisterNavPage("custom", "自定义文案", "文本工具", NavIconKind.Bookmark, cTitles.Length, customPage);

        settingsPage = BuildSettingsPage();
        RegisterNavPage("settings", "设置", "BOTTOM", NavIconKind.Settings, 0, settingsPage);

        aboutPage = BuildAboutPage();
        RegisterNavPage("about", "关于与更新", "BOTTOM", NavIconKind.Info, 0, aboutPage);

        LayoutHeaderBar();
        SwitchNavPage("join");
    }

    void RegisterNavPage(string key, string title, string group, NavIconKind icon, int count, Control pageCtrl)
    {
        if (pageCtrl == null) return;
        pageCtrl.Dock = DockStyle.Fill;
        pageCtrl.Visible = false;
        navPages[key] = pageCtrl;
        if (pageHost != null && pageCtrl.Parent != pageHost)
            pageHost.Controls.Add(pageCtrl);

        NavItem existing = navRail.FindItem(key);
        if (existing == null)
        {
            navRail.Items.Add(new NavItem
            {
                Key = key,
                Text = title,
                Group = group,
                Icon = icon,
                BadgeCount = count
            });
        }
        else
        {
            existing.Text = title;
            existing.BadgeCount = count;
        }
        navRail.Invalidate();
    }

    void BuildPlayerToolNavPages()
    {
        joinNavPage = new Panel();
        joinNavPage.BackColor = ThemePalette.Surface;
        AddPlayerToolDivider(joinNavPage);
        CreateJoinPanel(joinNavPage, 50);

        crossNavPage = new Panel();
        crossNavPage.BackColor = ThemePalette.Surface;
        AddPlayerToolDivider(crossNavPage);
        CreateCrossPanel(crossNavPage, 50);

        timerNavPage = new Panel();
        timerNavPage.BackColor = ThemePalette.Surface;
        AddPlayerToolDivider(timerNavPage);
        CreateTimerPanel(timerNavPage, 50);

        lblToolPageTip = new Label();
        lblToolPageTip.Font = TrackFont("Microsoft YaHei UI", 9f, FontStyle.Regular);
        lblToolPageTip.ForeColor = ThemePalette.TextSecondary;
        lblToolPageTip.AutoEllipsis = true;
        lblToolPageTip.UseMnemonic = false;
        lblToolPageTip.SetBounds(12, 15, 360, 22);
        joinNavPage.Controls.Add(lblToolPageTip);
        CreateToolUtilityButtons(joinNavPage);

        joinNavPage.Resize += delegate { LayoutToolBar(); };
        crossNavPage.Resize += delegate { LayoutToolBar(); };
        timerNavPage.Resize += delegate { LayoutToolBar(); };

        RegisterNavPage("join", "挤服助手", "实用工具", NavIconKind.Server, 0, joinNavPage);
        RegisterNavPage("cross", "屏幕准星", "实用工具", NavIconKind.Crosshair, 0, crossNavPage);
        RegisterNavPage("timer", "倒计时", "实用工具", NavIconKind.Timer, 0, timerNavPage);

        LoadFavs();
        FillSrvList();
        ApplyModeUi();
        UpdateHotkeyLabels();
    }

    void AddPlayerToolDivider(Panel host)
    {
        var div = new Panel();
        div.SetBounds(8, 46, Math.Max(100, host.ClientSize.Width - 16), 1);
        div.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        div.BackColor = Color.FromArgb(226, 232, 240);
        host.Controls.Add(div);
    }

    void MountPlayerToolStrip(Panel targetPage)
    {
        if (targetPage == null || btnHotkey == null) return;
        if (lblToolPageTip != null && lblToolPageTip.Parent != targetPage) targetPage.Controls.Add(lblToolPageTip);
        if (btnHotkeyOff != null && btnHotkeyOff.Parent != targetPage) targetPage.Controls.Add(btnHotkeyOff);
        if (bLog != null && bLog.Parent != targetPage) targetPage.Controls.Add(bLog);
        if (btnHotkey != null && btnHotkey.Parent != targetPage) targetPage.Controls.Add(btnHotkey);

        if (lblToolPageTip != null)
        {
            if (targetPage == joinNavPage)
                lblToolPageTip.Text = "A2S 状态探测与自动挤服 · 支持手动档 / 单一 IP / 收藏轮询（OCR 自动停）";
            else if (targetPage == crossNavPage)
                lblToolPageTip.Text = "桌面置顶透明准星 · 支持矢量调节与导入/粘贴自定义准星图";
            else if (targetPage == timerNavPage)
                lblToolPageTip.Text = "透明点击穿透倒计时 · 支持多方案管理与时间链变色 / 闪烁 / 标注";
        }
        LayoutToolBar();
    }

    void SwitchNavPage(string key)
    {
        if (string.IsNullOrEmpty(key) || !navPages.ContainsKey(key)) return;
        activeNavKey = key;
        if (navRail != null) navRail.SelectKey(key, false);

        foreach (var kv in navPages)
            kv.Value.Visible = (kv.Key == key);

        Control cur = navPages[key];
        cur.BringToFront();

        if (key == "join") { MountPlayerToolStrip(joinNavPage); LayoutJoin(); }
        else if (key == "cross") { MountPlayerToolStrip(crossNavPage); LayoutCrossPanel(); if (crossPreview != null) crossPreview.Invalidate(); }
        else if (key == "timer") { MountPlayerToolStrip(timerNavPage); LayoutTimer(); }
        else if (key == "settings") { SyncSettingsPageUi(); LayoutSettingsPage(); }

        UpdateHeaderBar();
    }

    void UpdateHeaderBar()
    {
        if (lblHeaderTitle == null || lblHeaderSub == null) return;
        NavItem it = (navRail != null) ? navRail.FindItem(activeNavKey) : null;
        string title = (it != null) ? it.Text : "RX 工具箱";
        lblHeaderTitle.Text = title;

        if (activeNavKey == "join")
            lblHeaderSub.Text = "已收藏 " + favList.Length + " 个服务器 · 支持 A2S 探测与自动挤服";
        else if (activeNavKey == "cross")
            lblHeaderSub.Text = "当前样式：" + Overlay.Styles[overlay.StyleIndex] + " · " + Overlay.ColorNames[overlay.ColorIndex];
        else if (activeNavKey == "timer")
            lblHeaderSub.Text = timerRunning ? (timerPaused ? "倒计时已暂停" : "倒计时运行中") : "常用倒计时方案与时间轴";
        else if (activeNavKey == "custom")
            lblHeaderSub.Text = "共 " + cTitles.Length + " 张卡片 · 单击卡片自动复制到剪贴板";
        else if (activeNavKey == "settings")
            lblHeaderSub.Text = "外观、侧边栏、全局热键、更新与日志";
        else if (activeNavKey == "about")
            lblHeaderSub.Text = APP_VERSION + " · 构建 " + APP_BUILD_DATE + " · 单文件绿色版";

        LayoutHeaderBar();
    }

    void LayoutHeaderBar()
    {
        if (headerBar == null) return;
        int w = headerBar.ClientSize.Width;
        if (w < 200) return;

        int titleMax = Math.Max(160, w - 32);
        if (lblHeaderTitle != null) lblHeaderTitle.Width = titleMax;
        if (lblHeaderSub != null) lblHeaderSub.Width = titleMax;
    }

    void UpdateNavBadges()
    {
        if (navRail == null) return;
        NavItem custIt = navRail.FindItem("custom");
        if (custIt != null) custIt.BadgeCount = cTitles.Length;
        navRail.Invalidate();
        UpdateHeaderBar();
    }

    void UpdatePlayerStatusPills()
    {
        if (statusPillBar == null) return;
        var pCross = statusPillBar.Find("cross");
        if (pCross != null)
        {
            pCross.Text = overlay.Visible ? ("准星：" + Overlay.Styles[overlay.StyleIndex]) : "准星：关闭";
            pCross.DotColor = overlay.Visible ? ThemePalette.Success : ThemePalette.TextMuted;
        }
        var pJoin = statusPillBar.Find("join");
        if (pJoin != null)
        {
            if (autoOn)
            {
                pJoin.Text = typingOn ? "挤服：粘贴连接中" : "挤服：回车连点中";
                pJoin.DotColor = ThemePalette.Danger;
            }
            else
            {
                int m = JoinModeIdx;
                string mn = (m == 0) ? "手动档" : (m == 2 ? "收藏轮询" : "单一IP");
                pJoin.Text = "挤服：" + mn + " · " + IntervalMs + "ms";
                pJoin.DotColor = ThemePalette.TextMuted;
            }
        }
        var pTimer = statusPillBar.Find("timer");
        if (pTimer != null)
        {
            string mm = CountdownOverlay.FormatMmSs(timerRemainSec);
            pTimer.Text = timerRunning ? (timerPaused ? ("倒计时：" + mm + " (暂停)") : ("倒计时：" + mm)) : ("倒计时：" + mm);
            pTimer.DotColor = timerRunning ? (timerPaused ? ThemePalette.Warning : ThemePalette.Accent) : ThemePalette.TextMuted;
        }
        var pHk = statusPillBar.Find("hk");
        if (pHk != null)
        {
            if (hotkeysOff)
            {
                pHk.Text = "热键：已关闭";
                pHk.DotColor = ThemePalette.Warning;
            }
            else if (HotkeysOk())
            {
                pHk.Text = "热键：正常";
                pHk.DotColor = ThemePalette.Success;
            }
            else
            {
                pHk.Text = "热键：部分冲突";
                pHk.DotColor = ThemePalette.Danger;
            }
        }
        var pTheme = statusPillBar.Find("theme");
        if (pTheme != null)
        {
            if (themeMode == 0) pTheme.Text = IsDark ? "跟随系统(暗)" : "跟随系统(亮)";
            else if (themeMode == 1) pTheme.Text = "夜间深色";
            else pTheme.Text = "护眼浅色";
            pTheme.DotColor = ThemePalette.Accent;
        }
        statusPillBar.Invalidate();
    }
}
