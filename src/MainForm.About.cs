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
    void LayoutAboutTab()
    {
        if (tabs == null || spacerPage == null || aboutPage == null) return;
        try
        {
            int w = tabs.ClientSize.Width;
            if (w < 260) return;
            int target = w - 6;                       // "关于"右边要贴到这里
            spacerPage.Text = "";
            tabs.PerformLayout();

            // 二分找空格数：让"关于"的右边界尽量贴近 target
            int lo = 0, hi = 400, best = 0;
            for (int iter = 0; iter < 9; iter++)
            {
                int mid = (lo + hi) / 2;
                spacerPage.Text = mid == 0 ? "" : new string(' ', mid);
                tabs.PerformLayout();
                int right = tabs.GetTabRect(tabs.TabPages.IndexOf(aboutPage)).Right;
                if (right <= target) { best = mid; lo = mid + 1; }
                else hi = mid - 1;
                if (lo > hi) break;
            }
            spacerPage.Text = best == 0 ? "" : new string(' ', best);
            tabs.PerformLayout();
        }
        catch { }
    }

    // ---------------- 更新日志 ----------------
    // 格式：版本|日期|第一条~第二条~…（用 ~ 分行）
    static readonly string[] CHANGELOG = new string[]
    {
        "1.8.0|2026-11-17|" +
            "调整主界面结构：将「挤服助手」「屏幕准星」「倒计时」独立为主标签页直接切换，右上角常驻「关闭热键」「查看日志」「热键设置」工具栏~" +
            "自定义页支持按标题与正文关键词实时筛选卡片（按 Esc 清空），并保留快捷编辑与剪贴板一键复制~" +
            "完善独立更新通道：支持软件内检查更新、SHA256 校验与纯 C# 原地替换及失败自动回滚保护",
        "1.7.5|2026-11-17|" +
            "新增「倒计时（时间链）」功能：纯数字点击穿透悬浮窗、多方案复制/新建与自定义变色 / 低中高频闪烁 / 描边文字标注事件链~" +
            "扩展全局热键至 8 项：新增 F9 倒计时开始/重置、Ctrl+F9 暂停/继续、F10 悬浮窗显隐，「退出工具」默认顺延为 Ctrl+F11~" +
            "新增「护眼浅色（默认）/ 跟随系统 / 夜间深色」三档主题（全弹窗适配），自绘滚动条修复切换分辨率黑块问题并精简底部状态栏",
        "1.7.3|2026-11-15|" +
            "优化挤服识别：整屏 OCR 不再缩放图像；未识别到内容时单独检测加载画面左侧的“正在加载插件”~" +
            "日志会在场景变化时记录 OCR 原文，便于排查",
        "1.7.1|2026-11-15|" +
            "新增“关闭热键”功能：可通过按钮或 Ctrl+F10 一键将功能热键暂时交还系统，下次启动自动恢复",
        "1.5.0|2026-11-15|" +
            "挤服助手支持手动档 / 单一 IP / 收藏轮询三模式与 A2S 实时人数探测；OCR 移至后台线程不阻塞界面；支持外置准星导入与粘贴自定义图片",
    };

    static string StripWarnChangelogLine(string raw, out bool isWarn)
    {
        string s = (raw ?? "").TrimStart();
        if (s.StartsWith("!!", StringComparison.Ordinal))
        {
            isWarn = true;
            return s.Substring(2).TrimStart();
        }
        isWarn = s.StartsWith("【重要提醒", StringComparison.Ordinal) || s.StartsWith("⚠", StringComparison.Ordinal);
        return raw ?? "";
    }

    int AddLogBlock(Panel host, int y, string ver, string date, string[] lines, bool highlight, int w)
    {
        Font fVer = TrackFont("Microsoft YaHei UI", 10f, FontStyle.Bold);
        Font fLine = TrackFont("Microsoft YaHei UI", 9f, FontStyle.Regular);
        Font fWarn = TrackFont("Microsoft YaHei UI", 9f, FontStyle.Bold);

        var head = new Label();
        head.Text = ver + (string.IsNullOrEmpty(date) ? "" : "    " + date);
        head.Font = fVer;
        head.ForeColor = highlight ? Color.FromArgb(20, 130, 70) : Color.FromArgb(40, 74, 120);
        head.SetBounds(10, y, w - 20, 22);
        head.UseMnemonic = false;
        host.Controls.Add(head);
        y += 24;

        for (int i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrEmpty(lines[i])) continue;
            bool isWarn;
            string lineText = StripWarnChangelogLine(lines[i], out isWarn);
            if (isWarn)
            {
                int cardW = Math.Max(260, w - 28);
                int textW = Math.Max(200, cardW - 22);
                Size sz = TextRenderer.MeasureText(lineText, fWarn, new Size(textW, 4000),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                int cardH = Math.Max(30, sz.Height + 12);

                var card = new Panel();
                card.Tag = "rx-warn";
                card.BackColor = Color.FromArgb(255, 246, 229);
                card.SetBounds(12, y, cardW, cardH);

                var bar = new Panel();
                bar.Tag = "rx-warn-bar";
                bar.BackColor = Color.FromArgb(217, 119, 6);
                bar.Dock = DockStyle.Left;
                bar.Width = 4;
                card.Controls.Add(bar);

                var lnWarn = new Label();
                lnWarn.Text = lineText;
                lnWarn.Font = fWarn;
                lnWarn.ForeColor = Color.FromArgb(166, 56, 12);
                lnWarn.BackColor = Color.Transparent;
                lnWarn.SetBounds(10, 6, textW + 4, cardH - 10);
                lnWarn.UseMnemonic = false;
                card.Controls.Add(lnWarn);

                host.Controls.Add(card);
                y += cardH + 6;
                continue;
            }

            var ln = new Label();
            ln.Text = "· " + lineText;
            ln.Font = fLine;
            ln.ForeColor = highlight ? Color.FromArgb(30, 110, 60) : Color.FromArgb(92, 102, 116);
            Size nsz = TextRenderer.MeasureText(ln.Text, fLine, new Size(w - 44, 4000),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            ln.SetBounds(14, y, w - 34, Math.Max(20, nsz.Height + 4));
            ln.UseMnemonic = false;
            host.Controls.Add(ln);
            y += ln.Height + 2;
        }
        y += 8;
        return y;
    }

    void RenderChangelog()
    {
        if (changelogHost == null) return;
        try
        {
            changelogHost.SuspendLayout();
            ClearAndDisposeControls(changelogHost);
            int w = Math.Max(320, changelogHost.ClientSize.Width - (changelogHost.VerticalScroll.Visible ? 20 : 4));
            int y = 8;

            // 远端有更新时，把它的说明放在最上面
            if (!string.IsNullOrEmpty(updLatestVersion) && CompareVersion(updLatestVersion, APP_VERSION_NUM) > 0)
            {
                string[] nl = string.IsNullOrEmpty(updLatestNotes)
                    ? new string[] { "（对方没写更新说明）" }
                    : updLatestNotes.Replace("\r\n", "\n").Split('\n');
                y = AddLogBlock(changelogHost, y, "v" + updLatestVersion, "新版本，可下载", nl, true, w);
            }

            for (int i = 0; i < CHANGELOG.Length; i++)
            {
                string[] parts = CHANGELOG[i].Split('|');
                if (parts.Length < 3) continue;
                string ver = parts[0], date = parts[1];
                string[] lines = parts[2].Split('~');
                bool cur = (CompareVersion(ver, APP_VERSION_NUM) == 0);
                y = AddLogBlock(changelogHost, y, "v" + ver, date + (cur ? "    当前版本" : ""), lines, false, w);
            }

            changelogHost.AutoScrollMinSize = new Size(w + 16, y + 8);
            ThemeControlTree(changelogHost);
            changelogHost.ResumeLayout();
        }
        catch { }
    }

    // ---------------- 关于 / 更新 ----------------
    int AddAboutSection(Panel host, string text, int x, int y, int w)
    {
        var lbl = new Label();
        lbl.Text = text;
        lbl.Font = TrackFont("Microsoft YaHei UI", 11f, FontStyle.Bold);
        lbl.ForeColor = Color.FromArgb(28, 60, 110);
        lbl.SetBounds(x, y, w, 26);
        lbl.UseMnemonic = false;
        host.Controls.Add(lbl);

        var line = new Panel();
        line.BackColor = Color.FromArgb(226, 234, 245);
        line.SetBounds(x, y + 27, w, 1);
        host.Controls.Add(line);
        return y + 38;
    }

    Label AddAboutText(Panel host, string text, int x, int y, int w, Font f, Color c, int h)
    {
        var lbl = new Label();
        lbl.Text = text;
        lbl.Font = f;
        lbl.ForeColor = c;
        lbl.SetBounds(x, y, w, h);
        lbl.UseMnemonic = false;
        host.Controls.Add(lbl);
        return lbl;
    }

    int AddAboutPara(Panel host, string text, int x, int y, int w, Font f, Color c)
    {
        Size sz = TextRenderer.MeasureText(text, f, new Size(w, 4000),
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        var lbl = new Label();
        lbl.Text = text;
        lbl.Font = f;
        lbl.ForeColor = c;
        lbl.SetBounds(x, y, w, Math.Max(22, sz.Height + 8));
        lbl.UseMnemonic = false;
        host.Controls.Add(lbl);
        return y + lbl.Height + 4;
    }

    TabPage BuildAboutPage()
    {
        var page = new TabPage("关于");
        page.BackColor = Color.White;

        var host = new Panel();
        host.Dock = DockStyle.Fill;
        host.AutoScroll = true;
        host.BackColor = Color.White;
        page.Controls.Add(host);

        Font fBody = TrackFont("Microsoft YaHei UI", 9.5f, FontStyle.Regular);
        Font fSmall = TrackFont("Microsoft YaHei UI", 9f, FontStyle.Regular);
        Font fHint = TrackFont("Microsoft YaHei UI", 9f, FontStyle.Italic);
        Color cBody = Color.FromArgb(58, 68, 84);
        Color cHint = Color.FromArgb(150, 158, 170);

        int x = 20, w = 700, y = 16;

        var t1 = new Label();
        t1.Text = "RX 工具箱";
        t1.Font = TrackFont("Microsoft YaHei UI", 17f, FontStyle.Bold);
        t1.ForeColor = Color.FromArgb(26, 58, 104);
        t1.SetBounds(x, y, w, 38);
        t1.UseMnemonic = false;
        host.Controls.Add(t1);
        y += 42;

        var t2 = new Label();
        t2.Text = APP_VERSION + " · 构建 " + APP_BUILD_DATE + " · 单文件绿色版，免安装";
        t2.Font = fBody;
        t2.ForeColor = Color.FromArgb(112, 124, 142);
        t2.SetBounds(x, y, w, 24);
        t2.UseMnemonic = false;
        host.Controls.Add(t2);
        y += 34;

        // ---------- 程序更新 ----------
        y = AddAboutSection(host, "程序更新", x, y, w);

        lblUpdState = AddAboutText(host, "尚未检查", x + 2, y, w - 4, fBody, cBody, 24);
        y += 30;

        btnUpdCheck = new Button();
        btnUpdCheck.Text = "检查更新";
        btnUpdCheck.FlatStyle = FlatStyle.Flat;
        btnUpdCheck.FlatAppearance.BorderColor = Color.FromArgb(180, 205, 238);
        btnUpdCheck.SetBounds(x + 2, y, 128, 34);
        btnUpdCheck.Click += delegate { CheckUpdate(true); };
        host.Controls.Add(btnUpdCheck);

        btnUpdOpen = new Button();
        btnUpdOpen.Text = "立即更新";
        btnUpdOpen.FlatStyle = FlatStyle.Flat;
        btnUpdOpen.FlatAppearance.BorderColor = Color.FromArgb(180, 205, 238);
        btnUpdOpen.SetBounds(x + 140, y, 128, 34);
        btnUpdOpen.Enabled = false;
        btnUpdOpen.Click += delegate { StartSelfUpdate(); };
        host.Controls.Add(btnUpdOpen);

        var btnUpdHelp = new Button();
        btnUpdHelp.Text = "?";
        btnUpdHelp.Font = TrackFont("Microsoft YaHei UI", 11f, FontStyle.Bold);
        btnUpdHelp.FlatStyle = FlatStyle.Flat;
        btnUpdHelp.FlatAppearance.BorderColor = Color.FromArgb(180, 205, 238);
        btnUpdHelp.SetBounds(x + 274, y, 32, 34);
        btnUpdHelp.Click += delegate { ShowUpdateHelp(); };
        var tipHelp = new ToolTip(uiComponents);
        tipHelp.SetToolTip(btnUpdHelp, "更新下载不保证稳定，点这里看说明");
        host.Controls.Add(btnUpdHelp);

        chkAutoUpdate = new CheckBox();
        chkAutoUpdate.Text = "启动时自动检查更新";
        chkAutoUpdate.Font = fSmall;
        chkAutoUpdate.SetBounds(x + 318, y + 6, 190, 24);
        chkAutoUpdate.Checked = autoUpdateOn;
        chkAutoUpdate.CheckedChanged += delegate
        {
            autoUpdateOn = chkAutoUpdate.Checked;
            SaveSettings();
        };
        host.Controls.Add(chkAutoUpdate);
        var btnReleases = new Button();
        btnReleases.Text = "GitHub 发布页";
        btnReleases.FlatStyle = FlatStyle.Flat;
        btnReleases.FlatAppearance.BorderColor = Color.FromArgb(180, 205, 238);
        btnReleases.SetBounds(x + 518, y, 168, 34);
        btnReleases.Click += delegate { TryOpenHttpUrl("https://github.com/Dauskwn/rx-tools/releases"); };
        host.Controls.Add(btnReleases);
        y += 46;

        AddAboutText(host, "更新日志", x + 2, y, 120, fSmall, Color.FromArgb(110, 122, 140), 22);
        y += 24;

        changelogHost = new Panel();
        changelogHost.BackColor = Color.FromArgb(248, 250, 253);
        changelogHost.BorderStyle = BorderStyle.FixedSingle;
        changelogHost.AutoScroll = true;
        changelogHost.SetBounds(x + 2, y, w - 10, 196);
        changelogHost.Resize += delegate { RenderChangelog(); };
        host.Controls.Add(changelogHost);
        RenderChangelog();
        y += 208;      // 与面板高度 196 对齐，别把下方标题压住


        // ---------- 声明 ----------
        y = AddAboutSection(host, "声明", x, y, w);

        AddAboutText(host, "使用范围", x + 2, y, w - 4, fSmall, Color.FromArgb(110, 122, 140), 20);
        y += 22;
        y = AddAboutPara(host, "适用于 Garry's Mod 游戏玩家日常游玩与对局辅助（倒计时内置标准预设适配 Breach 玩法，支持自由修改与新建）。",
            x + 2, y, w - 14, fBody, cBody);

        AddAboutText(host, "用途", x + 2, y, w - 4, fSmall, Color.FromArgb(110, 122, 140), 20);
        y += 22;
        y = AddAboutPara(host, "提供服务器 A2S 状态探测与自动挤服（支持系统 OCR 识别进服）、桌面置顶透明准星、点击穿透对局倒计时与自定义常用文本快捷复制等功能。",
            x + 2, y, w - 14, fBody, cBody);

        AddAboutText(host, "授权", x + 2, y, w - 4, fSmall, Color.FromArgb(110, 122, 140), 20);
        y += 22;
        y = AddAboutPara(host, "完全免费，仅供学习交流使用。允许二次分发与修改（保留出处即可）；禁止商用、倒卖或以此盈利。",
            x + 2, y, w - 14, fBody, cBody);

        AddAboutText(host, "开源", x + 2, y, w - 4, fSmall, Color.FromArgb(110, 122, 140), 20);
        y += 22;
        y = AddAboutPara(host, "本程序以 MIT 许可开源：源码公开，可自行编译核对，也欢迎提问题、提改进。",
            x + 2, y, w - 14, fBody, cBody);

        var lnkRepo = new LinkLabel();
        const string RepoUrl = "https://github.com/Dauskwn/rx-tools";
        lnkRepo.Text = RepoUrl;
        lnkRepo.Font = fBody;
        lnkRepo.LinkColor = Color.FromArgb(38, 100, 190);
        lnkRepo.ActiveLinkColor = Color.FromArgb(20, 70, 150);
        lnkRepo.VisitedLinkColor = Color.FromArgb(38, 100, 190);
        lnkRepo.LinkBehavior = LinkBehavior.HoverUnderline;
        lnkRepo.AutoSize = true;
        lnkRepo.SetBounds(x + 2, y + 4, 460, 22);
        lnkRepo.LinkClicked += delegate
        {
            try { System.Diagnostics.Process.Start(RepoUrl); }
            catch { }
        };
        host.Controls.Add(lnkRepo);
        y += 30;

        AddAboutText(host, "免责", x + 2, y, w - 4, fSmall, Color.FromArgb(110, 122, 140), 20);
        y += 22;
        y = AddAboutPara(host, "因使用本工具产生的任何直接或间接后果，作者不承担任何责任。游戏内行为请遵守各服务器规则。\n若本工具存在侵权、违反服务器规则的行为，请联系作者进行修改或删除处理。",
            x + 2, y, w - 14, fBody, cBody);
        y += 10;
        // ---------- 署名与鸣谢 ----------
        y = AddAboutSection(host, "署名与鸣谢", x, y, w);
        AddAboutText(host, "作者：暮嵐拂晓", x + 2, y, w - 4, fBody, cBody, 22);
        y += 24;
        AddAboutText(host, "感谢所有参与测试并提出改进建议的玩家朋友", x + 2, y, w - 4, fHint, cHint, 22);
        y += 26;


        // ---------- 反馈 ----------
        y = AddAboutSection(host, "反馈", x, y, w);
        AddAboutText(host, "遇到问题或想提新功能建议，欢迎前往 GitHub 提交 Issue：", x + 2, y, w - 4, fBody, cBody, 24);
        y += 24;

        var lnkIssue = new LinkLabel();
        const string IssueUrl = "https://github.com/Dauskwn/rx-tools/issues";
        lnkIssue.Text = IssueUrl;
        lnkIssue.Font = fBody;
        lnkIssue.LinkColor = Color.FromArgb(38, 100, 190);
        lnkIssue.ActiveLinkColor = Color.FromArgb(20, 70, 150);
        lnkIssue.VisitedLinkColor = Color.FromArgb(38, 100, 190);
        lnkIssue.LinkBehavior = LinkBehavior.HoverUnderline;
        lnkIssue.AutoSize = true;
        lnkIssue.SetBounds(x + 2, y, 460, 22);
        lnkIssue.LinkClicked += delegate
        {
            try { System.Diagnostics.Process.Start(IssueUrl); }
            catch { }
        };
        host.Controls.Add(lnkIssue);
        y += 40;

        host.AutoScrollMinSize = new Size(w + 40, y + 20);
        return page;
    }

    void ShowUpdateHelp()
    {
        string linkUrl = !string.IsNullOrEmpty(updLatestUrl) ? updLatestUrl : "";
        string linkInfo = updateUrl;
        var f = new Form();
        try
        {
            f.Text = "关于自动更新";
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.StartPosition = FormStartPosition.CenterParent;
            f.MinimizeBox = false;
            f.MaximizeBox = false;
            f.ClientSize = new Size(560, 340);
            f.Font = TrackFont("Microsoft YaHei UI", 9.5f, FontStyle.Regular);

            var tip = new Label();
            tip.SetBounds(14, 12, 532, 120);
            tip.ForeColor = Color.FromArgb(70, 80, 96);
            tip.Text = "更新源为个人自建的 Cloudflare 地址，部分地区或时段可能无法访问。\r\n\r\n" +
                       "自动更新流程：下载 → 校验 → 替换 → 重启。若替换失败会自动保留旧版本，\r\n" +
                       "不会导致程序无法启动。\r\n\r\n" +
                       "如多次尝试仍失败，可使用下方地址手动下载，或前往 GitHub Releases 获取最新版。";
            f.Controls.Add(tip);

            var lab1 = new Label();
            lab1.Text = "手动下载地址（可复制）：";
            lab1.SetBounds(14, 140, 300, 20);
            lab1.ForeColor = Color.FromArgb(110, 122, 140);
            f.Controls.Add(lab1);

            var box = new TextBox();
            box.Multiline = true;
            box.ReadOnly = true;
            box.ScrollBars = ScrollBars.Vertical;
            box.SetBounds(14, 162, 532, 76);
            box.Text = linkUrl.Length > 0
                ? (linkUrl + "\r\n\r\n更新信息：" + linkInfo)
                : ("尚未获取到新版本信息，请先点击一次“检查更新”。\r\n更新信息地址：" + linkInfo);
            f.Controls.Add(box);

            var lab2 = new Label();
            lab2.Text = "当前版本：" + APP_VERSION + "（" + APP_VERSION_NUM + "）　　开源发布页：github.com/Dauskwn/rx-tools/releases";
            lab2.SetBounds(14, 246, 520, 20);
            lab2.ForeColor = Color.FromArgb(110, 122, 140);
            f.Controls.Add(lab2);

            var bOpen = new Button();
            bOpen.Text = "打开链接";
            bOpen.SetBounds(14, 282, 110, 32);
            bOpen.Click += delegate
            {
                string u = linkUrl.Length > 0 ? linkUrl : linkInfo;
                TryOpenHttpUrl(u);
            };
            f.Controls.Add(bOpen);

            var bCopy = new Button();
            bCopy.Text = "复制地址";
            bCopy.SetBounds(132, 282, 110, 32);
            bCopy.Click += delegate
            {
                try { Clipboard.SetText(linkUrl.Length > 0 ? linkUrl : linkInfo); }
                catch { }
            };
            f.Controls.Add(bCopy);

            var bClose = new Button();
            bClose.Text = "关闭";
            bClose.SetBounds(432, 282, 110, 32);
            bClose.DialogResult = DialogResult.OK;
            f.Controls.Add(bClose);

            f.AcceptButton = bClose;
            f.CancelButton = bClose;
            ThemeDialog(f);
            f.ShowDialog(this);
        }
        finally
        {
            try { f.Dispose(); } catch { }
        }
    }

    void SetUpdState(string text, Color c)
    {
        if (lblUpdState == null) return;
        lblUpdState.Text = text;
        SetDynamicLabelColor(lblUpdState, c);
    }

    static string HttpGetText(string url, int timeoutMs)
    {
        try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }
        var req = (HttpWebRequest)WebRequest.Create(url);
        req.Timeout = timeoutMs;
        req.ReadWriteTimeout = timeoutMs;
        req.UserAgent = "RXToolbox/" + APP_VERSION_NUM;
        req.Proxy = null;
        using (var resp = (HttpWebResponse)req.GetResponse())
        using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            return sr.ReadToEnd();
    }

    static string JsonField(string json, string key)
    {
        if (string.IsNullOrEmpty(json)) return "";
        Match m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        if (!m.Success) return "";
        return JsonUnesc(m.Groups[1].Value);
    }

    static int CompareVersion(string a, string b)
    {
        string[] pa = (a ?? "").Split('.');
        string[] pb = (b ?? "").Split('.');
        for (int i = 0; i < 3; i++)
        {
            int va = 0, vb = 0;
            if (i < pa.Length) int.TryParse(Regex.Replace(pa[i], "[^0-9]", ""), out va);
            if (i < pb.Length) int.TryParse(Regex.Replace(pb[i], "[^0-9]", ""), out vb);
            if (va != vb) return va > vb ? 1 : -1;
        }
        return 0;
    }

    // ================= 自动更新（同目录下载 → 校验 → 原地改名替换 → 重启握手） =================
    // 不经过 %TEMP% 与 PowerShell 脚本，直接同目录 .upd 下载校验并原地改名替换
    // 同目录的 settings.ini / servers.txt / custom.txt 原样保留
    static bool IsHttpUrl(string u)
    {
        if (string.IsNullOrEmpty(u)) return false;
        return u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || u.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
    }

    static bool DownloadAndVerify(string url, string sha, string dest)
    {
        if (string.IsNullOrEmpty(sha) || sha.Length != 64) return false;
        try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072 | (SecurityProtocolType)12288; } catch { }
        using (var wc = new WebClient())
        {
            wc.Headers.Add("User-Agent", "RXToolbox-Updater");
            wc.DownloadFile(url, dest);
        }
        string got = Sha256File(dest);
        if (string.Equals(got, sha, StringComparison.OrdinalIgnoreCase)) return true;
        TryDeleteFile(dest, 1);
        return false;
    }

    static bool TryOpenHttpUrl(string u)
    {
        if (!IsHttpUrl(u)) return false;
        try
        {
            using (var p = System.Diagnostics.Process.Start(u)) { }
            return true;
        }
        catch { return false; }
    }

    static string Sha256File(string path)
    {
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var sha = new System.Security.Cryptography.SHA256Managed())
        {
            byte[] hash = sha.ComputeHash(fs);
            var sb = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }
    }

    public static bool CanWriteDir(string dir)
    {
        try
        {
            string probe = Path.Combine(dir, ".wtest_" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "x");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    static bool TryDeleteFile(string path, int retries)
    {
        if (string.IsNullOrEmpty(path)) return true;
        for (int i = 0; i < retries; i++)
        {
            try
            {
                if (!File.Exists(path)) return true;
                File.Delete(path);
                if (!File.Exists(path)) return true;
            }
            catch { }
            if (i + 1 < retries) Thread.Sleep(80);
        }
        return !File.Exists(path);
    }

    public static void MoveWithRetry(string src, string dst, int retries)
    {
        Exception last = null;
        for (int i = 0; i < retries; i++)
        {
            try
            {
                File.Move(src, dst);
                return;
            }
            catch (Exception ex) { last = ex; }
            if (i + 1 < retries) Thread.Sleep(100);
        }
        if (last != null) throw last;
    }

    public static string PickBackupPath(string target)
    {
        string baseOld = target + ".old";
        if (TryDeleteFile(baseOld, 3)) return baseOld;
        return baseOld + "." + DateTime.UtcNow.Ticks;
    }

    public static void CleanupUpdateArtifacts(string targetExe, string targetData, bool retryOld)
    {
        int oldTries = retryOld ? 12 : 1;
        bool anyCleaned = false;
        if (!string.IsNullOrEmpty(targetExe))
        {
            if (File.Exists(targetExe + ".old") && TryDeleteFile(targetExe + ".old", oldTries))
                anyCleaned = true;
            TryDeleteFile(targetExe + ".upd", 1);
            try
            {
                string dir = Path.GetDirectoryName(targetExe);
                string pat = Path.GetFileName(targetExe) + ".old.*";
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    foreach (string f in Directory.GetFiles(dir, pat))
                    {
                        if (TryDeleteFile(f, retryOld ? 3 : 1)) anyCleaned = true;
                    }
                }
            }
            catch { }
        }
        if (!string.IsNullOrEmpty(targetData))
        {
            if (File.Exists(targetData + ".old") && TryDeleteFile(targetData + ".old", retryOld ? 6 : 1))
                anyCleaned = true;
            TryDeleteFile(targetData + ".upd", 1);
            try
            {
                string ddir = Path.GetDirectoryName(targetData);
                string dpat = Path.GetFileName(targetData) + ".old.*";
                if (!string.IsNullOrEmpty(ddir) && Directory.Exists(ddir))
                {
                    foreach (string f in Directory.GetFiles(ddir, dpat))
                    {
                        if (TryDeleteFile(f, 1)) anyCleaned = true;
                    }
                }
            }
            catch { }
        }
        if (retryOld)
            Native.Log("update: restarted cleanly after self-update" + (anyCleaned ? " (.old cleaned)" : ""));
    }

    public static void RollbackSelfUpdateFiles(
        string targetExe, string oldExe, string updExe,
        string targetData, string oldData, string updData)
    {
        RollbackSelfUpdateFiles(targetExe, oldExe, updExe, targetData, oldData, updData, "", "");
    }

    public static void RollbackSelfUpdateFiles(
        string targetExe, string oldExe, string updExe,
        string targetData, string oldData, string updData,
        string overridePath, string oldOverride)
    {
        if (!string.IsNullOrEmpty(oldExe) && File.Exists(oldExe))
        {
            try
            {
                if (File.Exists(targetExe))
                {
                    if (!string.IsNullOrEmpty(updExe))
                    {
                        TryDeleteFile(updExe, 2);
                        try { MoveWithRetry(targetExe, updExe, 3); } catch { TryDeleteFile(targetExe, 3); }
                    }
                    else
                    {
                        TryDeleteFile(targetExe, 3);
                    }
                }
                if (!File.Exists(targetExe)) MoveWithRetry(oldExe, targetExe, 4);
            }
            catch (Exception ex) { Native.Log("update rollback exe fail: " + ex.Message); }
        }
        if (!string.IsNullOrEmpty(oldData) && File.Exists(oldData))
        {
            try
            {
                if (File.Exists(targetData))
                {
                    if (!string.IsNullOrEmpty(updData))
                    {
                        TryDeleteFile(updData, 2);
                        try { MoveWithRetry(targetData, updData, 3); } catch { TryDeleteFile(targetData, 3); }
                    }
                    else
                    {
                        TryDeleteFile(targetData, 3);
                    }
                }
                if (!File.Exists(targetData)) MoveWithRetry(oldData, targetData, 4);
            }
            catch (Exception ex) { Native.Log("update rollback data fail: " + ex.Message); }
        }
        if (!string.IsNullOrEmpty(overridePath) && !string.IsNullOrEmpty(oldOverride) && File.Exists(oldOverride))
        {
            try
            {
                TryDeleteFile(overridePath, 2);
                if (!File.Exists(overridePath)) MoveWithRetry(oldOverride, overridePath, 3);
            }
            catch (Exception ex) { Native.Log("update rollback override fail: " + ex.Message); }
        }
    }

    public static bool ApplySelfUpdateFiles(
        string targetExe, string updExe, long expectExeSize,
        string targetData, string updData, long expectDataSize,
        out string oldExeUsed, out string oldDataUsed, out string err)
    {
        string dummyOv;
        return ApplySelfUpdateFiles(
            targetExe, updExe, expectExeSize,
            targetData, updData, expectDataSize,
            "", false,
            out oldExeUsed, out oldDataUsed, out dummyOv, out err);
    }

    public static bool ApplySelfUpdateFiles(
        string targetExe, string updExe, long expectExeSize,
        string targetData, string updData, long expectDataSize,
        string overridePath, bool clearOverrides,
        out string oldExeUsed, out string oldDataUsed, out string oldOverrideUsed, out string err)
    {
        oldExeUsed = "";
        oldDataUsed = "";
        oldOverrideUsed = "";
        err = "";
        bool hasData = !string.IsNullOrEmpty(updData);
        bool movedDataOld = false, placedData = false;
        bool movedOvOld = false;
        bool movedExeOld = false, placedExe = false;
        try
        {
            if (string.IsNullOrEmpty(targetExe) || string.IsNullOrEmpty(updExe) || !File.Exists(updExe))
                throw new Exception("找不到新版本程序缓存");
            if (expectExeSize <= 0 || new FileInfo(updExe).Length != expectExeSize)
                throw new Exception("程序文件大小校验不一致");

            if (hasData)
            {
                if (string.IsNullOrEmpty(targetData) || !File.Exists(updData))
                    throw new Exception("找不到新版本数据缓存");
                if (expectDataSize <= 0 || new FileInfo(updData).Length != expectDataSize)
                    throw new Exception("数据文件大小校验不一致");

                oldDataUsed = PickBackupPath(targetData);
                if (File.Exists(targetData))
                {
                    MoveWithRetry(targetData, oldDataUsed, 3);
                    movedDataOld = true;
                }
                MoveWithRetry(updData, targetData, 3);
                placedData = true;
                if (!File.Exists(targetData) || new FileInfo(targetData).Length != expectDataSize)
                    throw new Exception("数据文件替换后大小校验失败");
            }

            if (clearOverrides && !string.IsNullOrEmpty(overridePath) && File.Exists(overridePath))
            {
                oldOverrideUsed = PickBackupPath(overridePath);
                MoveWithRetry(overridePath, oldOverrideUsed, 3);
                movedOvOld = true;
            }

            oldExeUsed = PickBackupPath(targetExe);
            if (File.Exists(targetExe))
            {
                MoveWithRetry(targetExe, oldExeUsed, 4);
                movedExeOld = true;
            }
            MoveWithRetry(updExe, targetExe, 4);
            placedExe = true;
            if (!File.Exists(targetExe) || new FileInfo(targetExe).Length != expectExeSize)
                throw new Exception("程序文件替换后大小校验失败");

            return true;
        }
        catch (Exception ex)
        {
            err = ex.Message;
            RollbackSelfUpdateFiles(
                targetExe, movedExeOld ? oldExeUsed : "", placedExe ? updExe : "",
                targetData, movedDataOld ? oldDataUsed : "", placedData ? updData : "",
                overridePath, movedOvOld ? oldOverrideUsed : "");
            return false;
        }
    }

    void StartSelfUpdate()
    {
        string u = !string.IsNullOrEmpty(updLatestUrl) ? updLatestUrl : updLatestFallback;
        if (!IsHttpUrl(u)) { ShowUpdateHelp(); return; }

        string exePath = Application.ExecutablePath;
        string dir = Path.GetDirectoryName(exePath);

        // 目录不可写（装在 Program Files 之类）→ 只能退回浏览器下载
        if (string.IsNullOrEmpty(dir) || !CanWriteDir(dir))
        {
            if (!TryOpenHttpUrl(u)) ShowUpdateHelp();
            return;
        }

        if (updBusy) return;


        if (updBusy) return;
        updBusy = true;
        SetUpdState("正在下载新版本…", Color.FromArgb(60, 90, 140));
        if (btnUpdOpen != null) btnUpdOpen.Enabled = false;

        string sha = updLatestSha;
        string updExe = exePath + ".upd";
        int pid = System.Diagnostics.Process.GetCurrentProcess().Id;

        TryDeleteFile(updExe, 1);

        var th = new Thread(delegate()
        {
            string err = null;
            try
            {
                if (!DownloadAndVerify(u, sha, updExe))
                    throw new Exception("程序文件校验不一致（可能下载不完整）");
            }
            catch (Exception ex) { err = ex.Message; }

            if (isClosing)
            {
                TryDeleteFile(updExe, 1);
                return;
            }

            SafeBeginInvoke(delegate
            {
                updBusy = false;
                if (err != null)
                {
                    TryDeleteFile(updExe, 1);
                    SetUpdState("自动更新失败：" + err + "，改为打开浏览器下载", Color.FromArgb(184, 88, 44));
                    if (btnUpdOpen != null) btnUpdOpen.Enabled = true;
                    if (!TryOpenHttpUrl(u)) ShowUpdateHelp();
                    return;
                }

                long expect = 0;
                try { expect = new FileInfo(updExe).Length; } catch { }
                string oldExeUsed, oldDataUsed, swapErr;
                if (!ApplySelfUpdateFiles(exePath, updExe, expect, "", "", 0, out oldExeUsed, out oldDataUsed, out swapErr))
                {
                    TryDeleteFile(updExe, 1);
                    SetUpdState("原地替换失败：" + swapErr + "，已保留原程序，改为打开浏览器下载", Color.FromArgb(184, 88, 44));
                    if (btnUpdOpen != null) btnUpdOpen.Enabled = true;
                    if (!TryOpenHttpUrl(u)) ShowUpdateHelp();
                    return;
                }

                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo();
                    psi.FileName = exePath;
                    psi.Arguments = "--after-update " + pid + (Program.IsDev ? " --dev" : "");
                    psi.WorkingDirectory = dir;
                    psi.UseShellExecute = false;
                    using (var launched = System.Diagnostics.Process.Start(psi)) { }
                }
                catch (Exception ex2)
                {
                    RollbackSelfUpdateFiles(exePath, oldExeUsed, updExe, "", "", "");
                    TryDeleteFile(updExe, 1);
                    SetUpdState("启动新版本失败：" + ex2.Message + "，已恢复旧版本，改为打开浏览器下载", Color.FromArgb(184, 88, 44));
                    if (btnUpdOpen != null) btnUpdOpen.Enabled = true;
                    if (!TryOpenHttpUrl(u)) ShowUpdateHelp();
                    return;
                }

                SetUpdState("下载并替换完成，正在重启…", Color.FromArgb(20, 130, 70));
                Native.Log("update: launched new instance --after-update " + pid);
                Application.Exit();
            });
        });
        th.IsBackground = true;
        th.Start();
    }

    void CheckUpdate(bool manual)
    {
        if (updBusy) return;
        if (string.IsNullOrEmpty(updateUrl))
        {
            SetUpdState("未配置更新地址（当前版本不会联网）", Color.FromArgb(120, 128, 140));
            return;
        }
        updBusy = true;
        if (manual)
        {
            SetUpdState("正在检查…", Color.FromArgb(60, 90, 140));
            if (btnUpdCheck != null) btnUpdCheck.Enabled = false;
        }
        string url = updateUrl;
        updThread = new Thread(delegate()
        {
            string json = null, err = null;
            // 冷启动 / CDN 抖一下很常见，失败后隔 600ms 再试一次，超时放宽到 12 秒
            try { json = HttpGetText(url, 12000); }
            catch (Exception)
            {
                try
                {
                    System.Threading.Thread.Sleep(600);
                    json = HttpGetText(url, 12000);
                }
                catch (Exception ex2) { err = ex2.GetType().Name; }
            }

            string ver = JsonField(json, "version");
            string dl = JsonField(json, "url");
            string fb = JsonField(json, "fallback");
            string notes = JsonField(json, "notes");
            string sha = JsonField(json, "sha256");
            // JsonField 已把 \n 还原成真换行，这里不再处理

            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    updBusy = false;
                    if (btnUpdCheck != null) btnUpdCheck.Enabled = true;

                    if (err != null)
                    {
                        if (manual) SetUpdState("检查失败（" + err + "）：网络异常或服务器无响应，请稍后重试“检查更新”", Color.FromArgb(184, 88, 44));
                        return;
                    }
                    if (string.IsNullOrEmpty(ver))
                    {
                        if (manual) SetUpdState("更新信息格式不对，稍后再试", Color.FromArgb(184, 88, 44));
                        return;
                    }

                    updLatestVersion = ver;
                    updLatestUrl = dl;
                    updLatestFallback = fb;
                    updLatestSha = sha;

                    if (CompareVersion(ver, APP_VERSION_NUM) > 0)
                    {
                        SetUpdState("发现新版本 v" + ver + "（当前 " + APP_VERSION_NUM + "）", Color.FromArgb(20, 130, 70));
                        if (btnUpdOpen != null) btnUpdOpen.Enabled = true;
                        updLatestNotes = notes;
                        RenderChangelog();
                        if (toast != null) toast.ShowText("发现新版本 v" + ver + "，可在“关于”页查看详情");
                    }
                    else
                    {
                        SetUpdState("已是最新版本（" + APP_VERSION_NUM + "）", Color.FromArgb(60, 110, 90));
                        if (btnUpdOpen != null) btnUpdOpen.Enabled = false;
                        if (manual && !string.IsNullOrEmpty(notes)) updLatestNotes = notes;
                    }
                });
            }
            catch { }
        });
        updThread.IsBackground = true;
        updThread.Start();
    }


}
