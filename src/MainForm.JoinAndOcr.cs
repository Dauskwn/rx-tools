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
    void CreateToolUtilityButtons(Control host)
    {
        bLog = new Button();
        bLog.Text = "查看日志";
        bLog.SetBounds(624, 10, 96, 36);
        bLog.Click += delegate
        {
            try
            {
                if (!string.IsNullOrEmpty(Native.LogPath) && File.Exists(Native.LogPath))
                {
                    using (var proc = System.Diagnostics.Process.Start(Native.LogPath)) { }
                }
                else
                {
                    toast.ShowText("还没有日志文件");
                }
            }
            catch (Exception ex) { toast.ShowText("打不开日志：" + ex.Message); }
        };
        host.Controls.Add(bLog);

        // 关闭热键：把 F5/F6… 还给系统（按钮本身也有 Ctrl+F10 开关）
        btnHotkeyOff = new Button();
        btnHotkeyOff.Text = "关闭热键 (" + ToggleKeyName() + ")";
        btnHotkeyOff.FlatStyle = FlatStyle.Flat;
        btnHotkeyOff.SetBounds(440, 10, 176, 36);
        btnHotkeyOff.ForeColor = Color.FromArgb(60, 90, 140);
        btnHotkeyOff.FlatAppearance.BorderColor = Color.FromArgb(180, 205, 238);
        btnHotkeyOff.Click += delegate { ToggleHotkeys(); };
        host.Controls.Add(btnHotkeyOff);

        btnHotkey = new Button();
        btnHotkey.Text = "热键设置";
        btnHotkey.SetBounds(728, 10, 104, 36);
        btnHotkey.Click += delegate { ShowHotkeyDialog(); };
        host.Controls.Add(btnHotkey);
    }

    void AddPlayerStripDivider(TabPage page)
    {
        var div = new Panel();
        div.SetBounds(8, 50, Math.Max(100, page.ClientSize.Width - 16), 1);
        div.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        div.BackColor = Color.FromArgb(226, 232, 240);
        page.Controls.Add(div);
    }

    void BuildPlayerToolPages()
    {
        joinPage = new TabPage("挤服助手");
        joinPage.BackColor = Color.White;
        AddPlayerStripDivider(joinPage);
        CreateJoinPanel(joinPage, 54);

        crossPage = new TabPage("屏幕准星");
        crossPage.BackColor = Color.White;
        AddPlayerStripDivider(crossPage);
        CreateCrossPanel(crossPage, 54);

        timerPage = new TabPage("倒计时");
        timerPage.BackColor = Color.White;
        AddPlayerStripDivider(timerPage);
        CreateTimerPanel(timerPage, 54);

        lblPlayerBarTip = new Label();
        lblPlayerBarTip.Font = TrackFont("Microsoft YaHei UI", 9.5f, FontStyle.Regular);
        lblPlayerBarTip.ForeColor = Color.FromArgb(90, 98, 112);
        lblPlayerBarTip.AutoEllipsis = true;
        lblPlayerBarTip.UseMnemonic = false;
        lblPlayerBarTip.Text = "A2S 状态探测与自动挤服 · 支持手动档 / 单一 IP / 收藏轮询（OCR 自动停）";
        lblPlayerBarTip.SetBounds(12, 17, 410, 22);
        joinPage.Controls.Add(lblPlayerBarTip);
        CreateToolUtilityButtons(joinPage);

        joinPage.Resize += delegate { LayoutToolBar(); };
        crossPage.Resize += delegate { LayoutToolBar(); };
        timerPage.Resize += delegate { LayoutToolBar(); };

        tabs.TabPages.Add(joinPage);
        tabs.TabPages.Add(crossPage);
        tabs.TabPages.Add(timerPage);

        UpdatePlayerToolBar();
        LayoutToolBar();
        LayoutJoin();
        LoadFavs();
        FillSrvList();
        ApplyModeUi();
        UpdateHotkeyLabels();
    }

    void UpdatePlayerToolBar()
    {
        if (tabs == null || btnHotkey == null) return;
        TabPage sel = tabs.SelectedTab;
        if (sel == null && joinPage != null) sel = joinPage;
        if (sel != joinPage && sel != crossPage && sel != timerPage) return;

        if (lblPlayerBarTip != null && lblPlayerBarTip.Parent != sel) sel.Controls.Add(lblPlayerBarTip);
        if (btnHotkeyOff != null && btnHotkeyOff.Parent != sel) sel.Controls.Add(btnHotkeyOff);
        if (bLog != null && bLog.Parent != sel) sel.Controls.Add(bLog);
        if (btnHotkey != null && btnHotkey.Parent != sel) sel.Controls.Add(btnHotkey);

        if (lblPlayerBarTip != null)
        {
            if (sel == joinPage)
                lblPlayerBarTip.Text = "A2S 状态探测与自动挤服 · 支持手动档 / 单一 IP / 收藏轮询（OCR 自动停）";
            else if (sel == crossPage)
                lblPlayerBarTip.Text = "桌面置顶透明准星 · 支持矢量调节与导入/粘贴自定义准星图";
            else if (sel == timerPage)
                lblPlayerBarTip.Text = "透明点击穿透倒计时 · 支持多方案管理与时间链变色 / 闪烁 / 标注";
        }
        LayoutToolBar();
    }

    Panel CreateJoinPanel(Control page, int topY)
    {
        // ================= 挤服面板 =================
        var pJoin = new Panel();
        pJoin.SetBounds(8, topY, page.ClientSize.Width - 16, page.ClientSize.Height - topY - 8);
        pJoin.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        page.Controls.Add(pJoin);

        btnAuto = new Button();
        btnAuto.Text = "▶ 开始挤服 (" + KeyName(2) + ")";
        btnAuto.Font = TrackFont("Microsoft YaHei UI", 10f, FontStyle.Bold);
        btnAuto.BackColor = Color.FromArgb(224, 240, 255);
        btnAuto.ForeColor = Color.FromArgb(12, 62, 130);
        btnAuto.FlatStyle = FlatStyle.Flat;
        btnAuto.FlatAppearance.BorderColor = Color.FromArgb(140, 185, 240);
        btnAuto.Cursor = Cursors.Hand;
        btnAuto.SetBounds(8, 4, 176, 38);
        btnAuto.Click += delegate { ToggleStart(); };
        pJoin.Controls.Add(btnAuto);

        var lblMode = new Label();
        lblMode.Text = "模式：";
        lblMode.SetBounds(194, 13, 48, 22);
        pJoin.Controls.Add(lblMode);

        rbTapMode = new RadioButton();
        rbTapMode.Text = "手动档";
        rbTapMode.SetBounds(242, 11, 86, 24);
        rbTapMode.Checked = true;
        rbTapMode.CheckedChanged += delegate { if (rbTapMode.Checked) OnModeChanged(); };
        pJoin.Controls.Add(rbTapMode);

        rbOneIp = new RadioButton();
        rbOneIp.Text = "单一 IP";
        rbOneIp.SetBounds(330, 11, 90, 24);
        rbOneIp.CheckedChanged += delegate { if (rbOneIp.Checked) OnModeChanged(); };
        pJoin.Controls.Add(rbOneIp);

        rbPollMode = new RadioButton();
        rbPollMode.Text = "收藏轮询";
        rbPollMode.SetBounds(422, 11, 98, 24);
        rbPollMode.CheckedChanged += delegate { if (rbPollMode.Checked) OnModeChanged(); };
        pJoin.Controls.Add(rbPollMode);

        btnGuide = new Button();
        btnGuide.Text = "?";
        btnGuide.Font = TrackFont("Microsoft YaHei UI", 9f, FontStyle.Bold);
        btnGuide.SetBounds(524, 10, 26, 26);
        btnGuide.Click += delegate { ShowGuide(); };
        pJoin.Controls.Add(btnGuide);

        var tipGuide = new ToolTip(uiComponents);
        tipGuide.InitialDelay = 300;
        tipGuide.SetToolTip(btnGuide, "使用指南：按当前模式查看分步操作与原理说明");

        var lblSpeed = new Label();
        lblSpeed.Text = "间隔：";
        lblSpeed.SetBounds(560, 13, 46, 22);
        pJoin.Controls.Add(lblSpeed);

        cmbSpeed = new ThemedComboBox();
        cmbSpeed.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbSpeed.Items.AddRange(TapIntervals);
        cmbSpeed.SelectedIndex = 1;
        cmbSpeed.SetBounds(606, 10, 92, 26);
        cmbSpeed.SelectedIndexChanged += delegate { SaveSettings(); RefreshUi(true); };
        pJoin.Controls.Add(cmbSpeed);

        chkOcr = new CheckBox();
        chkOcr.Text = "OCR 识别进服后自动停";
        chkOcr.Checked = true;
        chkOcr.SetBounds(712, 12, 210, 24);
        chkOcr.CheckedChanged += delegate
        {
            SaveSettings();
            if (!chkOcr.Checked) tapOcrFail = 0;
            RefreshUi(true);
        };
        pJoin.Controls.Add(chkOcr);

        lblListHint = new Label();
        lblListHint.Text = "服务器列表：单击选中＝单一 IP 目标　·　勾选＝参与收藏轮询（点表头可按空位排序）";
        lblListHint.ForeColor = Color.FromArgb(70, 90, 110);
        lblListHint.SetBounds(8, 52, 540, 22);
        pJoin.Controls.Add(lblListHint);

        btnCheckAll = new Button();
        btnCheckAll.Text = "全选";
        btnCheckAll.SetBounds(680, 46, 64, 28);
        btnCheckAll.Click += delegate { SetAllChecks(true); };
        pJoin.Controls.Add(btnCheckAll);

        btnUncheckAll = new Button();
        btnUncheckAll.Text = "全不选";
        btnUncheckAll.SetBounds(750, 46, 74, 28);
        btnUncheckAll.Click += delegate { SetAllChecks(false); };
        pJoin.Controls.Add(btnUncheckAll);

        btnRefreshFav = new Button();
        btnRefreshFav.Text = "↻ 刷新人数";
        btnRefreshFav.BackColor = Color.FromArgb(236, 244, 255);
        btnRefreshFav.ForeColor = Color.FromArgb(24, 64, 128);
        btnRefreshFav.FlatStyle = FlatStyle.Flat;
        btnRefreshFav.FlatAppearance.BorderColor = Color.FromArgb(170, 200, 238);
        btnRefreshFav.SetBounds(832, 46, 104, 28);
        btnRefreshFav.Click += delegate { RefreshAllFavs(); };
        pJoin.Controls.Add(btnRefreshFav);

        lvSrv = new BufferedListView();
        lvSrv.View = View.Details;
        lvSrv.CheckBoxes = true;
        lvSrv.FullRowSelect = true;
        lvSrv.MultiSelect = true;
        lvSrv.HideSelection = false;
        lvSrv.HeaderStyle = ColumnHeaderStyle.Clickable;
        lvSrv.Columns.Add("备注名", 110);
        lvSrv.Columns.Add("真实服务器名（A2S 探测）", 370);
        lvSrv.Columns.Add("人数", 104, HorizontalAlignment.Right);
        lvSrv.Columns.Add("地址", 160);
        lvSrv.SetBounds(8, 80, 900, 290);
        lvSrv.ColumnClick += delegate(object snd, ColumnClickEventArgs ce) { SortSrvListByColumn(ce.Column); };
        lvSrv.SelectedIndexChanged += delegate
        {
            SyncActiveTargets();
            UpdateJoinHint();
            RefreshUi(false);
        };
        lvSrv.ItemCheck += delegate
        {
            if (suppressListCheck) return;
            try
            {
                BeginInvoke(new MethodInvoker(delegate
                {
                    SyncActiveTargets();
                    UpdateJoinHint();
                    RefreshUi(false);
                }));
            }
            catch { }
        };
        pJoin.Controls.Add(lvSrv);

        lblTapGuide = new Label();
        lblTapGuide.BackColor = Color.FromArgb(255, 252, 244);
        lblTapGuide.ForeColor = Color.FromArgb(120, 80, 10);
        lblTapGuide.BorderStyle = BorderStyle.FixedSingle;
        lblTapGuide.Padding = new Padding(14, 12, 14, 12);
        lblTapGuide.Font = TrackFont("Microsoft YaHei UI", 10f, FontStyle.Regular);
        lblTapGuide.SetBounds(8, 80, 900, 300);
        lblTapGuide.Visible = false;
        pJoin.Controls.Add(lblTapGuide);

        lblNewIp = new Label();
        lblNewIp.Text = "IP:端口";
        lblNewIp.SetBounds(8, 396, 54, 22);
        pJoin.Controls.Add(lblNewIp);

        txtNewIp = new TextBox();
        txtNewIp.SetBounds(64, 394, 176, 26);
        txtNewIp.KeyDown += delegate(object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { AddFavFromBox(); e.Handled = true; e.SuppressKeyPress = true; }
        };
        pJoin.Controls.Add(txtNewIp);

        lblNewAlias = new Label();
        lblNewAlias.Text = "备注:";
        lblNewAlias.SetBounds(248, 396, 40, 22);
        pJoin.Controls.Add(lblNewAlias);

        txtNewAlias = new TextBox();
        txtNewAlias.SetBounds(290, 394, 96, 26);
        txtNewAlias.KeyDown += delegate(object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter) { AddFavFromBox(); e.Handled = true; e.SuppressKeyPress = true; }
        };
        pJoin.Controls.Add(txtNewAlias);

        btnAddFav = new Button();
        btnAddFav.Text = "★ 加入收藏";
        btnAddFav.BackColor = Color.FromArgb(236, 244, 255);
        btnAddFav.ForeColor = Color.FromArgb(24, 64, 128);
        btnAddFav.FlatStyle = FlatStyle.Flat;
        btnAddFav.FlatAppearance.BorderColor = Color.FromArgb(170, 200, 238);
        btnAddFav.SetBounds(394, 392, 104, 30);
        btnAddFav.Click += delegate { AddFavFromBox(); };
        pJoin.Controls.Add(btnAddFav);

        btnPasteFav = new Button();
        btnPasteFav.Text = "粘贴加服";
        btnPasteFav.SetBounds(504, 392, 84, 30);
        btnPasteFav.Click += delegate { PasteAndAddFav(); };
        pJoin.Controls.Add(btnPasteFav);

        btnEditFav = new Button();
        btnEditFav.Text = "编辑…";
        btnEditFav.SetBounds(594, 392, 76, 30);
        btnEditFav.Click += delegate { EditSelectedFav(); };
        pJoin.Controls.Add(btnEditFav);

        btnDelFav = new Button();
        btnDelFav.Text = "删除选中";
        btnDelFav.SetBounds(676, 392, 88, 30);
        btnDelFav.Click += delegate { DeleteSelectedFav(); };
        pJoin.Controls.Add(btnDelFav);

        cmsSrv = new ContextMenuStrip(uiComponents);
        cmsSrv.Items.Add("复制选中服务器 IP:端口", null, delegate
        {
            string a = SelectedAddr();
            if (a.Length > 0) CopyToClipboard("服务器地址", a);
        });
        cmsSrv.Items.Add("复制 connect 连接命令", null, delegate
        {
            string a = SelectedAddr();
            if (a.Length > 0) CopyToClipboard("连接命令", "connect " + a);
        });
        cmsSrv.Items.Add(new ToolStripSeparator());
        cmsSrv.Items.Add("编辑备注名 / IP… (F2)", null, delegate { EditSelectedFav(); });
        cmsSrv.Items.Add("勾选选中行", null, delegate { SetChecksOnSelection(true); });
        cmsSrv.Items.Add("取消勾选选中行", null, delegate { SetChecksOnSelection(false); });
        cmsSrv.Items.Add("刷新选中行的人数", null, delegate { RefreshSelectedFavs(); });
        cmsSrv.Items.Add(new ToolStripSeparator());
        cmsSrv.Items.Add("删除选中 (Delete)", null, delegate { DeleteSelectedFav(); });
        lvSrv.ContextMenuStrip = cmsSrv;
        lvSrv.MouseDown += delegate(object snd, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            ListViewItem it = lvSrv.GetItemAt(e.X, e.Y);
            if (it != null && !it.Selected)
            {
                lvSrv.SelectedItems.Clear();
                it.Selected = true;
                it.Focused = true;
            }
        };
        lvSrv.DoubleClick += delegate { EditSelectedFav(); };
        lvSrv.KeyDown += delegate(object snd, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete) { e.Handled = true; DeleteSelectedFav(); }
            else if (e.KeyCode == Keys.F2) { e.Handled = true; EditSelectedFav(); }
            else if (e.Control && e.KeyCode == Keys.C)
            {
                string a = SelectedAddr();
                if (a.Length > 0) CopyToClipboard("服务器地址", a);
                e.Handled = true;
            }
        };

        lblJoinHint = new Label();
        lblJoinHint.ForeColor = Color.FromArgb(40, 70, 110);
        lblJoinHint.SetBounds(8, 428, 900, 44);
        pJoin.Controls.Add(lblJoinHint);

        pJoinPanel = pJoin;
        pJoin.Resize += delegate { LayoutJoin(); };
        return pJoin;
    }

    Panel CreateCrossPanel(Control page, int topY)
    {
        // ================= 准星面板 =================
        var pCross = new Panel();
        pCross.SetBounds(8, topY, page.ClientSize.Width - 16, page.ClientSize.Height - topY - 8);
        pCross.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        page.Controls.Add(pCross);

        btnCross = new Button();
        btnCross.Text = "准星开关 (" + KeyName(4) + ")";
        btnCross.Font = TrackFont("Microsoft YaHei UI", 10f, FontStyle.Bold);
        btnCross.BackColor = Color.FromArgb(228, 242, 255);
        btnCross.ForeColor = Color.FromArgb(24, 64, 128);
        btnCross.FlatStyle = FlatStyle.Flat;
        btnCross.FlatAppearance.BorderColor = Color.FromArgb(140, 185, 240);
        btnCross.SetBounds(8, 4, 170, 36);
        btnCross.Click += delegate { ToggleCross(); };
        pCross.Controls.Add(btnCross);

        btnColor = new Button();
        btnColor.Text = "换颜色 (" + KeyName(3) + ")";
        btnColor.SetBounds(186, 4, 120, 36);
        btnColor.Click += delegate
        {
            overlay.CycleColor();
            if (cmbColor != null) cmbColor.SelectedIndex = overlay.ColorIndex;
            if (crossPreview != null) crossPreview.Invalidate();
            SaveSettings();
            RefreshUi(true);
        };
        pCross.Controls.Add(btnColor);

        var lblColorPick = new Label();
        lblColorPick.Text = "颜色：";
        lblColorPick.SetBounds(322, 12, 48, 22);
        pCross.Controls.Add(lblColorPick);

        cmbColor = new ThemedComboBox();
        cmbColor.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbColor.Items.AddRange(Overlay.ColorNames);
        cmbColor.SelectedIndex = 0;
        cmbColor.SetBounds(372, 9, 110, 26);
        cmbColor.SelectedIndexChanged += delegate
        {
            overlay.ColorIndex = cmbColor.SelectedIndex;
            if (crossPreview != null) crossPreview.Invalidate();
            SaveSettings();
            RefreshUi(true);
        };
        pCross.Controls.Add(cmbColor);

        var lblStyle = new Label();
        lblStyle.Text = "样式：";
        lblStyle.SetBounds(8, 56, 48, 22);
        pCross.Controls.Add(lblStyle);

        cmbStyle = new ThemedComboBox();
        cmbStyle.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbStyle.Items.AddRange(Overlay.Styles);
        cmbStyle.SelectedIndex = 0;
        cmbStyle.SetBounds(58, 54, 180, 26);
        cmbStyle.SelectedIndexChanged += delegate
        {
            overlay.StyleIndex = cmbStyle.SelectedIndex;
            overlay.Invalidate();
            if (crossPreview != null) crossPreview.Invalidate();
            if (!suppressSettingsSave) toast.ShowText("准星样式：" + cmbStyle.Text);
            SaveSettings();
            RefreshUi(true);
        };
        pCross.Controls.Add(cmbStyle);

        var lblThick = new Label();
        lblThick.Text = "线宽：";
        lblThick.SetBounds(256, 56, 48, 22);
        pCross.Controls.Add(lblThick);

        nThick = new NumericUpDown();
        nThick.Minimum = 1; nThick.Maximum = 6;
        nThick.Value = overlay.Thickness;
        nThick.SetBounds(306, 54, 56, 26);
        nThick.ValueChanged += delegate
        {
            overlay.Thickness = (int)nThick.Value;
            overlay.Invalidate();
            if (crossPreview != null) crossPreview.Invalidate();
            SaveSettings();
        };
        pCross.Controls.Add(nThick);

        var lblLen = new Label();
        lblLen.Text = "臂长：";
        lblLen.SetBounds(378, 56, 48, 22);
        pCross.Controls.Add(lblLen);

        nLen = new NumericUpDown();
        nLen.Minimum = 3; nLen.Maximum = 40;
        nLen.Value = overlay.Length;
        nLen.SetBounds(428, 54, 56, 26);
        nLen.ValueChanged += delegate
        {
            overlay.Length = (int)nLen.Value;
            overlay.Invalidate();
            if (crossPreview != null) crossPreview.Invalidate();
            SaveSettings();
        };
        pCross.Controls.Add(nLen);

        var lblGap = new Label();
        lblGap.Text = "间隙：";
        lblGap.SetBounds(500, 56, 48, 22);
        pCross.Controls.Add(lblGap);

        nGap = new NumericUpDown();
        nGap.Minimum = 0; nGap.Maximum = 30;
        nGap.Value = overlay.Gap;
        nGap.SetBounds(550, 54, 56, 26);
        nGap.ValueChanged += delegate
        {
            overlay.Gap = (int)nGap.Value;
            overlay.Invalidate();
            if (crossPreview != null) crossPreview.Invalidate();
            SaveSettings();
        };
        pCross.Controls.Add(nGap);

        var lblDot = new Label();
        lblDot.Text = "中心点：";
        lblDot.SetBounds(622, 56, 58, 22);
        pCross.Controls.Add(lblDot);

        nDot = new NumericUpDown();
        nDot.Minimum = 0; nDot.Maximum = 8;
        nDot.Value = overlay.DotSize;
        nDot.SetBounds(682, 54, 56, 26);
        nDot.ValueChanged += delegate
        {
            overlay.DotSize = (int)nDot.Value;
            overlay.Invalidate();
            if (crossPreview != null) crossPreview.Invalidate();
            SaveSettings();
        };
        pCross.Controls.Add(nDot);

        btnCrossImg = new Button();
        btnCrossImg.Text = "导入准星图片…";
        btnCrossImg.SetBounds(8, 284, 132, 30);
        btnCrossImg.Click += delegate { ImportCrosshairImage(); };
        pCross.Controls.Add(btnCrossImg);

        btnCrossImgPaste = new Button();
        btnCrossImgPaste.Text = "粘贴图片";
        btnCrossImgPaste.SetBounds(148, 284, 96, 30);
        btnCrossImgPaste.Click += delegate { PasteCrosshairImage(); };
        pCross.Controls.Add(btnCrossImgPaste);

        btnCrossImgClear = new Button();
        btnCrossImgClear.Text = "清除图片";
        btnCrossImgClear.SetBounds(252, 284, 88, 30);
        btnCrossImgClear.Click += delegate { ClearCrosshairImage(); };
        pCross.Controls.Add(btnCrossImgClear);

        lblCrossImg = new Label();
        lblCrossImg.Text = "当前用内置矢量样式（可导入 PNG 做自定义准星）";
        lblCrossImg.ForeColor = Color.FromArgb(90, 100, 114);
        lblCrossImg.SetBounds(352, 290, 520, 22);
        pCross.Controls.Add(lblCrossImg);

        crossPreview = new CrosshairPreview(overlay);
        crossPreview.SetBounds(8, 96, 180, 180);
        crossPreview.Cursor = Cursors.Hand;
        crossPreview.Click += delegate
        {
            overlay.CycleColor();
            if (cmbColor != null) cmbColor.SelectedIndex = overlay.ColorIndex;
            crossPreview.Invalidate();
            SaveSettings();
            RefreshUi(true);
        };
        pCross.Controls.Add(crossPreview);

        lblCrossHint = new Label();
        lblCrossHint.ForeColor = Color.FromArgb(80, 90, 102);
        lblCrossHint.SetBounds(206, 98, 620, 160);
        pCross.Controls.Add(lblCrossHint);
        return pCross;
    }

    Panel CreateTimerPanel(Control page, int topY)
    {
        // ================= 倒计时（时间链）面板 =================
        var pTimer = new Panel();
        pTimer.SetBounds(8, topY, page.ClientSize.Width - 16, page.ClientSize.Height - topY - 8);
        pTimer.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        page.Controls.Add(pTimer);
        pTimerPanel = pTimer;

        btnTimerStart = new Button();
        btnTimerStart.Text = "▶ 开始 / 重置 (" + KeyName(6) + ")";
        btnTimerStart.Font = TrackFont("Microsoft YaHei UI", 10f, FontStyle.Bold);
        btnTimerStart.FlatStyle = FlatStyle.Flat;
        btnTimerStart.Cursor = Cursors.Hand;
        btnTimerStart.SetBounds(8, 6, 180, 38);
        btnTimerStart.Click += delegate { StartOrResetCountdown(); };
        pTimer.Controls.Add(btnTimerStart);

        btnTimerPause = new Button();
        btnTimerPause.Text = "⏸ 暂停 (" + KeyName(8) + ")";
        btnTimerPause.Enabled = false;
        btnTimerPause.SetBounds(196, 6, 152, 38);
        btnTimerPause.Click += delegate { PauseOrResumeCountdown(); };
        pTimer.Controls.Add(btnTimerPause);

        btnTimerVis = new Button();
        btnTimerVis.Text = "显示悬浮窗 (" + KeyName(7) + ")";
        btnTimerVis.FlatStyle = FlatStyle.Flat;
        btnTimerVis.Cursor = Cursors.Hand;
        btnTimerVis.SetBounds(356, 6, 162, 38);
        btnTimerVis.Click += delegate { ToggleCountdownOverlay(); };
        pTimer.Controls.Add(btnTimerVis);

        lblTimerBig = new Label();
        lblTimerBig.Text = "18:00";
        lblTimerBig.Font = TrackFont("Consolas", 21f, FontStyle.Bold);
        lblTimerBig.TextAlign = ContentAlignment.MiddleCenter;
        lblTimerBig.ForeColor = Color.FromArgb(24, 64, 128);
        lblTimerBig.SetBounds(524, 2, 130, 46);
        pTimer.Controls.Add(lblTimerBig);

        lblTimerState = new Label();
        lblTimerState.Text = "待命中";
        lblTimerState.Font = TrackFont("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
        lblTimerState.ForeColor = Color.FromArgb(90, 100, 114);
        lblTimerState.TextAlign = ContentAlignment.MiddleLeft;
        lblTimerState.AutoEllipsis = true;
        lblTimerState.SetBounds(660, 6, 140, 38);
        pTimer.Controls.Add(lblTimerState);

        timerPresetTip = new ToolTip(uiComponents);
        timerPresetTip.InitialDelay = 300;

        var lblPresetTitle = new Label();
        lblPresetTitle.Text = "预设方案：";
        lblPresetTitle.SetBounds(8, 57, 74, 22);
        pTimer.Controls.Add(lblPresetTitle);

        flpTimerPresets = new FlowLayoutPanel();
        flpTimerPresets.SetBounds(84, 51, 360, 36);
        flpTimerPresets.AutoScroll = true;
        flpTimerPresets.WrapContents = true;
        flpTimerPresets.Padding = new Padding(0, 2, 0, 0);
        pTimer.Controls.Add(flpTimerPresets);

        var lblDurTitle = new Label();
        lblDurTitle.Name = "lblDurTitle";
        lblDurTitle.Text = "总时长：";
        lblDurTitle.SetBounds(468, 57, 58, 22);
        pTimer.Controls.Add(lblDurTitle);

        nTimerMin = new NumericUpDown();
        nTimerMin.Minimum = 0; nTimerMin.Maximum = 180;
        nTimerMin.Value = 18;
        nTimerMin.SetBounds(528, 55, 54, 26);
        nTimerMin.ValueChanged += delegate { OnTimerDurationEdited(); };
        pTimer.Controls.Add(nTimerMin);

        var lblDurMin = new Label();
        lblDurMin.Name = "lblDurMin";
        lblDurMin.Text = "分";
        lblDurMin.SetBounds(584, 57, 20, 22);
        pTimer.Controls.Add(lblDurMin);

        nTimerSec = new NumericUpDown();
        nTimerSec.Minimum = 0; nTimerSec.Maximum = 59;
        nTimerSec.Value = 0;
        nTimerSec.SetBounds(606, 55, 50, 26);
        nTimerSec.ValueChanged += delegate { OnTimerDurationEdited(); };
        pTimer.Controls.Add(nTimerSec);

        var lblDurSec = new Label();
        lblDurSec.Name = "lblDurSec";
        lblDurSec.Text = "秒";
        lblDurSec.SetBounds(658, 57, 20, 22);
        pTimer.Controls.Add(lblDurSec);

        btnTimerAddPreset = new Button();
        btnTimerAddPreset.Text = "＋ 新建方案";
        btnTimerAddPreset.BackColor = Color.FromArgb(236, 244, 255);
        btnTimerAddPreset.ForeColor = Color.FromArgb(24, 64, 128);
        btnTimerAddPreset.FlatStyle = FlatStyle.Flat;
        btnTimerAddPreset.FlatAppearance.BorderColor = Color.FromArgb(170, 200, 238);
        btnTimerAddPreset.SetBounds(686, 53, 102, 30);
        btnTimerAddPreset.Click += delegate { CreateCleanTimerPreset(); };
        pTimer.Controls.Add(btnTimerAddPreset);

        pTimerDiv1 = new Panel();
        pTimerDiv1.SetBounds(8, 91, pTimer.ClientSize.Width - 16, 1);
        pTimerDiv1.BackColor = Color.FromArgb(226, 232, 240);
        pTimer.Controls.Add(pTimerDiv1);

        var lblFontTitle = new Label();
        lblFontTitle.Text = "字号:";
        lblFontTitle.SetBounds(8, 102, 40, 22);
        pTimer.Controls.Add(lblFontTitle);

        nTimerFont = new NumericUpDown();
        nTimerFont.Minimum = 12; nTimerFont.Maximum = 72;
        nTimerFont.Value = 28;
        nTimerFont.SetBounds(50, 100, 50, 26);
        nTimerFont.ValueChanged += delegate { OnTimerAppearanceEdited(true); };
        pTimer.Controls.Add(nTimerFont);

        var lblTColorTitle = new Label();
        lblTColorTitle.Text = "默认色:";
        lblTColorTitle.SetBounds(108, 102, 52, 22);
        pTimer.Controls.Add(lblTColorTitle);

        cmbTimerColor = new ThemedComboBox();
        cmbTimerColor.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbTimerColor.Items.AddRange(CountdownOverlay.TimerColorNames);
        cmbTimerColor.SelectedIndex = 0;
        cmbTimerColor.SetBounds(162, 100, 88, 26);
        cmbTimerColor.SelectedIndexChanged += delegate { OnTimerAppearanceEdited(false); };
        pTimer.Controls.Add(cmbTimerColor);

        var lblAnchorTitle = new Label();
        lblAnchorTitle.Text = "位置:";
        lblAnchorTitle.SetBounds(258, 102, 40, 22);
        pTimer.Controls.Add(lblAnchorTitle);

        cmbTimerAnchor = new ThemedComboBox();
        cmbTimerAnchor.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbTimerAnchor.Items.AddRange(CountdownOverlay.AnchorNames);
        cmbTimerAnchor.SelectedIndex = 1;
        cmbTimerAnchor.SetBounds(300, 100, 156, 26);
        cmbTimerAnchor.SelectedIndexChanged += delegate { OnTimerAppearanceEdited(true); };
        pTimer.Controls.Add(cmbTimerAnchor);

        var lblOffXTitle = new Label();
        lblOffXTitle.Text = "X:";
        lblOffXTitle.SetBounds(466, 102, 20, 22);
        pTimer.Controls.Add(lblOffXTitle);

        nTimerOffX = new NumericUpDown();
        nTimerOffX.Minimum = -1000; nTimerOffX.Maximum = 1000;
        nTimerOffX.Value = 0;
        nTimerOffX.SetBounds(488, 100, 64, 26);
        nTimerOffX.ValueChanged += delegate { OnTimerAppearanceEdited(true); };
        pTimer.Controls.Add(nTimerOffX);

        var lblOffYTitle = new Label();
        lblOffYTitle.Text = "Y:";
        lblOffYTitle.SetBounds(560, 102, 20, 22);
        pTimer.Controls.Add(lblOffYTitle);

        nTimerOffY = new NumericUpDown();
        nTimerOffY.Minimum = -1000; nTimerOffY.Maximum = 1000;
        nTimerOffY.Value = 24;
        nTimerOffY.SetBounds(582, 100, 64, 26);
        nTimerOffY.ValueChanged += delegate { OnTimerAppearanceEdited(true); };
        pTimer.Controls.Add(nTimerOffY);

        btnTimerResetPos = new Button();
        btnTimerResetPos.Text = "重置位置";
        btnTimerResetPos.SetBounds(656, 98, 82, 28);
        btnTimerResetPos.Click += delegate
        {
            suppressTimerCtrl = true;
            cmbTimerAnchor.SelectedIndex = 1;
            nTimerOffX.Value = 0;
            nTimerOffY.Value = 24;
            suppressTimerCtrl = false;
            timerAnchorIdx = 1;
            timerOffX = 0;
            timerOffY = 24;
            SyncTimerOverlay(true);
            SaveSettings();
        };
        pTimer.Controls.Add(btnTimerResetPos);

        pTimerDiv2 = new Panel();
        pTimerDiv2.SetBounds(8, 134, pTimer.ClientSize.Width - 16, 1);
        pTimerDiv2.BackColor = Color.FromArgb(226, 232, 240);
        pTimer.Controls.Add(pTimerDiv2);

        lblTimerChainTitle = new Label();
        lblTimerChainTitle.Text = "时间链事件（双击编辑，多条命中按顺序叠加 · 右键预设可复制、重命名或删除）：";
        lblTimerChainTitle.ForeColor = Color.FromArgb(70, 90, 110);
        lblTimerChainTitle.AutoEllipsis = true;
        lblTimerChainTitle.SetBounds(8, 145, 420, 22);
        pTimer.Controls.Add(lblTimerChainTitle);

        btnChainAdd = new Button();
        btnChainAdd.Text = "＋ 添加事件…";
        btnChainAdd.BackColor = Color.FromArgb(236, 244, 255);
        btnChainAdd.ForeColor = Color.FromArgb(24, 64, 128);
        btnChainAdd.FlatStyle = FlatStyle.Flat;
        btnChainAdd.FlatAppearance.BorderColor = Color.FromArgb(170, 200, 238);
        btnChainAdd.SetBounds(440, 141, 112, 28);
        btnChainAdd.Click += delegate { AddTimerChainNode(); };
        pTimer.Controls.Add(btnChainAdd);

        btnChainEdit = new Button();
        btnChainEdit.Text = "编辑…";
        btnChainEdit.SetBounds(558, 141, 74, 28);
        btnChainEdit.Click += delegate { EditSelectedTimerChainNode(); };
        pTimer.Controls.Add(btnChainEdit);

        btnChainDel = new Button();
        btnChainDel.Text = "删除";
        btnChainDel.SetBounds(638, 141, 64, 28);
        btnChainDel.Click += delegate { DeleteSelectedTimerChainNode(); };
        pTimer.Controls.Add(btnChainDel);

        btnChainClear = new Button();
        btnChainClear.Text = "清空";
        btnChainClear.SetBounds(708, 141, 64, 28);
        btnChainClear.Click += delegate { ClearTimerChainNodes(); };
        pTimer.Controls.Add(btnChainClear);

        lvTimerChain = new BufferedListView();
        lvTimerChain.View = View.Details;
        lvTimerChain.FullRowSelect = true;
        lvTimerChain.MultiSelect = false;
        lvTimerChain.HideSelection = false;
        lvTimerChain.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        lvTimerChain.Columns.Add("#", 44);
        lvTimerChain.Columns.Add("触发时间", 196);
        lvTimerChain.Columns.Add("事件效果（变色 / 闪烁 / 文字标注）", 380);
        lvTimerChain.Columns.Add("状态", 104);
        lvTimerChain.SetBounds(8, 175, Math.Max(400, pTimer.ClientSize.Width - 16), 200);
        lvTimerChain.DoubleClick += delegate { EditSelectedTimerChainNode(); };
        lvTimerChain.KeyDown += delegate(object snd, KeyEventArgs ke)
        {
            if (ke.KeyCode == Keys.Delete) { ke.Handled = true; DeleteSelectedTimerChainNode(); }
            else if (ke.KeyCode == Keys.F2) { ke.Handled = true; EditSelectedTimerChainNode(); }
        };
        var cmsChain = new ContextMenuStrip(uiComponents);
        cmsChain.Items.Add("＋ 添加事件…", null, delegate { AddTimerChainNode(); });
        cmsChain.Items.Add("编辑选中事件… (F2)", null, delegate { EditSelectedTimerChainNode(); });
        cmsChain.Items.Add("删除选中事件 (Delete)", null, delegate { DeleteSelectedTimerChainNode(); });
        cmsChain.Items.Add(new ToolStripSeparator());
        cmsChain.Items.Add("清空全部事件", null, delegate { ClearTimerChainNodes(); });
        lvTimerChain.ContextMenuStrip = cmsChain;
        pTimer.Controls.Add(lvTimerChain);

        lblTimerHint = new Label();
        lblTimerHint.ForeColor = Color.FromArgb(95, 105, 118);
        lblTimerHint.AutoEllipsis = true;
        lblTimerHint.SetBounds(8, 382, Math.Max(300, pTimer.ClientSize.Width - 16), 22);
        pTimer.Controls.Add(lblTimerHint);

        pTimer.Resize += delegate { LayoutTimer(); };

        // 默认装入第 1 个方案（标准对局 18分）
        ApplyTimerPresetIndex(0, true);
        LayoutTimer();
        return pTimer;
    }

    void LayoutToolBar()
    {
        if (btnHotkey == null || btnHotkey.Parent == null) return;
        int pageW = btnHotkey.Parent.ClientSize.Width;
        if (pageW < 400) pageW = 400;
        if (btnHotkey != null) btnHotkey.SetBounds(pageW - 8 - 104, 10, 104, 36);
        if (bLog != null) bLog.SetBounds(pageW - 8 - 104 - 8 - 96, 10, 96, 36);
        if (btnHotkeyOff != null) btnHotkeyOff.SetBounds(pageW - 8 - 104 - 8 - 96 - 8 - 176, 10, 176, 36);
        if (lblPlayerBarTip != null && btnHotkeyOff != null)
            lblPlayerBarTip.SetBounds(12, 17, Math.Max(120, btnHotkeyOff.Left - 24), 22);
    }

    void LayoutTimer()
    {
        if (pTimerPanel == null) return;
        int W = pTimerPanel.ClientSize.Width;
        int H = pTimerPanel.ClientSize.Height;
        try
        {
            if (tabs != null && pTimerPanel.Parent != null)
            {
                int cap = tabs.DisplayRectangle.Height - pTimerPanel.Top - 6;
                if (cap > 200 && cap < H) H = cap;
            }
        }
        catch { }
        if (W < 600) W = 600;
        if (H < 300) H = 300;

        if (lblTimerState != null) lblTimerState.SetBounds(660, 6, Math.Max(120, W - 668), 38);

        if (btnTimerAddPreset != null) btnTimerAddPreset.SetBounds(W - 112, 53, 104, 30);
        Control cDurSec = pTimerPanel.Controls["lblDurSec"];
        if (cDurSec != null) cDurSec.SetBounds(W - 134, 57, 20, 22);
        if (nTimerSec != null) nTimerSec.SetBounds(W - 188, 55, 50, 26);
        Control cDurMin = pTimerPanel.Controls["lblDurMin"];
        if (cDurMin != null) cDurMin.SetBounds(W - 210, 57, 20, 22);
        if (nTimerMin != null) nTimerMin.SetBounds(W - 268, 55, 54, 26);
        Control cDurTitle = pTimerPanel.Controls["lblDurTitle"];
        if (cDurTitle != null) cDurTitle.SetBounds(W - 328, 57, 58, 22);
        if (flpTimerPresets != null) flpTimerPresets.SetBounds(84, 51, Math.Max(140, W - 336 - 84), 36);

        if (pTimerDiv1 != null) pTimerDiv1.SetBounds(8, 91, W - 16, 1);
        if (pTimerDiv2 != null) pTimerDiv2.SetBounds(8, 134, W - 16, 1);

        if (btnChainClear != null) btnChainClear.SetBounds(W - 72, 141, 64, 28);
        if (btnChainDel != null) btnChainDel.SetBounds(W - 142, 141, 64, 28);
        if (btnChainEdit != null) btnChainEdit.SetBounds(W - 222, 141, 74, 28);
        if (btnChainAdd != null) btnChainAdd.SetBounds(W - 340, 141, 112, 28);
        if (lblTimerChainTitle != null) lblTimerChainTitle.SetBounds(8, 145, Math.Max(180, W - 356), 22);

        int hintH = 22;
        int hintY = H - 6 - hintH;
        int lvH = Math.Max(90, hintY - 6 - 175);
        if (lvTimerChain != null)
        {
            lvTimerChain.SetBounds(8, 175, W - 16, lvH);
            if (lvTimerChain.Columns.Count == 4)
            {
                int cw0 = 44, cw1 = 196, cw3 = 104;
                int cw2 = Math.Max(180, W - 16 - cw0 - cw1 - cw3 - 24);
                lvTimerChain.Columns[0].Width = cw0;
                lvTimerChain.Columns[1].Width = cw1;
                lvTimerChain.Columns[2].Width = cw2;
                lvTimerChain.Columns[3].Width = cw3;
            }
        }
        if (lblTimerHint != null) lblTimerHint.SetBounds(8, hintY, W - 16, hintH);
    }

    // ---------- 倒计时与「时间链」核心控制 ----------
    void EvaluateCurrentTimeline()
    {
        TimerChainPreset.EvaluateTimeline(timerNodes, timerRemainSec, timerColorIdx,
            out timerActiveColorIdx, out timerActiveFlash, out timerActiveLabel);
    }

    void StartOrResetCountdown()
    {
        if (timerTotalSec <= 0)
        {
            toast.ShowText("请先设置大于 0 秒的倒计时时长");
            return;
        }
        timerRemainSec = timerTotalSec;
        timerRunning = true;
        timerPaused = false;
        timerFlashDim = false;
        timerPausedRemainMs = timerTotalSec * 1000.0;
        timerPausedElapsedMs = 0;
        timerStartedAt = DateTime.Now;
        timerEndAt = timerStartedAt.AddSeconds(timerTotalSec);
        EvaluateCurrentTimeline();
        if (timerOverlay != null)
        {
            SyncTimerOverlay(true);
            if (!timerOverlay.Visible)
            {
                timerOverlay.Visible = true;
                timerOverlay.ApplyBoundsAndTopmost();
            }
            Native.MakeTopmost(timerOverlay.Handle);
        }
        UpdateTimerButtonsStyle();
        UpdateTimerUi();
        Native.Log("timer start: " + timerTotalSec + "s, nodes=" + timerNodes.Count);
    }

    void PauseOrResumeCountdown()
    {
        if (!timerRunning) return;
        if (!timerPaused)
        {
            double remMs = Math.Max(0.0, (timerEndAt - DateTime.Now).TotalMilliseconds);
            timerPausedRemainMs = remMs;
            timerPausedElapsedMs = Math.Max(0L, (long)(DateTime.Now - timerStartedAt).TotalMilliseconds);
            timerRemainSec = Math.Max(0, (int)Math.Ceiling(remMs / 1000.0));
            timerPaused = true;
            timerFlashDim = false;
        }
        else
        {
            double useMs = timerPausedRemainMs > 0 ? timerPausedRemainMs : (timerRemainSec * 1000.0);
            DateTime now = DateTime.Now;
            timerStartedAt = now.AddMilliseconds(-timerPausedElapsedMs);
            timerEndAt = now.AddMilliseconds(useMs);
            timerPaused = false;
        }
        EvaluateCurrentTimeline();
        SyncTimerOverlay(false);
        UpdateTimerButtonsStyle();
        UpdateTimerUi();
    }

    void ToggleCountdownOverlay()
    {
        if (timerOverlay == null) return;
        try
        {
            if (timerOverlay.Visible)
            {
                timerOverlay.Visible = false;
                Native.Log("timer overlay hide");
            }
            else
            {
                EvaluateCurrentTimeline();
                SyncTimerOverlay(true);
                timerOverlay.Visible = true;
                timerOverlay.ApplyBoundsAndTopmost();
                Native.MakeTopmost(timerOverlay.Handle);
                Native.Log("timer overlay show");
            }
        }
        catch (Exception ex) { Native.Log("timer overlay EX: " + ex.Message); }
        UpdateTimerButtonsStyle();
        UpdateTimerUi();
    }

    void SyncTimerOverlay(bool layoutChanged)
    {
        if (timerOverlay == null) return;
        int digits = (timerRemainSec >= 6000) ? 6 : 5;
        if (digits != lastTimerDigits)
        {
            lastTimerDigits = digits;
            layoutChanged = true;
        }
        string lbl = timerActiveLabel ?? "";
        if (lbl != lastTimerOverlayLabel)
        {
            lastTimerOverlayLabel = lbl;
            layoutChanged = true;
        }
        timerOverlay.RemainingSeconds = timerRemainSec;
        timerOverlay.ActiveColorIndex = timerActiveColorIdx;
        timerOverlay.FlashDim = timerFlashDim;
        timerOverlay.ActiveLabel = lbl;
        timerOverlay.FontSize = timerFontSize;
        timerOverlay.AnchorIndex = timerAnchorIdx;
        timerOverlay.OffsetX = timerOffX;
        timerOverlay.OffsetY = timerOffY;
        if (layoutChanged) timerOverlay.ApplyBoundsAndTopmost();
        timerOverlay.Invalidate();
    }

    void SyncCurrentNodesToActivePreset()
    {
        if (activePresetIdx >= 0 && activePresetIdx < timerPresets.Count)
        {
            timerPresets[activePresetIdx].TotalSec = Math.Max(1, timerTotalSec);
            timerPresets[activePresetIdx].Nodes.Clear();
            for (int i = 0; i < timerNodes.Count; i++)
                timerPresets[activePresetIdx].Nodes.Add(timerNodes[i].Clone());
        }
    }

    void OnTimerDurationEdited()
    {
        if (suppressTimerCtrl || nTimerMin == null || nTimerSec == null) return;
        timerTotalSec = (int)nTimerMin.Value * 60 + (int)nTimerSec.Value;
        SyncCurrentNodesToActivePreset();
        if (!timerRunning)
        {
            timerRemainSec = timerTotalSec;
            timerPausedRemainMs = timerTotalSec * 1000.0;
            timerPausedElapsedMs = 0;
            timerFlashDim = false;
            EvaluateCurrentTimeline();
            SyncTimerOverlay(true);
        }
        UpdateTimerButtonsStyle();
        UpdateTimerUi();
        SaveSettings();
    }

    void OnTimerAppearanceEdited(bool layoutChanged)
    {
        if (suppressTimerCtrl) return;
        if (nTimerFont != null) timerFontSize = (int)nTimerFont.Value;
        if (cmbTimerColor != null && cmbTimerColor.SelectedIndex >= 0) timerColorIdx = cmbTimerColor.SelectedIndex;
        if (cmbTimerAnchor != null && cmbTimerAnchor.SelectedIndex >= 0) timerAnchorIdx = cmbTimerAnchor.SelectedIndex;
        if (nTimerOffX != null) timerOffX = (int)nTimerOffX.Value;
        if (nTimerOffY != null) timerOffY = (int)nTimerOffY.Value;
        EvaluateCurrentTimeline();
        SyncTimerOverlay(layoutChanged);
        UpdateTimerUi();
        SaveSettings();
    }

    void ApplyTimerPresetIndex(int idx, bool initQuiet)
    {
        if (timerPresets.Count == 0)
        {
            activePresetIdx = -1;
            RebuildTimerPresetButtons();
            RefillTimerChainList();
            return;
        }
        if (idx < 0 || idx >= timerPresets.Count) idx = 0;
        activePresetIdx = idx;
        TimerChainPreset p = timerPresets[idx];
        timerTotalSec = Math.Max(1, p.TotalSec);

        suppressTimerCtrl = true;
        if (nTimerMin != null) nTimerMin.Value = Math.Max(nTimerMin.Minimum, Math.Min(nTimerMin.Maximum, timerTotalSec / 60));
        if (nTimerSec != null) nTimerSec.Value = Math.Max(nTimerSec.Minimum, Math.Min(nTimerSec.Maximum, timerTotalSec % 60));
        suppressTimerCtrl = false;

        timerNodes.Clear();
        for (int i = 0; i < p.Nodes.Count; i++) timerNodes.Add(p.Nodes[i].Clone());
        timerNodes.Sort(TimerChainNode.CompareNodes);

        if (timerRunning && !initQuiet)
        {
            timerRemainSec = timerTotalSec;
            timerPaused = false;
            timerFlashDim = false;
            timerPausedRemainMs = timerTotalSec * 1000.0;
            timerPausedElapsedMs = 0;
            timerStartedAt = DateTime.Now;
            timerEndAt = timerStartedAt.AddSeconds(timerTotalSec);
        }
        else
        {
            timerRemainSec = timerTotalSec;
            timerPaused = false;
            timerFlashDim = false;
            timerPausedRemainMs = timerTotalSec * 1000.0;
            timerPausedElapsedMs = 0;
        }

        EvaluateCurrentTimeline();
        RebuildTimerPresetButtons();
        RefillTimerChainList();
        SyncTimerOverlay(true);
        UpdateTimerButtonsStyle();
        UpdateTimerUi();
        if (!initQuiet) SaveSettings();
    }

    void CreateCleanTimerPreset()
    {
        string nm = PromptText("新建方案", "输入新时间链方案名称：", "新方案");
        if (nm == null) return;
        nm = nm.Trim();
        if (nm.Length == 0) nm = "新方案";

        var p = new TimerChainPreset();
        p.Name = nm;
        p.TotalSec = 1080;
        timerPresets.Add(p);
        ApplyTimerPresetIndex(timerPresets.Count - 1, false);
        toast.ShowText("已新建空方案：" + nm);
    }

    void DuplicateTimerPreset(int idx)
    {
        if (idx < 0 || idx >= timerPresets.Count) return;
        TimerChainPreset copy = timerPresets[idx].Clone();
        copy.Name = timerPresets[idx].Name + "（复制）";
        timerPresets.Add(copy);
        ApplyTimerPresetIndex(timerPresets.Count - 1, false);
        toast.ShowText("已复制方案：" + copy.Name);
    }

    void RenameTimerPreset(int idx)
    {
        if (idx < 0 || idx >= timerPresets.Count) return;
        string nm = PromptText("重命名方案", "输入新的方案名称：", timerPresets[idx].Name);
        if (nm == null) return;
        nm = nm.Trim();
        if (nm.Length == 0) return;
        timerPresets[idx].Name = nm;
        RebuildTimerPresetButtons();
        UpdateTimerUi();
        SaveSettings();
    }

    void DeleteTimerPreset(int idx)
    {
        if (idx < 0 || idx >= timerPresets.Count) return;
        string nm = timerPresets[idx].Name;
        timerPresets.RemoveAt(idx);
        if (timerPresets.Count == 0)
        {
            activePresetIdx = -1;
            RebuildTimerPresetButtons();
            UpdateTimerUi();
        }
        else if (activePresetIdx == idx)
        {
            ApplyTimerPresetIndex(Math.Min(idx, timerPresets.Count - 1), true);
        }
        else
        {
            if (activePresetIdx > idx) activePresetIdx--;
            RebuildTimerPresetButtons();
            UpdateTimerUi();
        }
        SaveSettings();
        toast.ShowText("已删除方案：" + nm);
    }

    void RebuildTimerPresetButtons()
    {
        if (flpTimerPresets == null) return;
        ClearAndDisposeControls(flpTimerPresets);
        for (int i = 0; i < timerPresets.Count; i++)
        {
            int idx = i;
            TimerChainPreset p = timerPresets[i];
            var b = new Button();
            b.Text = p.Name;
            b.Tag = idx;
            b.AutoSize = true;
            b.MinimumSize = new Size(68, 30);
            b.MaximumSize = new Size(200, 30);
            b.Margin = new Padding(0, 0, 6, 4);
            b.Padding = new Padding(8, 0, 8, 0);
            b.FlatStyle = FlatStyle.Flat;
            b.Cursor = Cursors.Hand;
            b.Click += delegate { ApplyTimerPresetIndex(idx, false); };
            if (timerPresetTip != null) timerPresetTip.SetToolTip(b, "左键切换方案 · 右键可复制、重命名或删除");
            var cms = new ContextMenuStrip(uiComponents);
            cms.Items.Add("复制预设", null, delegate { DuplicateTimerPreset(idx); });
            cms.Items.Add("重命名方案…", null, delegate { RenameTimerPreset(idx); });
            cms.Items.Add("删除此方案", null, delegate { DeleteTimerPreset(idx); });
            b.ContextMenuStrip = cms;
            flpTimerPresets.Controls.Add(b);
        }
        UpdateTimerButtonsStyle();
    }

    void AddTimerChainNode()
    {
        TimerChainNode created = PromptTimerChainNode(null);
        if (created == null) return;
        lastAddedTriggerMode = created.TriggerMode;
        timerNodes.Add(created);
        timerNodes.Sort(TimerChainNode.CompareNodes);
        SyncCurrentNodesToActivePreset();
        EvaluateCurrentTimeline();
        RefillTimerChainList();
        SyncTimerOverlay(true);
        UpdateTimerUi();
        SaveSettings();
    }

    void EditSelectedTimerChainNode()
    {
        if (lvTimerChain == null || lvTimerChain.SelectedIndices.Count == 0)
        {
            toast.ShowText("请先在下方列表选中一条事件");
            return;
        }
        int idx = lvTimerChain.SelectedIndices[0];
        if (idx < 0 || idx >= timerNodes.Count) return;
        TimerChainNode edited = PromptTimerChainNode(timerNodes[idx]);
        if (edited == null) return;
        timerNodes[idx] = edited;
        timerNodes.Sort(TimerChainNode.CompareNodes);
        SyncCurrentNodesToActivePreset();
        EvaluateCurrentTimeline();
        RefillTimerChainList();
        SyncTimerOverlay(true);
        UpdateTimerUi();
        SaveSettings();
    }

    void DeleteSelectedTimerChainNode()
    {
        if (lvTimerChain == null || lvTimerChain.SelectedIndices.Count == 0)
        {
            toast.ShowText("请先选中要删除的事件");
            return;
        }
        int idx = lvTimerChain.SelectedIndices[0];
        if (idx < 0 || idx >= timerNodes.Count) return;
        timerNodes.RemoveAt(idx);
        SyncCurrentNodesToActivePreset();
        EvaluateCurrentTimeline();
        RefillTimerChainList();
        SyncTimerOverlay(true);
        UpdateTimerUi();
        SaveSettings();
    }

    void ClearTimerChainNodes()
    {
        if (timerNodes.Count == 0) return;
        if (MessageBox.Show(this, "确定清空当前时间链中的所有事件？",
                "RX 工具箱", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
            return;
        timerNodes.Clear();
        SyncCurrentNodesToActivePreset();
        EvaluateCurrentTimeline();
        RefillTimerChainList();
        SyncTimerOverlay(true);
        UpdateTimerUi();
        SaveSettings();
    }

    void RefillTimerChainList()
    {
        if (lvTimerChain == null) return;
        int selIdx = (lvTimerChain.SelectedIndices.Count > 0) ? lvTimerChain.SelectedIndices[0] : -1;
        lvTimerChain.BeginUpdate();
        lvTimerChain.Items.Clear();
        for (int i = 0; i < timerNodes.Count; i++)
        {
            TimerChainNode n = timerNodes[i];
            var it = new ListViewItem((i + 1).ToString());
            it.UseItemStyleForSubItems = false;
            it.SubItems.Add(n.FormatTimeRange());
            it.SubItems.Add(n.FormatEffects());
            it.SubItems.Add("待触发");
            lvTimerChain.Items.Add(it);
        }
        if (selIdx >= 0 && selIdx < lvTimerChain.Items.Count)
            lvTimerChain.Items[selIdx].Selected = true;
        lvTimerChain.EndUpdate();
        UpdateTimerChainRowsState();
    }

    void UpdateTimerChainRowsState()
    {
        if (lvTimerChain == null) return;
        bool activeClock = (timerRunning || timerRemainSec == 0);
        Color rowBg = IsDark ? Color.FromArgb(13, 17, 23) : Color.White;
        Color txtNormal = IsDark ? Color.FromArgb(230, 237, 243) : Color.FromArgb(30, 41, 59);
        Color txtMuted = IsDark ? Color.FromArgb(139, 148, 158) : Color.FromArgb(110, 120, 134);

        for (int i = 0; i < lvTimerChain.Items.Count && i < timerNodes.Count; i++)
        {
            ListViewItem it = lvTimerChain.Items[i];
            TimerChainNode n = timerNodes[i];
            it.BackColor = rowBg;
            for (int c = 0; c < 3; c++)
            {
                it.SubItems[c].BackColor = rowBg;
                it.SubItems[c].ForeColor = txtNormal;
            }
            string stTxt = "待触发";
            Color stFg = txtMuted;
            Color stBg = rowBg;
            bool boldSt = false;

            if (activeClock)
            {
                if (n.IsActiveAt(timerRemainSec))
                {
                    stTxt = "● 生效中";
                    boldSt = true;
                    bool isRed = (n.ColorIdx == 5);
                    if (IsDark)
                    {
                        stFg = isRed ? Color.FromArgb(255, 161, 152) : Color.FromArgb(126, 231, 135);
                        stBg = isRed ? Color.FromArgb(68, 28, 34) : Color.FromArgb(22, 58, 38);
                    }
                    else
                    {
                        stFg = isRed ? Color.FromArgb(185, 28, 28) : Color.FromArgb(21, 128, 61);
                        stBg = isRed ? Color.FromArgb(254, 226, 226) : Color.FromArgb(220, 252, 231);
                    }
                }
                else if (timerRemainSec < n.EffectiveEndSec())
                {
                    stTxt = "已过";
                }
            }

            if (it.SubItems[3].Text != stTxt) it.SubItems[3].Text = stTxt;
            it.SubItems[3].ForeColor = stFg;
            it.SubItems[3].BackColor = stBg;
            it.SubItems[3].Font = TrackFont("Microsoft YaHei UI", 8.5f, boldSt ? FontStyle.Bold : FontStyle.Regular);
        }
        if (lvTimerChain.IsHandleCreated) lvTimerChain.Invalidate();
    }

    TimerChainNode PromptTimerChainNode(TimerChainNode existing)
    {
        TimerChainNode src = existing != null ? existing.Clone() : new TimerChainNode();
        if (existing == null)
        {
            src.TriggerMode = Math.Max(0, Math.Min(2, lastAddedTriggerMode));
            src.StartSec = Math.Min(90, Math.Max(0, timerTotalSec));
            src.EndSec = 0;
            src.DurationSec = 5;
            src.ColorIdx = (timerColorIdx >= 0 && timerColorIdx < CountdownOverlay.TimerPalette.Length) ? timerColorIdx : 0;
            src.FlashMode = 0;
            src.LabelText = "";
        }

        using (var f = new Form())
        {
            f.Text = existing == null ? "添加时间链事件 · RX 工具箱" : "编辑时间链事件 · RX 工具箱";
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.StartPosition = FormStartPosition.CenterParent;
            f.MinimizeBox = false;
            f.MaximizeBox = false;
            f.ShowInTaskbar = false;
            f.BackColor = Color.White;
            f.ClientSize = new Size(492, 356);

            var lbTrig = new Label();
            lbTrig.Text = "1. 触发时机（按倒计时剩余时间计算）";
            lbTrig.Font = TrackFont("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            lbTrig.SetBounds(16, 12, 460, 22);
            f.Controls.Add(lbTrig);

            var rbM0 = new RadioButton();
            rbM0.Text = "持续数秒";
            rbM0.SetBounds(16, 40, 100, 24);
            rbM0.Checked = (src.TriggerMode == 0);
            f.Controls.Add(rbM0);

            var rbM1 = new RadioButton();
            rbM1.Text = "时间区间";
            rbM1.SetBounds(126, 40, 100, 24);
            rbM1.Checked = (src.TriggerMode == 1);
            f.Controls.Add(rbM1);

            var rbM2 = new RadioButton();
            rbM2.Text = "此后一直生效（至 00:00）";
            rbM2.SetBounds(236, 40, 230, 24);
            rbM2.Checked = (src.TriggerMode == 2);
            f.Controls.Add(rbM2);

            var lbSt = new Label();
            lbSt.Text = "起始时刻：";
            lbSt.SetBounds(16, 74, 78, 22);
            f.Controls.Add(lbSt);

            var nStM = new NumericUpDown();
            nStM.Minimum = 0; nStM.Maximum = 180;
            nStM.Value = Math.Max(0, Math.Min(180, src.StartSec / 60));
            nStM.SetBounds(98, 72, 58, 26);
            f.Controls.Add(nStM);

            var lbStM = new Label();
            lbStM.Text = "分";
            lbStM.SetBounds(160, 74, 20, 22);
            f.Controls.Add(lbStM);

            var nStS = new NumericUpDown();
            nStS.Minimum = 0; nStS.Maximum = 59;
            nStS.Value = Math.Max(0, Math.Min(59, src.StartSec % 60));
            nStS.SetBounds(184, 72, 54, 26);
            f.Controls.Add(nStS);

            var lbStS = new Label();
            lbStS.Text = "秒";
            lbStS.SetBounds(242, 74, 22, 22);
            f.Controls.Add(lbStS);

            var lbDur = new Label();
            lbDur.Text = "持续时长：";
            lbDur.SetBounds(16, 108, 78, 22);
            f.Controls.Add(lbDur);

            var nDur = new NumericUpDown();
            nDur.Minimum = 1; nDur.Maximum = 3600;
            nDur.Value = Math.Max(1, Math.Min(3600, src.DurationSec));
            nDur.SetBounds(98, 106, 66, 26);
            f.Controls.Add(nDur);

            var lbDurU = new Label();
            lbDurU.Text = "秒";
            lbDurU.SetBounds(168, 108, 28, 22);
            f.Controls.Add(lbDurU);

            var lbEn = new Label();
            lbEn.Text = "结束时刻：";
            lbEn.SetBounds(16, 108, 78, 22);
            f.Controls.Add(lbEn);

            var nEnM = new NumericUpDown();
            nEnM.Minimum = 0; nEnM.Maximum = 180;
            nEnM.Value = Math.Max(0, Math.Min(180, src.EndSec / 60));
            nEnM.SetBounds(98, 106, 58, 26);
            f.Controls.Add(nEnM);

            var lbEnM = new Label();
            lbEnM.Text = "分";
            lbEnM.SetBounds(160, 108, 20, 22);
            f.Controls.Add(lbEnM);

            var nEnS = new NumericUpDown();
            nEnS.Minimum = 0; nEnS.Maximum = 59;
            nEnS.Value = Math.Max(0, Math.Min(59, src.EndSec % 60));
            nEnS.SetBounds(184, 106, 54, 26);
            f.Controls.Add(nEnS);

            var lbEnS = new Label();
            lbEnS.Text = "秒";
            lbEnS.SetBounds(242, 108, 22, 22);
            f.Controls.Add(lbEnS);

            var divA = new Panel();
            divA.SetBounds(16, 144, 460, 1);
            divA.BackColor = Color.FromArgb(226, 232, 240);
            f.Controls.Add(divA);

            var lbEff = new Label();
            lbEff.Text = "2. 触发效果（可自由组合叠加）";
            lbEff.Font = TrackFont("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            lbEff.SetBounds(16, 154, 460, 22);
            f.Controls.Add(lbEff);

            var lbCol = new Label();
            lbCol.Text = "数字变色：";
            lbCol.SetBounds(16, 184, 78, 22);
            f.Controls.Add(lbCol);

            var cmbCol = new ThemedComboBox();
            cmbCol.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbCol.Items.Add("保持不变");
            cmbCol.Items.AddRange(CountdownOverlay.TimerColorNames);
            cmbCol.SelectedIndex = (src.ColorIdx >= 0 && src.ColorIdx < CountdownOverlay.TimerColorNames.Length)
                ? (src.ColorIdx + 1) : 0;
            cmbCol.SetBounds(98, 182, 130, 26);
            f.Controls.Add(cmbCol);

            var lbFl = new Label();
            lbFl.Text = "闪烁频率：";
            lbFl.SetBounds(244, 184, 78, 22);
            f.Controls.Add(lbFl);

            var cmbFl = new ThemedComboBox();
            cmbFl.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbFl.Items.AddRange(new object[] { "不闪烁", "低频闪烁 (2秒/次)", "中频闪烁 (1秒/次)", "高频闪烁 (快闪)" });
            cmbFl.SelectedIndex = Math.Max(0, Math.Min(3, src.FlashMode));
            cmbFl.SetBounds(324, 182, 152, 26);
            f.Controls.Add(cmbFl);

            var lbTxt = new Label();
            lbTxt.Text = "文字标注：";
            lbTxt.SetBounds(16, 220, 78, 22);
            f.Controls.Add(lbTxt);

            var txtLbl = new TextBox();
            txtLbl.Text = src.LabelText ?? "";
            txtLbl.MaxLength = 32;
            txtLbl.SetBounds(98, 218, 210, 26);
            f.Controls.Add(txtLbl);

            var lbTxtTip = new Label();
            lbTxtTip.Text = "可留空；如填“刷支援”";
            lbTxtTip.ForeColor = Color.FromArgb(95, 105, 118);
            lbTxtTip.SetBounds(314, 220, 162, 22);
            f.Controls.Add(lbTxtTip);

            var lbPreview = new Label();
            lbPreview.ForeColor = Color.FromArgb(24, 64, 128);
            lbPreview.SetBounds(16, 258, 460, 40);
            f.Controls.Add(lbPreview);

            Func<TimerChainNode> collect = delegate
            {
                var n = new TimerChainNode();
                n.TriggerMode = rbM0.Checked ? 0 : (rbM1.Checked ? 1 : 2);
                n.StartSec = (int)nStM.Value * 60 + (int)nStS.Value;
                n.EndSec = (int)nEnM.Value * 60 + (int)nEnS.Value;
                n.DurationSec = (int)nDur.Value;
                n.ColorIdx = cmbCol.SelectedIndex > 0 ? (cmbCol.SelectedIndex - 1) : -1;
                n.FlashMode = cmbFl.SelectedIndex;
                n.LabelText = (txtLbl.Text ?? "").Trim();
                return n;
            };

            Action refreshDlg = delegate
            {
                bool m0 = rbM0.Checked, m1 = rbM1.Checked;
                lbDur.Visible = m0; nDur.Visible = m0; lbDurU.Visible = m0;
                lbEn.Visible = m1; nEnM.Visible = m1; lbEnM.Visible = m1; nEnS.Visible = m1; lbEnS.Visible = m1;
                TimerChainNode cur = collect();
                lbPreview.Text = "规则预览：" + cur.FormatTimeRange() + " → " + cur.FormatEffects();
            };

            rbM0.CheckedChanged += delegate { refreshDlg(); };
            rbM1.CheckedChanged += delegate { refreshDlg(); };
            rbM2.CheckedChanged += delegate { refreshDlg(); };
            nStM.ValueChanged += delegate { refreshDlg(); };
            nStS.ValueChanged += delegate { refreshDlg(); };
            nDur.ValueChanged += delegate { refreshDlg(); };
            nEnM.ValueChanged += delegate { refreshDlg(); };
            nEnS.ValueChanged += delegate { refreshDlg(); };
            cmbCol.SelectedIndexChanged += delegate { refreshDlg(); };
            cmbFl.SelectedIndexChanged += delegate { refreshDlg(); };
            txtLbl.TextChanged += delegate { refreshDlg(); };
            refreshDlg();

            var bOk = new Button();
            bOk.Text = "确定";
            bOk.SetBounds(304, 310, 82, 32);
            bOk.Click += delegate
            {
                TimerChainNode cur = collect();
                if (cur.ColorIdx < 0 && cur.FlashMode == 0 && string.IsNullOrEmpty(cur.LabelText))
                {
                    toast.ShowText("请至少设置变色、闪烁或文字标注中的一项");
                    return;
                }
                f.DialogResult = DialogResult.OK;
                f.Close();
            };
            f.Controls.Add(bOk);

            var bNo = new Button();
            bNo.Text = "取消";
            bNo.SetBounds(394, 310, 82, 32);
            bNo.DialogResult = DialogResult.Cancel;
            f.Controls.Add(bNo);

            f.AcceptButton = bOk;
            f.CancelButton = bNo;
            ThemeDialog(f);
            if (f.ShowDialog(this) != DialogResult.OK) return null;
            return collect();
        }
    }

    void UpdateTimerButtonsStyle()
    {
        if (btnTimerStart != null)
        {
            btnTimerStart.Text = "▶ 开始 / 重置 (" + KeyName(6) + ")";
            btnTimerStart.FlatStyle = FlatStyle.Flat;
            if (IsDark)
            {
                btnTimerStart.BackColor = Color.FromArgb(27, 47, 74);
                btnTimerStart.ForeColor = Color.FromArgb(121, 192, 255);
                btnTimerStart.FlatAppearance.BorderColor = Color.FromArgb(56, 139, 253);
                btnTimerStart.FlatAppearance.MouseOverBackColor = Color.FromArgb(35, 60, 94);
            }
            else
            {
                btnTimerStart.BackColor = Color.FromArgb(224, 240, 255);
                btnTimerStart.ForeColor = Color.FromArgb(12, 62, 130);
                btnTimerStart.FlatAppearance.BorderColor = Color.FromArgb(140, 185, 240);
                btnTimerStart.FlatAppearance.MouseOverBackColor = Color.FromArgb(210, 232, 255);
            }
        }
        if (btnTimerPause != null)
        {
            btnTimerPause.Enabled = timerRunning;
            btnTimerPause.Text = (timerPaused ? "▶ 继续 (" : "⏸ 暂停 (") + KeyName(8) + ")";
        }
        if (btnTimerVis != null)
        {
            bool shown = (timerOverlay != null && timerOverlay.Visible);
            btnTimerVis.Text = (shown ? "隐藏悬浮窗 (" : "显示悬浮窗 (") + KeyName(7) + ")";
            btnTimerVis.FlatStyle = FlatStyle.Flat;
            if (IsDark)
            {
                btnTimerVis.BackColor = shown ? Color.FromArgb(22, 58, 38) : Color.FromArgb(33, 38, 45);
                btnTimerVis.ForeColor = shown ? Color.FromArgb(126, 231, 135) : Color.FromArgb(230, 237, 243);
                btnTimerVis.FlatAppearance.BorderColor = shown ? Color.FromArgb(46, 160, 67) : Color.FromArgb(54, 59, 66);
                btnTimerVis.FlatAppearance.MouseOverBackColor = shown ? Color.FromArgb(28, 74, 48) : Color.FromArgb(44, 52, 63);
            }
            else
            {
                btnTimerVis.BackColor = shown ? Color.FromArgb(220, 252, 231) : Color.FromArgb(248, 250, 252);
                btnTimerVis.ForeColor = shown ? Color.FromArgb(21, 128, 61) : Color.FromArgb(30, 41, 59);
                btnTimerVis.FlatAppearance.BorderColor = shown ? Color.FromArgb(134, 239, 172) : Color.FromArgb(203, 213, 225);
                btnTimerVis.FlatAppearance.MouseOverBackColor = shown ? Color.FromArgb(187, 247, 208) : Color.FromArgb(238, 242, 248);
            }
        }
        if (flpTimerPresets != null)
        {
            for (int i = 0; i < flpTimerPresets.Controls.Count; i++)
            {
                var b = flpTimerPresets.Controls[i] as Button;
                if (b == null) continue;
                int pidx = (b.Tag is int) ? (int)b.Tag : -1;
                bool active = (pidx == activePresetIdx);
                b.FlatStyle = FlatStyle.Flat;
                if (IsDark)
                {
                    b.BackColor = active ? Color.FromArgb(27, 47, 74) : Color.FromArgb(33, 38, 45);
                    b.ForeColor = active ? Color.FromArgb(121, 192, 255) : Color.FromArgb(230, 237, 243);
                    b.FlatAppearance.BorderColor = active ? Color.FromArgb(56, 139, 253) : Color.FromArgb(54, 59, 66);
                    b.FlatAppearance.MouseOverBackColor = active ? Color.FromArgb(35, 60, 94) : Color.FromArgb(44, 52, 63);
                }
                else
                {
                    b.BackColor = active ? Color.FromArgb(219, 234, 254) : Color.FromArgb(248, 250, 252);
                    b.ForeColor = active ? Color.FromArgb(24, 64, 128) : Color.FromArgb(30, 41, 59);
                    b.FlatAppearance.BorderColor = active ? Color.FromArgb(147, 197, 253) : Color.FromArgb(203, 213, 225);
                    b.FlatAppearance.MouseOverBackColor = active ? Color.FromArgb(191, 219, 254) : Color.FromArgb(238, 242, 248);
                }
            }
        }
    }

    void UpdateTimerFlashPreview()
    {
        if (lblTimerBig == null) return;
        int cidx = ((timerActiveColorIdx % CountdownOverlay.TimerPalette.Length) + CountdownOverlay.TimerPalette.Length) % CountdownOverlay.TimerPalette.Length;
        Color c = CountdownOverlay.TimerPalette[cidx];
        // 白天背景白底下的浅亮色稍微压暗一档保证界面里也看清
        if (!IsDark)
        {
            if (cidx == 0) c = Color.FromArgb(21, 128, 61);
            else if (cidx == 1) c = Color.FromArgb(8, 145, 178);
            else if (cidx == 2) c = Color.FromArgb(37, 99, 235);
            else if (cidx == 3) c = Color.FromArgb(180, 110, 0);
            else if (cidx == 4) c = Color.FromArgb(217, 119, 6);
            else if (cidx == 5) c = Color.FromArgb(220, 38, 38);
            else if (cidx == 6) c = Color.FromArgb(30, 41, 59);
        }
        if (timerFlashDim)
            c = IsDark ? Color.FromArgb(72, 82, 96) : Color.FromArgb(185, 195, 208);
        if (lblTimerBig.ForeColor != c) lblTimerBig.ForeColor = c;
    }

    void UpdateTimerUi()
    {
        if (lblTimerBig != null)
        {
            string txt = CountdownOverlay.FormatMmSs(timerRemainSec);
            if (lblTimerBig.Text != txt) lblTimerBig.Text = txt;
            UpdateTimerFlashPreview();
        }
        if (lblTimerState != null)
        {
            string st;
            Color sc;
            if (timerRunning && !timerPaused)
            {
                st = string.IsNullOrEmpty(timerActiveLabel) ? "倒计时中" : ("倒计时中 · " + timerActiveLabel);
                if (timerActiveColorIdx == 5)
                    sc = Color.FromArgb(239, 68, 68);
                else if (timerActiveColorIdx == 4)
                    sc = IsDark ? Color.FromArgb(251, 191, 36) : Color.FromArgb(217, 119, 6);
                else
                    sc = IsDark ? Color.FromArgb(126, 231, 135) : Color.FromArgb(21, 128, 61);
            }
            else if (timerRunning && timerPaused)
            {
                st = string.IsNullOrEmpty(timerActiveLabel) ? "已暂停" : ("已暂停 · " + timerActiveLabel);
                sc = IsDark ? Color.FromArgb(227, 179, 65) : Color.FromArgb(180, 83, 9);
            }
            else if (timerRemainSec == 0)
            {
                st = string.IsNullOrEmpty(timerActiveLabel) ? "已到点 00:00" : ("已到点 · " + timerActiveLabel);
                sc = Color.FromArgb(239, 68, 68);
            }
            else
            {
                string pn = (activePresetIdx >= 0 && activePresetIdx < timerPresets.Count) ? timerPresets[activePresetIdx].Name : "";
                st = pn.Length > 0 ? ("待命中 · " + pn) : "待命中";
                sc = IsDark ? Color.FromArgb(139, 148, 158) : Color.FromArgb(90, 100, 114);
            }
            if (lblTimerState.Text != st) lblTimerState.Text = st;
            if (lblTimerState.ForeColor != sc) lblTimerState.ForeColor = sc;
        }
        UpdateTimerChainRowsState();
    }

    // ---------- 配置持久化 ----------
    void LoadSettings()
    {
        try
        {
            if (string.IsNullOrEmpty(settingsPath) || !File.Exists(settingsPath)) return;
            suppressSettingsSave = true;
            suppressTimerCtrl = true;
            bool hasTimerChains = false;
            bool hasCustomChain = false;
            bool hasHotkey8 = false;
            int loadedPresetIdx = activePresetIdx;
            int legacyWarnSec = -1;
            List<TimerChainNode> loadedCustomNodes = null;
            string[] lines = File.ReadAllLines(settingsPath, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string k = line.Substring(0, eq).Trim();
                string v = line.Substring(eq + 1).Trim();
                if (k.Length > 6 && k.StartsWith("Hotkey", StringComparison.Ordinal))
                {
                    int hi, hv;
                    if (int.TryParse(k.Substring(6), out hi) && hi >= 1 && hi <= 8 && int.TryParse(v, out hv))
                    {
                        if (hi == 8) hasHotkey8 = true;
                        int nvk = hv / 16, nmod = hv % 16;
                        if (nvk >= 8 && nvk <= 255)
                        {
                            hkVk[hi - 1] = nvk;
                            hkMod[hi - 1] = nmod & 0x000F;
                        }
                    }
                    continue;
                }
                if (k == "TimerChains")
                {
                    hasTimerChains = true;
                    timerPresets = TimerChainPreset.DeserializePresets(v);
                    continue;
                }
                if (k == "TimerCustomChain")
                {
                    hasCustomChain = (v.Length > 0);
                    loadedCustomNodes = TimerChainPreset.DeserializeNodes(v);
                    continue;
                }
                if (k == "TimerPresets") continue;
                if (k == "UpdateUrl") { updateUrl = v; continue; }
                if (k == "AutoUpdate") { autoUpdateOn = (v == "1"); continue; }
                if (k == "CrossImg")
                {
                    crossImgToLoad = v;
                    continue;
                }
                if (k == "Checked")
                {
                    chkPref.Clear();
                    if (v.Length > 0)
                        foreach (string one in v.Split('|'))
                        {
                            string a = one.Trim();
                            if (a.Length > 0) chkPref.Add(a);
                        }
                    chkPrefLoaded = true;
                    chkPrefAuthoritative = true;
                    continue;
                }
                int iv;
                if (!int.TryParse(v, out iv)) continue;

                if (k == "StyleIndex" && iv >= 0 && iv < Overlay.Styles.Length)
                {
                    overlay.StyleIndex = iv;
                    if (cmbStyle != null) cmbStyle.SelectedIndex = iv;
                }
                else if (k == "ColorIndex" && iv >= 0 && iv < Overlay.Palette.Length)
                {
                    overlay.ColorIndex = iv;
                    if (cmbColor != null) cmbColor.SelectedIndex = iv;
                }
                else if (k == "Thickness" && iv >= 1 && iv <= 6)
                {
                    overlay.Thickness = iv;
                    if (nThick != null) nThick.Value = iv;
                }
                else if (k == "Length" && iv >= 3 && iv <= 40)
                {
                    overlay.Length = iv;
                    if (nLen != null) nLen.Value = iv;
                }
                else if (k == "Gap" && iv >= 0 && iv <= 30)
                {
                    overlay.Gap = iv;
                    if (nGap != null) nGap.Value = iv;
                }
                else if (k == "DotSize" && iv >= 0 && iv <= 8)
                {
                    overlay.DotSize = iv;
                    if (nDot != null) nDot.Value = iv;
                }
                else if (k == "TimerPresetIdx")
                {
                    loadedPresetIdx = iv;
                }
                else if (k == "TimerSec" && iv >= 1 && iv <= 10859)
                {
                    timerTotalSec = iv;
                    timerRemainSec = iv;
                    if (nTimerMin != null) nTimerMin.Value = Math.Max(nTimerMin.Minimum, Math.Min(nTimerMin.Maximum, iv / 60));
                    if (nTimerSec != null) nTimerSec.Value = Math.Max(nTimerSec.Minimum, Math.Min(nTimerSec.Maximum, iv % 60));
                }
                else if (k == "TimerWarnSec" && iv >= 0 && iv <= 10859)
                {
                    legacyWarnSec = iv;
                }
                else if (k == "TimerFont" && iv >= 12 && iv <= 72)
                {
                    timerFontSize = iv;
                    if (nTimerFont != null) nTimerFont.Value = iv;
                }
                else if (k == "TimerColor" && iv >= 0 && iv < CountdownOverlay.TimerPalette.Length)
                {
                    timerColorIdx = iv;
                    if (cmbTimerColor != null) cmbTimerColor.SelectedIndex = iv;
                }
                else if (k == "TimerAnchor" && iv >= 0 && iv < CountdownOverlay.AnchorNames.Length)
                {
                    timerAnchorIdx = iv;
                    if (cmbTimerAnchor != null) cmbTimerAnchor.SelectedIndex = iv;
                }
                else if (k == "TimerOffX" && iv >= -1000 && iv <= 1000)
                {
                    timerOffX = iv;
                    if (nTimerOffX != null) nTimerOffX.Value = iv;
                }
                else if (k == "TimerOffY" && iv >= -1000 && iv <= 1000)
                {
                    timerOffY = iv;
                    if (nTimerOffY != null) nTimerOffY.Value = iv;
                }
                else if (k == "TimerLastMode" && iv >= 0 && iv <= 2)
                {
                    lastAddedTriggerMode = iv;
                }
                else if (k == "SpeedIndex" && cmbSpeed != null && iv >= 0 && iv < cmbSpeed.Items.Count)
                {
                    cmbSpeed.SelectedIndex = iv;
                }
                else if (k == "OcrOn" && chkOcr != null)
                {
                    chkOcr.Checked = (v == "1");
                }
                else if (k == "ThemeMode" && iv >= 0 && iv <= 2)
                {
                    themeMode = iv;
                }
                else if (k == "JMode" && rbTapMode != null && iv >= 0 && iv <= 2)
                {
                    suppressModeEvent = true;
                    if (iv == 0) rbTapMode.Checked = true;
                    else if (iv == 2) rbPollMode.Checked = true;
                    else rbOneIp.Checked = true;
                    suppressModeEvent = false;
                }
            }
            if (!hasHotkey8 && hkVk[4] == 0x78 && hkMod[4] == 0x0002)
            {
                hkVk[4] = 0x7A;
                hkMod[4] = 0x0002;
            }
            if (hasTimerChains)
            {
                activePresetIdx = (loadedPresetIdx >= 0 && loadedPresetIdx < timerPresets.Count)
                    ? loadedPresetIdx : (timerPresets.Count > 0 && loadedPresetIdx >= 0 ? 0 : -1);
                if (hasCustomChain && loadedCustomNodes != null)
                {
                    timerNodes = loadedCustomNodes;
                }
                else if (activePresetIdx >= 0 && activePresetIdx < timerPresets.Count)
                {
                    timerNodes.Clear();
                    for (int i = 0; i < timerPresets[activePresetIdx].Nodes.Count; i++)
                        timerNodes.Add(timerPresets[activePresetIdx].Nodes[i].Clone());
                }
                else
                {
                    activePresetIdx = -1;
                    timerNodes = loadedCustomNodes != null ? loadedCustomNodes : new List<TimerChainNode>();
                }
            }
            else if (legacyWarnSec >= 0 && timerNodes.Count > 0)
            {
                timerNodes[0] = TimerChainPreset.MakeNode(2, legacyWarnSec, 0, 5, 5, 0, "");
                SyncCurrentNodesToActivePreset();
            }
            EvaluateCurrentTimeline();
            RebuildTimerPresetButtons();
            RefillTimerChainList();
            SyncTimerOverlay(true);
            UpdateTimerButtonsStyle();
            UpdateTimerUi();
            overlay.Invalidate();
            if (crossPreview != null) crossPreview.Invalidate();
        }
        catch (Exception ex) { Native.Log("settings load: " + ex.Message); }
        finally { suppressTimerCtrl = false; suppressSettingsSave = false; }
    }

    // 把当前列表的勾选状态记到偏好集合里（用户手动勾选时调用）
    void CaptureChkPref()
    {
        if (lvSrv == null) return;
        chkPref.Clear();
        for (int i = 0; i < lvSrv.Items.Count; i++)
            if (lvSrv.Items[i].Checked)
            {
                string a = Convert.ToString(lvSrv.Items[i].Tag);
                if (!string.IsNullOrEmpty(a)) chkPref.Add(a);
            }
        chkPrefLoaded = true;
        SaveSettings();
    }

    void SaveSettings()
    {
        if (suppressSettingsSave || string.IsNullOrEmpty(settingsPath)) return;
        try
        {
            var sb = new StringBuilder();
            sb.Append("StyleIndex=").Append(overlay.StyleIndex).Append("\r\n");
            sb.Append("ColorIndex=").Append(overlay.ColorIndex).Append("\r\n");
            sb.Append("Thickness=").Append(overlay.Thickness).Append("\r\n");
            sb.Append("Length=").Append(overlay.Length).Append("\r\n");
            sb.Append("Gap=").Append(overlay.Gap).Append("\r\n");
            sb.Append("DotSize=").Append(overlay.DotSize).Append("\r\n");
            if (cmbSpeed != null) sb.Append("SpeedIndex=").Append(cmbSpeed.SelectedIndex).Append("\r\n");
            if (chkOcr != null) sb.Append("OcrOn=").Append(chkOcr.Checked ? 1 : 0).Append("\r\n");
            if (overlay != null && !string.IsNullOrEmpty(overlay.CustomImagePath))
                sb.Append("CrossImg=").Append(overlay.CustomImagePath).Append("\r\n");
            for (int hi = 0; hi < 8; hi++)
                sb.Append("Hotkey").Append(hi + 1).Append("=").Append(hkVk[hi] * 16 + hkMod[hi]).Append("\r\n");
            sb.Append("TimerPresetIdx=").Append(activePresetIdx).Append("\r\n");
            sb.Append("TimerLastMode=").Append(lastAddedTriggerMode).Append("\r\n");
            sb.Append("TimerSec=").Append(timerTotalSec).Append("\r\n");
            sb.Append("TimerFont=").Append(timerFontSize).Append("\r\n");
            sb.Append("TimerColor=").Append(timerColorIdx).Append("\r\n");
            sb.Append("TimerAnchor=").Append(timerAnchorIdx).Append("\r\n");
            sb.Append("TimerOffX=").Append(timerOffX).Append("\r\n");
            sb.Append("TimerOffY=").Append(timerOffY).Append("\r\n");
            sb.Append("TimerChains=").Append(TimerChainPreset.SerializePresets(timerPresets)).Append("\r\n");
            sb.Append("TimerCustomChain=").Append(TimerChainPreset.SerializeNodes(timerNodes)).Append("\r\n");
            sb.Append("JMode=").Append(JoinModeIdx).Append("\r\n");
            sb.Append("AutoUpdate=").Append(autoUpdateOn ? 1 : 0).Append("\r\n");
            sb.Append("ThemeMode=").Append(themeMode).Append("\r\n");
            if (!string.IsNullOrEmpty(updateUrl)) sb.Append("UpdateUrl=").Append(updateUrl).Append("\r\n");
            // 收藏轮询的勾选偏好；一个都没勾也要写（表示"用户就是要全不选"）
            sb.Append("Checked=");
            if (lvSrv != null)
            {
                var picked = new System.Collections.Generic.List<string>();
                for (int i = 0; i < lvSrv.Items.Count; i++)
                    if (lvSrv.Items[i].Checked) picked.Add(Convert.ToString(lvSrv.Items[i].Tag));
                sb.Append(string.Join("|", picked.ToArray()));
            }
            sb.Append("\r\n");
            AtomicWriteText(settingsPath, sb.ToString());
        }
        catch { }
    }

    // ---------- 收藏夹 ----------
    void LoadFavs()
    {
        try
        {
            if (File.Exists(favPath))
            {
                string[] lines = File.ReadAllLines(favPath, Encoding.UTF8);
                int n = 0;
                for (int i = 0; i < lines.Length; i++) if (lines[i].Trim().Length > 0) n++;
                favList = new string[n];
                int k = 0;
                for (int i = 0; i < lines.Length; i++)
                    if (lines[i].Trim().Length > 0) favList[k++] = lines[i].Trim();
            }
        }
        catch (Exception ex) { Native.Log("favs load: " + ex.Message); }
    }

    bool SaveFavs()
    {
        try { return AtomicWriteText(favPath, string.Join(Environment.NewLine, favList)); }
        catch (Exception ex) { Native.Log("favs save: " + ex.Message); return false; }
    }

    int JoinModeIdx
    {
        get
        {
            if (rbTapMode != null && rbTapMode.Checked) return 0;
            if (rbPollMode != null && rbPollMode.Checked) return 2;
            return 1;
        }
    }

    void ApplyModeUi()
    {
        int m = JoinModeIdx;
        bool tap = (m == 0);
        bool poll = (m == 2);

        if (lvSrv != null)
        {
            lvSrv.Visible = !tap;
            if (lvSrv.CheckBoxes != poll) lvSrv.CheckBoxes = poll;
        }
        if (lblListHint != null)
        {
            lblListHint.Visible = !tap;
            lblListHint.Text = tap
                ? "手动档无需选服：在游戏内点击连接后，由工具连续发送回车"
                : (poll ? "服务器列表：勾选参与轮询的服务器（支持全选），点击“↻ 刷新人数”查询在线情况（点击表头可排序）"
                        : "服务器列表：单击选中需要加入的服务器，点击“↻ 刷新人数”查询在线情况（点击表头可排序）");
        }
        if (btnCheckAll != null) btnCheckAll.Visible = poll;
        if (btnUncheckAll != null) btnUncheckAll.Visible = poll;
        if (btnRefreshFav != null) btnRefreshFav.Visible = !tap;
        if (lblTapGuide != null) lblTapGuide.Visible = tap;

        if (lblNewIp != null) lblNewIp.Visible = !tap;
        if (txtNewIp != null) txtNewIp.Visible = !tap;
        if (lblNewAlias != null) lblNewAlias.Visible = !tap;
        if (txtNewAlias != null) txtNewAlias.Visible = !tap;
        if (btnAddFav != null) btnAddFav.Visible = !tap;
        if (btnPasteFav != null) btnPasteFav.Visible = !tap;
        if (btnDelFav != null) btnDelFav.Visible = !tap;
        if (btnEditFav != null) btnEditFav.Visible = !tap;

        LayoutJoin();
        UpdateJoinHint();
        if (pJoinPanel != null)
        {
            try
            {
                BeginInvoke(new MethodInvoker(delegate
                {
                    if (pJoinPanel == null || isClosing) return;
                    pJoinPanel.Invalidate(true);
                }));
            }
            catch { }
        }
    }

    static Image guideImgL, guideImgR;

    static Image LoadGuideImg(string resName, ref Image slot)
    {
        if (slot != null) return slot;
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var st = asm.GetManifestResourceStream(resName))
            {
                if (st == null) return null;
                var ms = new MemoryStream();
                st.CopyTo(ms);
                ms.Position = 0;
                slot = Image.FromStream(ms);
            }
        }
        catch { slot = null; }
        return slot;
    }

    void ShowGuide()
    {
        int m = JoinModeIdx;
        string hkStart = KeyName(2);
        string title;
        string[] steps, flow;

        if (m == 0)
        {
            title = "手动档 · 游戏内点击连接后自动连按回车";
            steps = new string[] {
                "1. 打开游戏：Play → 服务器列表，找到目标服务器。",
                "2. 选中服务器点击“连接”。若弹出“服务器人数已满”提示框，保持开启即可，也不要关闭本工具。",
                "3. 切换回游戏窗口使其保持在前台，然后按下 " + hkStart + "。",
                "4. 工具会按所设定的“间隔”自动发送回车键：关闭满员弹窗并重新发起连接。",
                "5. 进入游戏后再次按 " + hkStart + " 停止（如勾选了 OCR 自动识别，检测到加载画面后会自动停止）。",
            };
            flow = new string[] {
                "1. 工具在做什么",
                "· 仅模拟按下回车键（keybd_event 配合硬件扫描码，确保游戏正常响应）。",
                "· 满员提示框的默认焦点位于确认按钮上，按下回车即关闭弹窗并触发重新连接；",
                "  持续按回车即可不断重试进入，对画面和操作影响最小。",
                "· 不模拟鼠标点击，也不修改剪贴板或发送文字，不会打断正常加载流程。",
                "",
                "2. 使用条件",
                "· 游戏必须处于前台窗口：工具仅向当前前台游戏窗口发送按键，切出游戏时会自动跳过。",
                "· 满员提示框需保持活动状态（点击连接后默认处于激活状态）。",
                "",
                "3. 何时停止",
                "· 手动停止：按下 " + hkStart + "。",
                "· 自动停止：开启 OCR 时，后台每 2 秒截取一次游戏画面，识别到“正在加载插件 / 加载中”等进度文字时立即停止，无法识别时不会误停。",
            };
        }
        else if (m == 1)
        {
            title = "单一 IP · 专注重试列表中选中的服务器";
            steps = new string[] {
                "1. 启动游戏并停留在主菜单即可（无需打开游戏内服务器列表）。",
                "2. 在下方服务器列表中单击选中需要进入的服务器。",
                "3. 若列表中没有该服，可在下方“IP:端口”栏输入地址（备注选填）后点击“★ 加入收藏”，或复制地址后点击“粘贴加服”。",
                "4. ★ 切回游戏后按 ~ 打开控制台（必须手动打开，否则无法写入连接指令）。",
                "5. 保持控制台打开，直接按 " + hkStart + " 开始：",
                "   工具会自动将 connect 指令粘贴进控制台并回车重试。",
                "6. 进服后按 " + hkStart + " 停止（如勾选了 OCR 自动识别，检测到加载画面后会自动停止）。",
            };
            flow = new string[] {
                "1. 工具在做什么",
                "· 自动生成 \"connect IP:端口\" 写入剪贴板，并在控制台内执行粘贴与回车。",
                "· 启动前自动备份原剪贴板内容，停止挤服后自动还原。",
                "· 每次发送严格按所选间隔执行（推荐 200ms 左右）。",
                "",
                "2. 为什么需要手动打开控制台",
                "· GMod 引擎会拦截模拟按键开关控制台，因此无法由外部程序自动呼出。",
                "· 正确顺序为：进入游戏 → 按 ~ 打开控制台 → 按 " + hkStart + "（全局热键，无需切回工具）。",
                "",
                "3. 输入法与窗口焦点",
                "· 开始时会自动将游戏窗口切换为英文输入法，避免中文输入法吞键；任何方式停止都会还原原输入法。",
                "· 只要游戏已处于前台，就不会反复抢夺焦点或把全屏窗口还原为窗口化。",
            };
        }
        else
        {
            title = "收藏轮询 · 在勾选的服务器之间按顺序轮流尝试";
            steps = new string[] {
                "1. 启动游戏并停留在主菜单即可（无需打开游戏内服务器列表）。",
                "2. 在列表中勾选参与轮询的服务器（可通过“全选 / 全不选”快速操作）。",
                "3. 点击“↻ 刷新人数”可提前查看各服在线情况：红色表示满员，绿色表示有空位（点击“人数”列标题可将有空位的服务器排到最前）。",
                "4. ★ 切回游戏后按 ~ 打开控制台（必须手动打开，否则无法写入连接指令）。",
                "5. 保持控制台打开，直接按 " + hkStart + " 开始：",
                "   工具会按列表顺序依次发送 connect 指令，逐一尝试进入。",
                "6. 进服后按 " + hkStart + " 停止（如勾选了 OCR 自动识别，检测到加载画面后会自动停止）。",
            };
            flow = new string[] {
                "1. 工具在做什么",
                "· 将已勾选的服务器组成轮询队列，按顺序轮流发送 connect 指令。",
                "· 列表第一列带有 ▶ 标记的服务器，即为当前正在尝试连接的目标。",
                "· 每个服务器均共享统一发送节拍，因此勾选数量越多，单个服务器的重试间隔越长。",
                "",
                "2. 挑选建议",
                "· 建议勾选 3~6 个服务器：既能错峰进服，又不会让单个服的重试周期过长。",
                "· 点击“人数”列标题即可快速把有空位的服务器置顶。",
                "",
                "3. 添加与删除",
                "· 添加：在“IP:端口”框填入地址后点击“★ 加入收藏”，或复制地址后点击“粘贴加服”。",
                "· 删除：选中目标行后点击“删除选中”（或按 Delete 键）。",
            };
        }

        using (var f = new Form())
        {
            f.Text = "使用指南 · RX 工具箱";
            f.StartPosition = FormStartPosition.CenterParent;
            f.FormBorderStyle = FormBorderStyle.FixedDialog;
            f.MinimizeBox = false;
            f.MaximizeBox = false;
            f.ShowInTaskbar = false;
            f.BackColor = Color.White;
            f.ClientSize = new Size(784, 670);

            var lbTitle = new Label();
            lbTitle.Text = "当前模式：" + title;
            lbTitle.Font = TrackFont("Microsoft YaHei UI", 11f, FontStyle.Bold);
            lbTitle.ForeColor = Color.FromArgb(20, 60, 120);
            lbTitle.SetBounds(16, 10, 752, 26);
            f.Controls.Add(lbTitle);

            var tabsG = new ThemedTabControl();
            // 顶部警示条：控制台必须手动开 + 剪贴板会被刷屏
            var warn = new Label();
            warn.SetBounds(16, 40, 752, 40);
            warn.BackColor = Color.FromArgb(255, 240, 236);
            warn.ForeColor = Color.FromArgb(176, 40, 26);
            warn.Font = TrackFont("Microsoft YaHei UI", 10f, FontStyle.Bold);
            warn.TextAlign = ContentAlignment.MiddleLeft;
            warn.Padding = new Padding(10, 0, 10, 0);
            if (m == 0)
            {
                warn.BackColor = Color.FromArgb(255, 249, 232);
                warn.ForeColor = Color.FromArgb(140, 90, 10);
                warn.Text = "⚠ 手动档无需控制台。请保持游戏窗口在前台，工具仅负责发送回车键。";
            }
            else
            {
                warn.Text = "⚠ 开始前请先按 ~ 打开游戏控制台，外部工具无法代为呼出控制台";
            }
            f.Controls.Add(warn);

            tabsG.SetBounds(12, 88, 760, 524);
            tabsG.Font = TrackFont("Microsoft YaHei UI", 9.5f, FontStyle.Regular);
            f.Controls.Add(tabsG);

            var p1 = new TabPage("① 按步骤操作");
            p1.BackColor = Color.White;
            p1.Padding = new Padding(10);
            var tbSteps = new TextBox();
            tbSteps.Multiline = true;
            tbSteps.ReadOnly = true;
            tbSteps.BorderStyle = BorderStyle.None;
            tbSteps.ScrollBars = ScrollBars.Vertical;
            tbSteps.BackColor = Color.White;
            tbSteps.Dock = DockStyle.Fill;
            tbSteps.Font = TrackFont("Microsoft YaHei UI", 10.5f, FontStyle.Regular);
            tbSteps.Text = string.Join(Environment.NewLine, steps);
            p1.Controls.Add(tbSteps);
            tabsG.TabPages.Add(p1);

            var p2 = new TabPage("② 原理与工作流");
            p2.BackColor = Color.FromArgb(253, 251, 245);
            p2.Padding = new Padding(10);
            var tbFlow = new TextBox();
            tbFlow.Multiline = true;
            tbFlow.ReadOnly = true;
            tbFlow.BorderStyle = BorderStyle.None;
            tbFlow.ScrollBars = ScrollBars.Vertical;
            tbFlow.BackColor = Color.FromArgb(253, 251, 245);
            tbFlow.Dock = DockStyle.Fill;
            tbFlow.Font = TrackFont("Microsoft YaHei UI", 9.5f, FontStyle.Regular);
            tbFlow.Text = string.Join(Environment.NewLine, flow);
            p2.Controls.Add(tbFlow);
            tabsG.TabPages.Add(p2);

            var p3 = new TabPage("③ 如何收藏服务器");
            p3.BackColor = Color.White;
            p3.Padding = new Padding(10);

            Image imgL = LoadGuideImg("guide_srv.jpg", ref guideImgL);
            Image imgR = LoadGuideImg("guide_ip.png", ref guideImgR);

            var lb31 = new Label();
            lb31.Text = (imgL != null)
                ? "① 在游戏内点击目标服务器，右上角会弹出该服信息栏（下图红框）"
                : "① 在游戏内服务器列表点击目标服务器，右上角会弹出该服信息栏";
            lb31.Font = TrackFont("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            lb31.ForeColor = Color.FromArgb(20, 60, 120);
            lb31.SetBounds(10, 10, 720, 22);
            p3.Controls.Add(lb31);

            if (imgL != null)
            {
                var pbL = new PictureBox();
                pbL.SizeMode = PictureBoxSizeMode.Zoom;
                pbL.BorderStyle = BorderStyle.FixedSingle;
                pbL.Image = imgL;
                pbL.SetBounds(10, 38, 340, 191);
                p3.Controls.Add(pbL);
            }

            var lb32 = new Label();
            lb32.Text = (imgL != null)
                ? ("左侧：游戏内的服务器列表，单击任意一行\r\n"
                   + "右上角：对应的服务器详情窗口\r\n\r\n"
                   + "选中服务器后，右上角详情栏才会显示完整的服务器名称和 IP:端口。")
                : ("进入游戏 → Play → 服务器列表，单击选中你常去的服务器；\r\n"
                   + "选中后右侧/右上角详情栏就会显示完整的服务器名称和 IP:端口。");
            lb32.Font = TrackFont("Microsoft YaHei UI", 9f, FontStyle.Regular);
            lb32.ForeColor = Color.FromArgb(70, 80, 95);
            lb32.SetBounds(imgL != null ? 360 : 10, 40, imgL != null ? 380 : 720, imgL != null ? 100 : 54);
            p3.Controls.Add(lb32);

            var lb33 = new Label();
            lb33.Text = (imgR != null)
                ? "② 鼠标选中黄框内的 IP:端口，按 Ctrl+C 复制"
                : "② 鼠标拖选服务器详情栏里的 IP:端口（如 127.0.0.1:27015），按 Ctrl+C 复制";
            lb33.Font = TrackFont("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
            lb33.ForeColor = Color.FromArgb(20, 60, 120);
            lb33.SetBounds(10, (imgL != null || imgR != null) ? 236 : 114, 720, 22);
            p3.Controls.Add(lb33);

            if (imgR != null)
            {
                var pbR = new PictureBox();
                pbR.SizeMode = PictureBoxSizeMode.Zoom;
                pbR.BorderStyle = BorderStyle.FixedSingle;
                pbR.Image = imgR;
                pbR.SetBounds(10, 262, 720, 172);
                p3.Controls.Add(pbR);
            }

            var lb34 = new Label();
            lb34.Text = "③ 回到工具点击“粘贴加服”（或粘贴到“IP:端口”输入框后点击“★ 加入收藏”）。\r\n"
                + "    程序会自动探测真实服务器名称并保存；如需删除，选中该行点击“删除选中”即可。";
            lb34.Font = TrackFont("Microsoft YaHei UI", 9f, FontStyle.Regular);
            lb34.ForeColor = Color.FromArgb(70, 80, 95);
            lb34.SetBounds(10, (imgR != null) ? 440 : ((imgL != null) ? 300 : 148), 720, 56);
            p3.Controls.Add(lb34);

            tabsG.TabPages.Add(p3);

            var bOk = new Button();
            bOk.Text = "知道了";
            bOk.SetBounds(676, 622, 92, 32);
            bOk.DialogResult = DialogResult.OK;
            f.Controls.Add(bOk);
            f.AcceptButton = bOk;
            f.CancelButton = bOk;

            ThemeDialog(f);
            if (IsDark)
            {
                warn.BackColor = m == 0 ? Color.FromArgb(62, 50, 24) : Color.FromArgb(68, 32, 28);
                warn.ForeColor = m == 0 ? Color.FromArgb(245, 210, 120) : Color.FromArgb(248, 160, 148);
            }
            Native.Log("guide shown, mode=" + m);
            f.ShowDialog(this);
        }
    }

    void OnModeChanged()
    {
        if (suppressModeEvent) return;
        Native.Log("mode changed -> " + JoinModeIdx);
        if (autoOn || typingOn)
        {
            StopActiveJoin("已切换挤服模式，当前挤服已自动停止", "stop: mode changed");
        }
        ApplyModeUi();
        SaveSettings();
        RefreshUi(true);
    }

    bool BuildTargets(int mode, bool quiet)
    {
        var nextTargets = new List<string>();
        if (mode == 2)
        {
            if (lvSrv != null)
                for (int i = 0; i < lvSrv.Items.Count; i++)
                    if (lvSrv.Items[i].Checked)
                    {
                        string a = Convert.ToString(lvSrv.Items[i].Tag);
                        if (!string.IsNullOrEmpty(a)) nextTargets.Add(a);
                    }
            if (nextTargets.Count == 0)
            {
                if (!quiet)
                {
                    toast.ShowText("收藏轮询：先在列表里勾选至少一个服务器");
                    Native.Log("start: poll but nothing checked");
                }
                return false;
            }
        }
        else
        {
            string a = SelectedAddr();
            if (a.Length == 0)
            {
                string typed = (txtNewIp == null) ? "" : txtNewIp.Text.Trim();
                string h; int p;
                if (ParseHostPort(typed, out h, out p)) a = h + ":" + p;
            }
            if (a.Length == 0 && favList.Length > 0) a = AddrOf(favList[0]);
            if (a.Length == 0)
            {
                if (!quiet) toast.ShowText("单一 IP：先在列表里点选一个服务器，或填 IP:端口");
                return false;
            }
            nextTargets.Add(a);
        }
        targets = nextTargets;
        if (multiIdx >= targets.Count) multiIdx = 0;
        if (!quiet)
        {
            Native.Log("start: mode=" + (mode == 2 ? "poll" : "one") + " targets=" + targets.Count
                + " first=" + targets[0]);
        }
        return true;
    }

    void SyncActiveTargets()
    {
        if (!autoOn || !typingOn) return;
        int m = JoinModeIdx;
        if (m == 1 || m == 2)
        {
            if (!BuildTargets(m, true))
            {
                StopActiveJoin("目标服务器已全部取消，挤服自动停止", "stop: targets empty");
            }
        }
    }

    void UpdateStartButton()
    {
        if (btnAuto == null) return;
        bool on = (autoOn || typingOn);
        btnAuto.Text = on ? ("■ 停止挤服 (" + KeyName(2) + ")") : ("▶ 开始挤服 (" + KeyName(2) + ")");
        if (IsDark)
        {
            btnAuto.BackColor = on ? Color.FromArgb(68, 28, 34) : Color.FromArgb(27, 47, 74);
            btnAuto.ForeColor = on ? Color.FromArgb(255, 161, 152) : Color.FromArgb(121, 192, 255);
            btnAuto.FlatAppearance.BorderColor = on ? Color.FromArgb(185, 68, 72) : Color.FromArgb(56, 139, 253);
            btnAuto.FlatAppearance.MouseOverBackColor = on ? Color.FromArgb(88, 34, 42) : Color.FromArgb(35, 60, 94);
        }
        else
        {
            btnAuto.BackColor = on ? Color.FromArgb(254, 215, 215) : Color.FromArgb(224, 240, 255);
            btnAuto.ForeColor = on ? Color.FromArgb(153, 27, 27) : Color.FromArgb(12, 62, 130);
            btnAuto.FlatAppearance.BorderColor = on ? Color.FromArgb(248, 150, 150) : Color.FromArgb(140, 185, 240);
            btnAuto.FlatAppearance.MouseOverBackColor = Color.Empty;
        }
    }

    // 统一权威停止入口：确保任何原因停止时都还原输入法、还原剪贴板、复位全部状态
    void StopActiveJoin(string toastMsg, string logReason)
    {
        bool wasTyping = typingOn;
        ocrHoldSend = false;
        ocrLoadingHits = 0;
        autoOn = false;
        typingOn = false;
        currentTargetAddr = "";

        if (wasTyping)
        {
            if (savedLayout != IntPtr.Zero)
            {
                IntPtr gameBack = Native.FindGameWindow();
                if (gameBack != IntPtr.Zero)
                {
                    Native.RestoreLayout(gameBack, savedLayout);
                    Native.Log("ime: restored");
                }
                savedLayout = IntPtr.Zero;
            }
            RestoreClipboardAfterStop();
        }

        RefreshActiveTargetRowHighlight();
        UpdateStartButton();
        if (!string.IsNullOrEmpty(logReason)) Native.Log(logReason);
        if (!string.IsNullOrEmpty(toastMsg)) toast.ShowText(toastMsg);
        RefreshUi(true);
    }

    // 停止挤服后收尾：剪贴板里如果还是我们写的 connect，就还原成玩家原来的内容（没有就清空）
    void RestoreClipboardAfterStop()
    {
        try
        {
            string cur = "";
            try { if (Clipboard.ContainsText()) cur = Clipboard.GetText(); } catch { }
            bool ours = cur != null && cur.StartsWith("connect ", StringComparison.OrdinalIgnoreCase) && cur.Length < 120;
            if (!ours)
            {
                // 玩家自己复制了新内容，不动它
                return;
            }
            if (!string.IsNullOrEmpty(savedClipboardText))
            {
                if (TrySetClipboardText(savedClipboardText)) Native.Log("clipboard: restored original");
                else { try { Clipboard.Clear(); } catch { } }
            }
            else
            {
                try { Clipboard.Clear(); Native.Log("clipboard: cleared connect residue"); } catch { }
            }
        }
        catch (Exception ex) { Native.Log("clipboard restore EX: " + ex.Message); }
    }

    void ToggleStart()
    {
        if (autoOn || typingOn)
        {
            StopActiveJoin("挤服：已停止", "stop: user toggle");
            return;
        }

        int m = JoinModeIdx;
        tapOcrFail = 0;
        tapOcrAt = DateTime.MinValue;
        lastEnter = DateTime.MinValue;

        if (m == 0)
        {
            typingOn = false;
            autoOn = true;
            currentTargetAddr = "";
            UpdateStartButton();
            toast.ShowText("手动档已开启：保持游戏在前台，按 " + KeyName(2) + " 可随时停止");
            Native.Log("start: mode=tap");
            RefreshUi(true);
            return;
        }

        if (!BuildTargets(m, false)) return;

        try
        {
            if (Clipboard.ContainsText())
            {
                string curClip = Clipboard.GetText();
                if (!string.IsNullOrEmpty(curClip) && !curClip.StartsWith("connect ", StringComparison.OrdinalIgnoreCase))
                    savedClipboardText = curClip;
            }
        }
        catch { }

        typingOn = true;
        autoOn = true;
        multiIdx = 0;
        currentTargetAddr = targets[0];

        IntPtr gw = Native.FindGameWindow();
        if (gw != IntPtr.Zero)
        {
            if (Native.GetForegroundWindow() != gw)
            {
                try
                {
                    if (Native.IsIconic(gw)) Native.ShowWindow(gw, 9);
                    else Native.ShowWindow(gw, 5);
                    Native.SetForegroundWindow(gw);
                }
                catch { }
            }
            savedLayout = Native.SwitchToEnglish(gw);
            Native.Log("ime: switched to english, old=" + savedLayout.ToInt64());
        }
        else
        {
            Native.Log("paste: game window not found at start");
        }

        RefreshActiveTargetRowHighlight();
        UpdateStartButton();
        string what = (targets.Count > 1)
            ? ("正在轮询 " + targets.Count + " 个服")
            : ("正在挤 " + targets[0]);
        toast.ShowText("已开启（打开游戏控制台 ~ 即可自动粘贴），" + what);
        RefreshUi(true);
    }

    int IntervalMs
    {
        get
        {
            if (cmbSpeed == null) return 200;
            switch (cmbSpeed.SelectedIndex)
            {
                case 0: return 100;
                case 1: return 200;
                case 2: return 300;
                case 3: return 500;
                default: return 900;
            }
        }
    }

    // ===== Windows 自带 OCR（后台非阻塞 + 引擎缓存 + 严格释放）=====
    static T WaitOp<T>(Windows.Foundation.IAsyncOperation<T> op, int timeoutMs)
    {
        if (op == null) return default(T);
        using (var done = new ManualResetEventSlim(false))
        {
            T result = default(T);
            op.Completed = delegate(Windows.Foundation.IAsyncOperation<T> o, Windows.Foundation.AsyncStatus s)
            {
                try
                {
                    if (s == Windows.Foundation.AsyncStatus.Completed)
                        result = o.GetResults();
                }
                catch { }
                finally
                {
                    try { done.Set(); } catch { }
                }
            };
            if (!done.Wait(timeoutMs))
            {
                try { op.Cancel(); } catch { }
            }
            return result;
        }
    }

    // 位图 → OCR 文本（scale>1 时放大，小字更易识别）
    string OcrBitmapText(Bitmap bmp, int scale)
    {
        if (!ocrEngineInitTried)
        {
            ocrEngineInitTried = true;
            try { cachedOcrEngine = OcrEngine.TryCreateFromUserProfileLanguages(); }
            catch { cachedOcrEngine = null; }
        }
        if (cachedOcrEngine == null) return null;

        byte[] png;
        int w = bmp.Width;
        int nw = Math.Max(1, w * Math.Max(1, scale));
        int nh = Math.Max(1, bmp.Height * nw / w);
        using (var scaled = new Bitmap(bmp, nw, nh))
        using (var ms = new MemoryStream())
        {
            scaled.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            png = ms.ToArray();
        }

        using (var ras = new InMemoryRandomAccessStream())
        {
            using (var writer = new DataWriter(ras))
            {
                writer.WriteBytes(png);
                WaitOp(writer.StoreAsync(), 2500);
                try { writer.DetachStream(); } catch { }
            }
            ras.Seek(0);

            var decoder = WaitOp(BitmapDecoder.CreateAsync(ras), 2500);
            if (decoder == null) return null;
            using (var sb = WaitOp(decoder.GetSoftwareBitmapAsync(), 2500))
            {
                if (sb == null) return null;
                var res = WaitOp(cachedOcrEngine.RecognizeAsync(sb), 3000);
                if (res == null) return null;

                var sbText = new StringBuilder();
                foreach (var line in res.Lines) { sbText.Append(line.Text); sbText.Append('\n'); }
                return sbText.ToString();
            }
        }
    }

    string OcrGameWindow(IntPtr hwnd)
    {
        try
        {
            Native.RECT r;
            if (!Native.GetWindowRect(hwnd, out r)) return null;
            int w = r.R - r.L, h = r.B - r.T;
            if (w < 400 || h < 300) return null;

            using (var bmp = new Bitmap(w, h))
            {
                using (var g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(r.L, r.T, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
                // 以前统一缩到 1280 宽，1080p 下左侧那行灰字会被缩糊
                int scale = w >= 1280 ? 1 : 1280 / Math.Max(1, w);
                return OcrBitmapText(bmp, Math.Max(1, scale));
            }
        }
        catch (Exception ex)
        {
            Native.Log("ocr fail: " + ex.Message);
            return null;
        }
    }

    // 读条界面左侧那行「正在加载插件中…」：字小又灰，整屏 OCR 常读不到。
    // 这里单独截左中一条带、放大 3 倍再认一次，只用来找这几个词。
    string OcrLoadingBand(IntPtr hwnd)
    {
        try
        {
            Native.RECT r;
            if (!Native.GetWindowRect(hwnd, out r)) return null;
            int w = r.R - r.L, h = r.B - r.T;
            if (w < 400 || h < 300) return null;

            int bx = r.L + (int)(w * 0.02);
            int by = r.T + (int)(h * 0.32);
            int bw = Math.Max(200, (int)(w * 0.46));
            int bh = Math.Max(80, (int)(h * 0.22));

            using (var bmp = new Bitmap(bw, bh))
            {
                using (var g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(bx, by, 0, 0, new Size(bw, bh), CopyPixelOperation.SourceCopy);
                return OcrBitmapText(bmp, 3);
            }
        }
        catch { return null; }
    }

    string lastOcrRaw = "";          // 最近一次 OCR 原文（RX_OCR_DUMP=1 时写日志）
    string lastOcrRawScene = "";

    string OcrScene()
    {
        IntPtr gw = Native.FindGameWindow();
        if (gw == IntPtr.Zero) return "nogame";
        string text = OcrGameWindow(gw);
        if (text == null) return "fail";
        lastOcrRaw = text;

        // OCR 结果里中文字之间常带空格（"服 务 器 列 表"），先去掉空白再匹配
        string flat = Regex.Replace(text, "\\s+", "");

        // ⓪ 主菜单（开始新游戏/寻找多人游戏/插件/副本/存档/回放/选项/退出）
        //    满员被踢回来就是这个画面，绝对不能当成"已进服"
        if (flat.IndexOf("开始新游戏", StringComparison.Ordinal) >= 0
            || flat.IndexOf("寻找多人游戏", StringComparison.Ordinal) >= 0
            || flat.IndexOf("寻找服务器", StringComparison.Ordinal) >= 0
            || flat.IndexOf("开始游戏", StringComparison.Ordinal) >= 0
            || flat.IndexOf("创意工坊", StringComparison.Ordinal) >= 0)
            return "menu";

        // ① 服务器列表 / 收藏夹：只要还看得见列表页，就绝不算"进服"。
        //    挤服时屏上同时有列表 + 控制台，这一条是防误判的第一道闸。
        if (flat.IndexOf("服务器列表", StringComparison.Ordinal) >= 0
            || flat.IndexOf("当服务器有空位", StringComparison.Ordinal) >= 0
            || flat.IndexOf("加入游戏", StringComparison.Ordinal) >= 0
            || flat.IndexOf("服务器名称", StringComparison.Ordinal) >= 0
            || flat.IndexOf("收藏夹", StringComparison.Ordinal) >= 0
            || flat.IndexOf("历史记录", StringComparison.Ordinal) >= 0)
            return "list";

        // ② 强进服信号：加载画面上的"加载插件/加载地图"。
        //    这两句只在读条时出现，控制台日志里没有，所以放在控制台判断之前。
        if (flat.IndexOf("加载插件", StringComparison.Ordinal) >= 0
            || flat.IndexOf("加载地图", StringComparison.Ordinal) >= 0
            || flat.IndexOf("正在加载插件", StringComparison.Ordinal) >= 0
            || flat.IndexOf("正在加载地图", StringComparison.Ordinal) >= 0)
            return "loading_plugin";


        // ③ 控制台日志 / 满员提示：挤服全程控制台都开着，屏上是 "Connecting to…"
        //    "Disconnected…" 这类文本，里面混着 Loading 字样也不能当读条。
        if (flat.IndexOf("CONNREJECT", StringComparison.OrdinalIgnoreCase) >= 0
            || flat.IndexOf("Disconnected", StringComparison.OrdinalIgnoreCase) >= 0
            || flat.IndexOf("Connectingto", StringComparison.OrdinalIgnoreCase) >= 0
            || flat.IndexOf("Attemptedtoprecache", StringComparison.OrdinalIgnoreCase) >= 0
            || flat.IndexOf("WorkshopID", StringComparison.OrdinalIgnoreCase) >= 0
            || flat.IndexOf("人数已满", StringComparison.Ordinal) >= 0
            || flat.IndexOf("服务器已满", StringComparison.Ordinal) >= 0)
            return "console";

        // ④ 弱信号：只有读条界面才有的词，同样排除控制台干扰
        if (flat.IndexOf("加载中", StringComparison.Ordinal) >= 0
            || flat.IndexOf("正在加载", StringComparison.Ordinal) >= 0
            || flat.IndexOf("Loadingmap", StringComparison.OrdinalIgnoreCase) >= 0
            || flat.IndexOf("Loadingaddons", StringComparison.OrdinalIgnoreCase) >= 0)
            return "loading";

        if (flat.IndexOf("连接服务器", StringComparison.Ordinal) >= 0
            || flat.IndexOf("Connecting", StringComparison.OrdinalIgnoreCase) >= 0)
            return "connecting";

        // 整屏没读到读条文字 → 单独认左中那条带（放大 3 倍再 OCR）
        string band = OcrLoadingBand(gw);
        if (!string.IsNullOrEmpty(band))
        {
            string bf = Regex.Replace(band, "\\s+", "");
            if (bf.IndexOf("加载插件", StringComparison.Ordinal) >= 0
                || bf.IndexOf("加载地图", StringComparison.Ordinal) >= 0)
            {
                lastOcrRaw = band;
                return "loading_plugin";
            }
        }

        if (flat.Length < 8) return "blank";
        return "other";
    }

    void TriggerOcrCheckAsync()
    {
        if (Interlocked.CompareExchange(ref ocrBusy, 1, 0) != 0) return;
        tapOcrAt = DateTime.Now;
        Task.Run(delegate
        {
            string scene = "fail";
            try { scene = OcrScene(); }
            catch { scene = "fail"; }
            finally { Interlocked.Exchange(ref ocrBusy, 0); }

            SafeBeginInvoke(delegate
            {
                if (!autoOn) return;
                if (scene != lastOcrScene) { lastOcrScene = scene; Native.Log("enter-mode: ocr scene=" + scene); }
                if (scene != lastOcrRawScene)
                {
                    lastOcrRawScene = scene;
                    string dump = (lastOcrRaw ?? "").Replace("\r", " ").Replace("\n", " | ");
                    if (dump.Length > 300) dump = dump.Substring(0, 300);
                    Native.Log("ocr dump [" + scene + "] " + dump);
                }
                if (scene == "loading_plugin")
                {
                    // 「加载插件中」是读条界面常驻文案，连续两帧读到才认进服。
                    // 第一帧就先停止发 connect，避免在确认期间又把人踢回菜单。
                    ocrLoadingHits++;
                    ocrHoldSend = true;
                    Native.Log("enter-mode: ocr 加载插件 hit " + ocrLoadingHits + "/2 (暂停发送)");
                    if (ocrLoadingHits < 2) return;
                    ocrLoadingHits = 0;
                    StopActiveJoin("已进服（加载插件画面），已自动停止", "enter-mode: ocr 加载插件 x2 -> joined, stop");
                    return;
                }
                if (scene == "loading")
                {
                    // 弱信号（加载中/Loadingmap…）不常驻，单帧不可信，只记日志并恢复发送
                    Native.Log("enter-mode: ocr weak loading ignored");
                    ocrLoadingHits = 0;
                    ocrHoldSend = false;
                    return;
                }
                ocrLoadingHits = 0;
                ocrHoldSend = false;
                // menu / list / console / other 一律视为"还没进服"，什么都不做
                if (scene == "fail")
                {
                    tapOcrFail++;
                    Native.Log("enter-mode: ocr fail " + tapOcrFail + "/3");
                    if (tapOcrFail >= 3)
                    {
                        tapOcrFail = 0;
                        if (chkOcr != null) chkOcr.Checked = false;
                        SaveSettings();
                        Native.Log("enter-mode: ocr disabled automatically");
                        toast.ShowText("当前系统不支持 OCR，已自动关闭进服识别（按 " + KeyName(2) + " 手动停）");
                    }
                }
                else tapOcrFail = 0;
            });
        });
    }
}
