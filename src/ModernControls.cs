using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

public class NavItem
{
    public string Key;
    public string Text;
    public string Group;      // 分组标题（仅首位出现时画组头），"BOTTOM" 表示固定贴底
    public NavIconKind Icon;
    public int BadgeCount;
    public bool HasDot;
    public bool Visible = true;
}

/// <summary>
/// 现代可折叠左侧导航轨（展开 172px / 图标模式 54px），纯 GDI+ 双缓冲，零依赖。
/// </summary>
public class NavRail : Control
{
    public readonly List<NavItem> Items = new List<NavItem>();
    public string SelectedKey = "";
    public bool Collapsed = false;
    public event Action<string> SelectionChanged;
    public event EventHandler CollapseToggled;

    const int ExpandedW = 172;
    const int CollapsedW = 54;
    const int ItemH = 34;
    const int GroupH = 24;
    const int ToggleBtnH = 34;
    int curGroupH = GroupH;
    int curCollapsedGroupGap = 8;

    int hoverIdx = -1;
    bool hoverToggle = false;
    Rectangle[] itemRects = new Rectangle[0];
    Rectangle toggleRect = Rectangle.Empty;
    ToolTip tip = new ToolTip();
    string lastTipText = "";

    Font fItem, fItemBold, fGroup, fBadge, fBrand;

    public NavRail()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Width = ExpandedW;
        fItem = new Font("Microsoft YaHei UI", 9.2f, FontStyle.Regular);
        fItemBold = new Font("Microsoft YaHei UI", 9.2f, FontStyle.Bold);
        fGroup = new Font("Microsoft YaHei UI", 7.8f, FontStyle.Bold);
        fBadge = new Font("Microsoft YaHei UI", 7.8f, FontStyle.Regular);
        fBrand = new Font("Microsoft YaHei UI", 9.5f, FontStyle.Bold);
        tip.InitialDelay = 250;
        tip.AutoPopDelay = 8000;
    }

    public int PreferredWidth { get { return Collapsed ? CollapsedW : ExpandedW; } }

    public void SetCollapsed(bool col)
    {
        if (Collapsed == col) return;
        Collapsed = col;
        Width = PreferredWidth;
        Invalidate();
        if (CollapseToggled != null) CollapseToggled(this, EventArgs.Empty);
    }

    public void SelectKey(string key, bool raiseEvent)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (SelectedKey == key) return;
        SelectedKey = key;
        Invalidate();
        if (raiseEvent && SelectionChanged != null) SelectionChanged(key);
    }

    public NavItem FindItem(string key)
    {
        for (int i = 0; i < Items.Count; i++)
            if (Items[i].Key == key) return Items[i];
        return null;
    }

    int CalcNeededHeight(int topCount, int topGroups, int botCount, int itemH, int groupH, int toggleH, int rowGap, int groupGap, int colGap)
    {
        int hTop = topCount * (itemH + rowGap);
        if (topGroups > 0)
        {
            if (!Collapsed) hTop += topGroups * groupH + (topGroups - 1) * groupGap;
            else hTop += (topGroups - 1) * colGap;
        }
        int hBot = botCount * (itemH + rowGap) + toggleH + 14;
        return hTop + hBot;
    }

    void ComputeRects()
    {
        int n = Items.Count;
        if (itemRects.Length != n) itemRects = new Rectangle[n];
        for (int i = 0; i < n; i++) itemRects[i] = Rectangle.Empty;

        int w = ClientSize.Width;
        int h = ClientSize.Height;
        int padX = Collapsed ? 7 : 8;
        int rowW = Math.Max(20, w - padX * 2);

        int topCount = 0, topGroups = 0, botCount = 0;
        string cntGroup = null;
        for (int i = 0; i < n; i++)
        {
            NavItem it = Items[i];
            if (!it.Visible) continue;
            if (it.Group == "BOTTOM") { botCount++; continue; }
            topCount++;
            if (!string.IsNullOrEmpty(it.Group) && it.Group != cntGroup)
            {
                topGroups++;
                cntGroup = it.Group;
            }
        }

        int effItemH = ItemH;
        int effGroupH = GroupH;
        int effToggleH = ToggleBtnH;
        int effRowGap = 2;
        int effGroupGap = 6;
        int effColGap = 8;
        int avail = Math.Max(120, h - 48 - 6);
        if (CalcNeededHeight(topCount, topGroups, botCount, effItemH, effGroupH, effToggleH, effRowGap, effGroupGap, effColGap) > avail)
        {
            effRowGap = 1;
            effGroupGap = 2;
            effColGap = 4;
            while (CalcNeededHeight(topCount, topGroups, botCount, effItemH, effGroupH, effToggleH, effRowGap, effGroupGap, effColGap) > avail
                   && (effItemH > 20 || effGroupH > 15 || effToggleH > 26))
            {
                if (effItemH > 20) effItemH--;
                if (CalcNeededHeight(topCount, topGroups, botCount, effItemH, effGroupH, effToggleH, effRowGap, effGroupGap, effColGap) > avail && effGroupH > 15) effGroupH--;
                if (CalcNeededHeight(topCount, topGroups, botCount, effItemH, effGroupH, effToggleH, effRowGap, effGroupGap, effColGap) > avail && effToggleH > 26) effToggleH--;
            }
        }
        curGroupH = effGroupH;
        curCollapsedGroupGap = effColGap;

        // 顶部品牌区预留 48px
        int yTop = 48;
        string lastGroup = null;
        for (int i = 0; i < n; i++)
        {
            NavItem it = Items[i];
            if (!it.Visible || it.Group == "BOTTOM") continue;
            if (!string.IsNullOrEmpty(it.Group) && it.Group != lastGroup)
            {
                if (lastGroup != null) yTop += Collapsed ? effColGap : effGroupGap;
                if (!Collapsed) yTop += effGroupH;
                lastGroup = it.Group;
            }
            itemRects[i] = new Rectangle(padX, yTop, rowW, effItemH);
            yTop += effItemH + effRowGap;
        }

        // 底部固定：先留最底部的折叠按钮
        int yBot = h - effToggleH - 8;
        toggleRect = new Rectangle(padX, yBot, rowW, effToggleH);
        yBot -= 6;

        for (int i = n - 1; i >= 0; i--)
        {
            NavItem it = Items[i];
            if (!it.Visible || it.Group != "BOTTOM") continue;
            yBot -= effItemH;
            itemRects[i] = new Rectangle(padX, yBot, rowW, effItemH);
            yBot -= effRowGap;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(ThemePalette.Rail);
        ComputeRects();

        int w = ClientSize.Width;
        int h = ClientSize.Height;

        // 右侧细分割线
        using (var sep = new Pen(ThemePalette.BorderSubtle, 1f))
            g.DrawLine(sep, w - 1, 0, w - 1, h);

        // 顶部品牌标记
        int bx = Collapsed ? (w - 26) / 2 : 12;
        Rectangle logoRc = new Rectangle(bx, 11, 26, 26);
        using (var lb = new SolidBrush(ThemePalette.Accent))
            Native.FillRoundedRect(g, lb, logoRc, 7);
        TextRenderer.DrawText(g, "RX", fBrand, logoRc, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

        if (!Collapsed)
        {
            string navBrandTitle = "RX 工具箱";
            string navBrandSub = MainForm.APP_VERSION + " 玩家版";
            TextRenderer.DrawText(g, navBrandTitle, fBrand, new Rectangle(44, 9, w - 52, 18), ThemePalette.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            TextRenderer.DrawText(g, navBrandSub, fBadge, new Rectangle(44, 25, w - 52, 14), ThemePalette.TextMuted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        // 绘制顶部各分组及项
        string lastGroup = null;
        for (int i = 0; i < Items.Count; i++)
        {
            NavItem it = Items[i];
            if (!it.Visible) continue;
            Rectangle rc = itemRects[i];
            if (rc.IsEmpty) continue;

            if (it.Group != "BOTTOM" && !string.IsNullOrEmpty(it.Group) && it.Group != lastGroup)
            {
                if (!Collapsed)
                {
                    Rectangle gr = new Rectangle(14, rc.Y - curGroupH + 3, w - 24, curGroupH - 3);
                    TextRenderer.DrawText(g, it.Group, fGroup, gr, ThemePalette.TextMuted,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                }
                else if (lastGroup != null)
                {
                    int sepY = rc.Y - Math.Max(3, curCollapsedGroupGap / 2 + 1);
                    using (var gp = new Pen(ThemePalette.BorderSubtle, 1f))
                        g.DrawLine(gp, 12, sepY, w - 12, sepY);
                }
                lastGroup = it.Group;
            }

            bool sel = (it.Key == SelectedKey);
            bool hov = (!sel && i == hoverIdx);

            if (sel)
            {
                using (var sb = new SolidBrush(ThemePalette.Selected))
                    Native.FillRoundedRect(g, sb, rc, 7);
                int indH = Math.Max(6, rc.Height - 12);
                int indY = rc.Y + (rc.Height - indH) / 2;
                using (var ab = new SolidBrush(ThemePalette.Accent))
                    Native.FillRoundedRect(g, ab, new Rectangle(rc.X + 1, indY, 3, indH), 2);
            }
            else if (hov)
            {
                using (var hb = new SolidBrush(ThemePalette.Hover))
                    Native.FillRoundedRect(g, hb, rc, 7);
            }

            Color iconCol = sel ? ThemePalette.Accent : (hov ? ThemePalette.TextPrimary : ThemePalette.TextSecondary);
            if (it.Icon == NavIconKind.Star && sel) iconCol = ThemePalette.StarActive;
            int iconX = Collapsed ? (rc.X + (rc.Width - 16) / 2) : (rc.X + 11);
            Rectangle iconRc = new Rectangle(iconX, rc.Y + (rc.Height - 16) / 2, 16, 16);
            UiIcons.Draw(g, it.Icon, iconRc, iconCol, sel && it.Icon == NavIconKind.Star);

            if (!Collapsed)
            {
                int rightKeep = 8;
                if (it.BadgeCount > 0)
                {
                    string cnt = it.BadgeCount.ToString();
                    Size csz = TextRenderer.MeasureText(cnt, fBadge);
                    int bw = Math.Max(22, csz.Width + 8);
                    int bh = 18;
                    Rectangle brc = new Rectangle(rc.Right - bw - 6, rc.Y + (rc.Height - bh) / 2, bw, bh);
                    using (var bb = new SolidBrush(sel ? ThemePalette.Accent : ThemePalette.SurfaceAlt))
                        Native.FillRoundedRect(g, bb, brc, 9);
                    TextRenderer.DrawText(g, cnt, fBadge, brc, sel ? Color.White : ThemePalette.TextMuted,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                    rightKeep = bw + 12;
                }
                else if (it.HasDot)
                {
                    using (var db = new SolidBrush(ThemePalette.Success))
                        g.FillEllipse(db, rc.Right - 14, rc.Y + (rc.Height - 7) / 2, 7, 7);
                    rightKeep = 18;
                }

                Color fg = sel ? ThemePalette.AccentSoftFg : (hov ? ThemePalette.TextPrimary : ThemePalette.TextSecondary);
                Rectangle tr = new Rectangle(rc.X + 34, rc.Y, Math.Max(20, rc.Width - 34 - rightKeep), rc.Height);
                TextRenderer.DrawText(g, it.Text ?? "", sel ? fItemBold : fItem, tr, fg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
            else if (it.HasDot)
            {
                using (var db = new SolidBrush(ThemePalette.Success))
                    g.FillEllipse(db, rc.Right - 10, rc.Y + 6, 6, 6);
            }
        }

        // 底部分割线 + 折叠切换按钮
        if (!toggleRect.IsEmpty)
        {
            using (var sep = new Pen(ThemePalette.BorderSubtle, 1f))
                g.DrawLine(sep, 10, toggleRect.Y - 4, w - 10, toggleRect.Y - 4);
            if (hoverToggle)
            {
                using (var hb = new SolidBrush(ThemePalette.Hover))
                    Native.FillRoundedRect(g, hb, toggleRect, 7);
            }
            int ix = Collapsed ? (toggleRect.X + (toggleRect.Width - 16) / 2) : (toggleRect.X + 11);
            UiIcons.Draw(g, NavIconKind.SidebarToggle, new Rectangle(ix, toggleRect.Y + (toggleRect.Height - 16) / 2, 16, 16),
                hoverToggle ? ThemePalette.TextPrimary : ThemePalette.TextMuted, false);
            if (!Collapsed)
            {
                TextRenderer.DrawText(g, "收起导航栏", fItem,
                    new Rectangle(toggleRect.X + 34, toggleRect.Y, toggleRect.Width - 40, toggleRect.Height),
                    hoverToggle ? ThemePalette.TextPrimary : ThemePalette.TextMuted,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ComputeRects();
        int next = -1;
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i].Visible && itemRects[i].Contains(e.Location)) { next = i; break; }
        }
        bool nextTog = toggleRect.Contains(e.Location);
        if (next != hoverIdx || nextTog != hoverToggle)
        {
            hoverIdx = next;
            hoverToggle = nextTog;
            Cursor = (next >= 0 || nextTog) ? Cursors.Hand : Cursors.Default;
            string wantTip = "";
            if (Collapsed && next >= 0)
            {
                NavItem it = Items[next];
                wantTip = it.Text + (it.BadgeCount > 0 ? " (" + it.BadgeCount + ")" : "");
            }
            else if (nextTog)
            {
                wantTip = Collapsed ? "展开左侧导航栏" : "收起为图标模式";
            }
            if (wantTip != lastTipText)
            {
                lastTipText = wantTip;
                tip.SetToolTip(this, wantTip);
            }
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (hoverIdx != -1 || hoverToggle)
        {
            hoverIdx = -1;
            hoverToggle = false;
            Cursor = Cursors.Default;
            Invalidate();
        }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ComputeRects();
        if (toggleRect.Contains(e.Location))
        {
            SetCollapsed(!Collapsed);
            return;
        }
        for (int i = 0; i < Items.Count; i++)
        {
            if (Items[i].Visible && itemRects[i].Contains(e.Location))
            {
                SelectKey(Items[i].Key, true);
                return;
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (fItem != null) { fItem.Dispose(); fItem = null; }
            if (fItemBold != null) { fItemBold.Dispose(); fItemBold = null; }
            if (fGroup != null) { fGroup.Dispose(); fGroup = null; }
            if (fBadge != null) { fBadge.Dispose(); fBadge = null; }
            if (fBrand != null) { fBrand.Dispose(); fBrand = null; }
            if (tip != null) { tip.Dispose(); tip = null; }
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// 现代圆角分段胶囊切换器：取代杂乱的 RadioButton。
/// </summary>
public class RxSegmentedControl : Control
{
    string[] items = new string[0];
    int selectedIndex = 0;
    int hoverIndex = -1;
    Font fReg, fBold;

    public event EventHandler SelectedIndexChanged;

    public RxSegmentedControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        Height = 30;
        fReg = new Font("Microsoft YaHei UI", 8.8f, FontStyle.Regular);
        fBold = new Font("Microsoft YaHei UI", 8.8f, FontStyle.Bold);
    }

    public string[] Items
    {
        get { return items; }
        set
        {
            items = value ?? new string[0];
            if (selectedIndex >= items.Length) selectedIndex = Math.Max(0, items.Length - 1);
            Invalidate();
        }
    }

    public int SelectedIndex
    {
        get { return selectedIndex; }
        set
        {
            if (items.Length == 0) return;
            int nv = Math.Max(0, Math.Min(items.Length - 1, value));
            if (nv == selectedIndex) return;
            selectedIndex = nv;
            Invalidate();
            if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
        }
    }

    public void SetSelectedSilent(int idx)
    {
        if (items.Length == 0) return;
        selectedIndex = Math.Max(0, Math.Min(items.Length - 1, idx));
        Invalidate();
    }

    int HitSegment(int x)
    {
        if (items.Length == 0 || ClientSize.Width <= 4) return -1;
        int n = items.Length;
        float sw = (float)(ClientSize.Width - 6) / n;
        int idx = (int)((x - 3) / sw);
        return (idx >= 0 && idx < n) ? idx : -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        int w = ClientSize.Width, h = ClientSize.Height;
        g.Clear(Parent != null ? Parent.BackColor : ThemePalette.SurfaceAlt);
        if (w <= 4 || h <= 4) return;

        Rectangle outer = new Rectangle(0, 0, w - 1, h - 1);
        Color track = ThemePalette.Dark ? Color.FromArgb(15, 19, 26) : Color.FromArgb(228, 234, 243);
        using (var tb = new SolidBrush(track))
            Native.FillRoundedRect(g, tb, outer, 7);
        using (var tp = new Pen(ThemePalette.BorderSubtle, 1f))
            Native.DrawRoundedRect(g, tp, outer, 7);

        int n = items.Length;
        if (n == 0) return;
        float sw = (float)(w - 6) / n;

        for (int i = 0; i < n; i++)
        {
            int sx = 3 + (int)Math.Round(i * sw);
            int ex = 3 + (int)Math.Round((i + 1) * sw);
            Rectangle seg = new Rectangle(sx, 3, Math.Max(8, ex - sx), Math.Max(8, h - 6));
            bool sel = (i == selectedIndex);
            bool hov = (!sel && i == hoverIndex);

            if (sel)
            {
                Color pillBg = ThemePalette.Dark ? Color.FromArgb(34, 52, 82) : Color.White;
                Color pillBorder = ThemePalette.Dark ? Color.FromArgb(72, 130, 210) : Color.FromArgb(196, 210, 230);
                using (var pb = new SolidBrush(pillBg))
                    Native.FillRoundedRect(g, pb, seg, 5);
                using (var pp = new Pen(pillBorder, 1f))
                    Native.DrawRoundedRect(g, pp, seg, 5);
            }
            else if (hov)
            {
                using (var hb = new SolidBrush(ThemePalette.Hover))
                    Native.FillRoundedRect(g, hb, seg, 5);
            }

            Color fg = sel ? ThemePalette.AccentSoftFg : (hov ? ThemePalette.TextPrimary : ThemePalette.TextSecondary);
            TextRenderer.DrawText(g, items[i] ?? "", sel ? fBold : fReg, seg, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int next = HitSegment(e.X);
        if (next != hoverIndex) { hoverIndex = next; Invalidate(); }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (hoverIndex != -1) { hoverIndex = -1; Invalidate(); }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        int hit = HitSegment(e.X);
        if (hit >= 0) SelectedIndex = hit;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (fReg != null) { fReg.Dispose(); fReg = null; }
            if (fBold != null) { fBold.Dispose(); fBold = null; }
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// 倒计时可视化时间轴（横向从 TotalSec -> 00:00 映射全部变色/闪烁区段与事件标钉，支持悬停与点击联动）。
/// </summary>
public class TimelineCanvas : Control
{
    public int TotalSeconds = 1080;
    public int RemainingSeconds = 1080;
    public bool IsRunning = false;
    public List<TimerChainNode> Nodes = new List<TimerChainNode>();
    public int SelectedNodeIndex = -1;

    public event Action<int> NodeClicked;
    public event Action<int> SeekRequested;

    const int TrackPadL = 22, TrackPadR = 22, TrackY = 26, TrackH = 14;
    int hoverNodeIdx = -1;
    bool draggingTrack = false;
    Rectangle[] markerRects = new Rectangle[0];
    ToolTip tip = new ToolTip();
    string lastTip = "";
    Font fTick, fTag;

    public TimelineCanvas()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 64;
        fTick = new Font("Consolas", 7.8f, FontStyle.Regular);
        fTag = new Font("Microsoft YaHei UI", 7.6f, FontStyle.Regular);
        tip.InitialDelay = 120;
        tip.AutoPopDelay = 10000;
    }

    float SecToX(int sec, int trackLeft, int trackW)
    {
        int tot = Math.Max(1, TotalSeconds);
        float frac = Math.Max(0f, Math.Min(1f, 1f - (float)sec / tot));
        return trackLeft + frac * trackW;
    }

    int XToSec(int x, int trackLeft, int trackW)
    {
        int tot = Math.Max(1, TotalSeconds);
        float frac = Math.Max(0f, Math.Min(1f, (float)(x - trackLeft) / Math.Max(1, trackW)));
        return Math.Max(0, Math.Min(tot, (int)Math.Round((1f - frac) * tot)));
    }

    bool InTrackHitArea(Point pt)
    {
        return pt.X >= TrackPadL - 10 && pt.X <= ClientSize.Width - TrackPadR + 10
            && pt.Y >= TrackY - 6 && pt.Y <= TrackY + TrackH + 14;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        int w = ClientSize.Width, h = ClientSize.Height;
        g.Clear(ThemePalette.Surface);
        if (w < 80 || h < 40) return;

        using (var border = new Pen(ThemePalette.BorderSubtle, 1f))
            Native.DrawRoundedRect(g, border, new Rectangle(0, 0, w - 1, h - 1), 7);

        int padL = TrackPadL, padR = TrackPadR;
        int trackW = Math.Max(40, w - padL - padR);
        int trackY = TrackY;
        int trackH = TrackH;
        Rectangle trackRect = new Rectangle(padL, trackY, trackW, trackH);

        // 轨道底槽
        using (var bg = new SolidBrush(ThemePalette.SurfaceAlt))
            Native.FillRoundedRect(g, bg, trackRect, 4);
        using (var tp = new Pen(ThemePalette.Border, 1f))
            Native.DrawRoundedRect(g, tp, trackRect, 4);

        // 1. 绘制时间链区段色带（变色与闪烁）
        int n = (Nodes != null) ? Nodes.Count : 0;
        if (markerRects.Length != n) markerRects = new Rectangle[n];
        for (int i = 0; i < n; i++) markerRects[i] = Rectangle.Empty;

        for (int i = 0; i < n; i++)
        {
            TimerChainNode nd = Nodes[i];
            if (nd == null) continue;
            int hi = nd.EffectiveStartSec();
            int lo = nd.EffectiveEndSec();
            float x1 = SecToX(hi, padL, trackW);
            float x2 = SecToX(lo, padL, trackW);
            int rx = (int)Math.Round(Math.Min(x1, x2));
            int rw = Math.Max(4, (int)Math.Round(Math.Abs(x2 - x1)));
            Color baseCol = (nd.ColorIdx >= 0 && nd.ColorIdx < CountdownOverlay.TimerPalette.Length)
                ? CountdownOverlay.TimerPalette[nd.ColorIdx]
                : ThemePalette.Accent;

            if (nd.TriggerMode == 1 || nd.TriggerMode == 2 || nd.ColorIdx >= 0 || nd.FlashMode > 0)
            {
                Rectangle seg = new Rectangle(rx, trackY + 2, rw, trackH - 4);
                if (nd.FlashMode > 0)
                {
                    HatchStyle hs = (nd.FlashMode == 3) ? HatchStyle.DarkUpwardDiagonal
                                  : (nd.FlashMode == 2 ? HatchStyle.ForwardDiagonal : HatchStyle.LightUpwardDiagonal);
                    using (var hb = new HatchBrush(hs, Color.FromArgb(215, baseCol.R, baseCol.G, baseCol.B), Color.FromArgb(75, baseCol.R, baseCol.G, baseCol.B)))
                        g.FillRectangle(hb, seg);
                }
                else
                {
                    using (var sb = new SolidBrush(Color.FromArgb(165, baseCol.R, baseCol.G, baseCol.B)))
                        g.FillRectangle(sb, seg);
                }
            }

            // 顶部事件标钉（点击命中区）
            int mx = (int)Math.Round(x1);
            bool hasLabel = !string.IsNullOrEmpty(nd.LabelText);
            bool sel = (i == SelectedNodeIndex);
            bool hov = (i == hoverNodeIdx);
            int dotR = (sel || hov) ? 5 : (hasLabel ? 4 : 3);
            int dotY = trackY - 9;
            markerRects[i] = new Rectangle(mx - 8, 2, 16, trackY + trackH);

            SmoothingMode oldSm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var lp = new Pen(Color.FromArgb(180, baseCol.R, baseCol.G, baseCol.B), sel ? 2f : 1.2f))
                g.DrawLine(lp, mx, dotY + dotR, mx, trackY + 1);
            using (var db = new SolidBrush(sel ? ThemePalette.Warning : baseCol))
                g.FillEllipse(db, mx - dotR, dotY - dotR, dotR * 2, dotR * 2);
            if (sel || hov)
            {
                using (var ring = new Pen(ThemePalette.TextPrimary, 1.3f))
                    g.DrawEllipse(ring, mx - dotR - 2, dotY - dotR - 2, (dotR + 2) * 2, (dotR + 2) * 2);
            }
            g.SmoothingMode = oldSm;
        }

        // 2. 刻度尺（底行）
        int tot = Math.Max(60, TotalSeconds);
        int step = (tot >= 1500) ? 300 : ((tot >= 600) ? 180 : 60);
        for (int s = tot; s >= 0; s -= step)
        {
            DrawTickAt(g, s, padL, trackW, trackY + trackH);
        }
        if (tot % step != 0) DrawTickAt(g, 0, padL, trackW, trackY + trackH);

        // 3. 当前实时进度游标
        float curX = SecToX(RemainingSeconds, padL, trackW);
        int cx = (int)Math.Round(curX);
        Color curCol = IsRunning ? ThemePalette.Danger : ThemePalette.Accent;
        using (var cp = new Pen(curCol, 2f))
            g.DrawLine(cp, cx, trackY - 4, cx, trackY + trackH + 3);
        Point[] tri = new Point[] { new Point(cx - 4, trackY - 5), new Point(cx + 4, trackY - 5), new Point(cx, trackY) };
        using (var tb = new SolidBrush(curCol))
            g.FillPolygon(tb, tri);

        // 右上角轻提示
        string title = (n == 0) ? "时间轴 · 可拖动进度（当前方案暂无事件）" : ("时间轴 · 共 " + n + " 个事件（可拖动进度，悬停/点击标钉）");
        TextRenderer.DrawText(g, title, fTag, new Rectangle(8, 2, w - 16, 14), ThemePalette.TextMuted,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    void DrawTickAt(Graphics g, int sec, int padL, int trackW, int yTop)
    {
        int x = (int)Math.Round(SecToX(sec, padL, trackW));
        using (var p = new Pen(ThemePalette.Border, 1f))
            g.DrawLine(p, x, yTop + 1, x, yTop + 5);
        string lb = CountdownOverlay.FormatMmSs(sec);
        TextRenderer.DrawText(g, lb, fTick, new Rectangle(x - 24, yTop + 5, 48, 14), ThemePalette.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }

    int HitNode(Point pt)
    {
        if (Nodes == null || pt.Y > TrackY - 1) return -1;
        int best = -1;
        int bestDist = 14;
        for (int i = 0; i < Nodes.Count && i < markerRects.Length; i++)
        {
            if (markerRects[i].IsEmpty) continue;
            int midX = markerRects[i].X + markerRects[i].Width / 2;
            int d = Math.Abs(pt.X - midX);
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }
        return best;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (draggingTrack)
        {
            int trackW = Math.Max(40, ClientSize.Width - TrackPadL - TrackPadR);
            int sec = XToSec(e.X, TrackPadL, trackW);
            if (SeekRequested != null) SeekRequested(sec);
            string dragTip = "调整进度至 " + CountdownOverlay.FormatMmSs(sec);
            if (dragTip != lastTip) { lastTip = dragTip; tip.SetToolTip(this, dragTip); }
            base.OnMouseMove(e);
            return;
        }
        int hit = HitNode(e.Location);
        bool onTrack = (hit < 0 && InTrackHitArea(e.Location));
        Cursor wantCur = (hit >= 0) ? Cursors.Hand : (onTrack ? Cursors.SizeWE : Cursors.Default);
        if (Cursor != wantCur) Cursor = wantCur;
        if (hit != hoverNodeIdx)
        {
            hoverNodeIdx = hit;
            string want = "";
            if (hit >= 0 && Nodes != null && hit < Nodes.Count)
            {
                TimerChainNode nd = Nodes[hit];
                want = "#" + (hit + 1) + "  " + nd.FormatTimeRange() + "\r\n" + nd.FormatEffects();
            }
            if (want != lastTip)
            {
                lastTip = want;
                tip.SetToolTip(this, want);
            }
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (!draggingTrack)
        {
            if (hoverNodeIdx != -1) { hoverNodeIdx = -1; Invalidate(); }
            if (Cursor != Cursors.Default) Cursor = Cursors.Default;
        }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        int hit = HitNode(e.Location);
        if (hit >= 0)
        {
            SelectedNodeIndex = hit;
            Invalidate();
            if (NodeClicked != null) NodeClicked(hit);
            return;
        }
        if (InTrackHitArea(e.Location))
        {
            draggingTrack = true;
            Capture = true;
            Cursor = Cursors.SizeWE;
            int trackW = Math.Max(40, ClientSize.Width - TrackPadL - TrackPadR);
            int sec = XToSec(e.X, TrackPadL, trackW);
            if (SeekRequested != null) SeekRequested(sec);
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (draggingTrack && e.Button == MouseButtons.Left)
        {
            draggingTrack = false;
            Capture = false;
            int hit = HitNode(e.Location);
            Cursor = (hit >= 0) ? Cursors.Hand : (InTrackHitArea(e.Location) ? Cursors.SizeWE : Cursors.Default);
        }
        base.OnMouseUp(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (fTick != null) { fTick.Dispose(); fTick = null; }
            if (fTag != null) { fTag.Dispose(); fTag = null; }
            if (tip != null) { tip.Dispose(); tip = null; }
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// 现代自定义短语卡片：左侧主题色条 + 粗体标题 + 两行淡灰正文预览 + 动态微标签。
/// </summary>
public class CustomPhraseCard : Control
{
    public string TitleText = "";
    public string BodyText = "";
    public Color StripeColor = Color.FromArgb(37, 99, 235);
    public bool HasTargetPlaceholder = false;
    public bool IsMultiLine = false;
    public event MouseEventHandler CardRightClick;

    bool hovered = false;
    bool pressed = false;
    Font fTitle, fPreview, fTag;

    public CustomPhraseCard()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Size = new Size(276, 86);
        Margin = new Padding(0, 0, 10, 10);
        Cursor = Cursors.Hand;
        fTitle = new Font("Microsoft YaHei UI", 9.8f, FontStyle.Bold);
        fPreview = new Font("Consolas", 8.3f, FontStyle.Regular);
        fTag = new Font("Microsoft YaHei UI", 7.4f, FontStyle.Regular);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        int w = ClientSize.Width, h = ClientSize.Height;
        g.Clear(Parent != null ? Parent.BackColor : ThemePalette.Surface);

        Rectangle card = new Rectangle(0, 0, w - 1, h - 1);
        Color bg = pressed ? ThemePalette.Selected : (hovered ? ThemePalette.Hover : ThemePalette.Elevated);
        Color border = hovered ? ThemePalette.Accent : ThemePalette.BorderSubtle;
        using (var br = new SolidBrush(bg))
            Native.FillRoundedRect(g, br, card, 8);
        using (var pen = new Pen(border, hovered ? 1.2f : 1f))
            Native.DrawRoundedRect(g, pen, card, 8);

        // 左侧色条
        using (var sb = new SolidBrush(StripeColor))
            Native.FillRoundedRect(g, sb, new Rectangle(3, 10, 3, Math.Max(12, h - 20)), 2);

        // 标题
        int tagReserve = (HasTargetPlaceholder || IsMultiLine) ? 68 : 10;
        TextRenderer.DrawText(g, TitleText ?? "", fTitle,
            new Rectangle(14, 9, Math.Max(40, w - 16 - tagReserve), 22), ThemePalette.TextPrimary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

        // 右上角微标签
        if (HasTargetPlaceholder || IsMultiLine)
        {
            string tagTxt = HasTargetPlaceholder ? "目标联动" : "多行宏";
            Color tbg = HasTargetPlaceholder ? ThemePalette.AccentSoft : ThemePalette.SuccessSoft;
            Color tfg = HasTargetPlaceholder ? ThemePalette.AccentSoftFg : ThemePalette.Success;
            Rectangle trc = new Rectangle(w - 64, 10, 56, 18);
            using (var tb = new SolidBrush(tbg))
                Native.FillRoundedRect(g, tb, trc, 4);
            TextRenderer.DrawText(g, tagTxt, fTag, trc, tfg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }

        // 正文预览（最多 2 行）
        string prev = (BodyText ?? "").Replace("\r\n", "\n").Replace("\r", "\n").Trim();
        if (prev.Length == 0) prev = "（空内容）";
        if (prev.Length > 140) prev = prev.Substring(0, 140) + "…";
        TextRenderer.DrawText(g, prev, fPreview,
            new Rectangle(14, 34, Math.Max(40, w - 24), h - 42), ThemePalette.TextSecondary,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); }
        base.OnMouseDown(e);
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (pressed) { pressed = false; Invalidate(); }
        if (e.Button == MouseButtons.Right && CardRightClick != null)
            CardRightClick(this, e);
        base.OnMouseUp(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (fTitle != null) { fTitle.Dispose(); fTitle = null; }
            if (fPreview != null) { fPreview.Dispose(); fPreview = null; }
            if (fTag != null) { fTag.Dispose(); fTag = null; }
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// 底部可交互状态胶囊栏：每个状态块均可直接点击触发对应快捷操作。
/// </summary>
public class StatusPillBar : Control
{
    public class Pill
    {
        public string Key;
        public string Text;
        public Color DotColor;
        public bool RightAlign;
        public Action OnClick;
        public string Tooltip;
        internal Rectangle Bounds;
    }

    public readonly List<Pill> Pills = new List<Pill>();
    int hoverIdx = -1;
    Font fPill;
    ToolTip tip = new ToolTip();
    string lastTip = "";

    public StatusPillBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Height = 32;
        fPill = new Font("Microsoft YaHei UI", 8.3f, FontStyle.Regular);
        tip.InitialDelay = 250;
    }

    public Pill Find(string key)
    {
        for (int i = 0; i < Pills.Count; i++)
            if (Pills[i].Key == key) return Pills[i];
        return null;
    }

    void ComputeBounds(Graphics g)
    {
        int xLeft = 8;
        int xRight = ClientSize.Width - 8;
        int h = ClientSize.Height;
        int ph = Math.Max(20, h - 8);
        int py = (h - ph) / 2;

        for (int i = 0; i < Pills.Count; i++)
        {
            Pill p = Pills[i];
            if (p.RightAlign) continue;
            Size sz = TextRenderer.MeasureText(g, p.Text ?? "", fPill);
            int pw = sz.Width + 24;
            p.Bounds = new Rectangle(xLeft, py, pw, ph);
            xLeft += pw + 6;
        }
        for (int i = Pills.Count - 1; i >= 0; i--)
        {
            Pill p = Pills[i];
            if (!p.RightAlign) continue;
            Size sz = TextRenderer.MeasureText(g, p.Text ?? "", fPill);
            int pw = sz.Width + 24;
            xRight -= pw;
            p.Bounds = new Rectangle(xRight, py, pw, ph);
            xRight -= 6;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(ThemePalette.SurfaceAlt);
        using (var pen = new Pen(ThemePalette.BorderSubtle, 1f))
            g.DrawLine(pen, 0, 0, ClientSize.Width, 0);

        ComputeBounds(g);
        SmoothingMode oldSm = g.SmoothingMode;
        for (int i = 0; i < Pills.Count; i++)
        {
            Pill p = Pills[i];
            if (p.Bounds.IsEmpty || p.Bounds.Right < 0 || p.Bounds.Left > ClientSize.Width) continue;
            bool hov = (i == hoverIdx && p.OnClick != null);
            Color bg = hov ? ThemePalette.Hover : ThemePalette.Surface;
            using (var br = new SolidBrush(bg))
                Native.FillRoundedRect(g, br, p.Bounds, 6);
            using (var bp = new Pen(hov ? ThemePalette.Accent : ThemePalette.BorderSubtle, 1f))
                Native.DrawRoundedRect(g, bp, p.Bounds, 6);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var db = new SolidBrush(p.DotColor))
                g.FillEllipse(db, p.Bounds.X + 7, p.Bounds.Y + (p.Bounds.Height - 6) / 2, 6, 6);
            g.SmoothingMode = oldSm;

            TextRenderer.DrawText(g, p.Text ?? "", fPill,
                new Rectangle(p.Bounds.X + 16, p.Bounds.Y, p.Bounds.Width - 20, p.Bounds.Height),
                hov ? ThemePalette.TextPrimary : ThemePalette.TextSecondary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int next = -1;
        for (int i = 0; i < Pills.Count; i++)
        {
            if (Pills[i].Bounds.Contains(e.Location)) { next = i; break; }
        }
        if (next != hoverIdx)
        {
            hoverIdx = next;
            Cursor = (next >= 0 && Pills[next].OnClick != null) ? Cursors.Hand : Cursors.Default;
            string want = (next >= 0) ? (Pills[next].Tooltip ?? "") : "";
            if (want != lastTip) { lastTip = want; tip.SetToolTip(this, want); }
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (hoverIdx != -1) { hoverIdx = -1; Cursor = Cursors.Default; Invalidate(); }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        for (int i = 0; i < Pills.Count; i++)
        {
            if (Pills[i].Bounds.Contains(e.Location) && Pills[i].OnClick != null)
            {
                Pills[i].OnClick();
                return;
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (fPill != null) { fPill.Dispose(); fPill = null; }
            if (tip != null) { tip.Dispose(); tip = null; }
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// 准星方案可视化卡片条（最多 6 套，3 列 × 2 行网格，左画迷你准星缩略图，右显示名称与修改状态）。
/// </summary>
public class CrosshairPresetStrip : Control
{
    public List<CrosshairPreset> Presets = new List<CrosshairPreset>();
    public int ActiveIndex = -1;
    public bool IsActiveModified = false;

    public event Action<int> PresetClicked;
    public event Action<int, Point> PresetContextRequested;

    Font fTitle, fTitleBold;
    ToolTip tip;
    int hoverIdx = -1;
    string lastTip = "";

    const int MaxCards = 6;
    const int Cols = 3;
    const int CardH = 34;
    const int GapX = 6;
    const int GapY = 6;

    public CrosshairPresetStrip()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        fTitle = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular);
        fTitleBold = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold);
        tip = new ToolTip();
        tip.InitialDelay = 350;
    }

    public Rectangle GetCardRect(int idx)
    {
        if (idx < 0 || idx >= MaxCards) return Rectangle.Empty;
        int w = Math.Max(120, ClientSize.Width);
        int cardW = Math.Max(48, (w - GapX * (Cols - 1)) / Cols);
        int row = idx / Cols;
        int col = idx % Cols;
        int x = col * (cardW + GapX);
        if (col == Cols - 1) cardW = Math.Max(cardW, w - x);
        int y = row * (CardH + GapY);
        return new Rectangle(x, y, cardW, CardH);
    }

    public int HitTest(Point pt)
    {
        int count = Presets != null ? Math.Min(MaxCards, Presets.Count) : 0;
        for (int i = 0; i < count; i++)
        {
            if (GetCardRect(i).Contains(pt)) return i;
        }
        return -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent != null ? Parent.BackColor : ThemePalette.SurfaceAlt);
        if (Presets == null || Presets.Count == 0) return;

        int count = Math.Min(MaxCards, Presets.Count);
        for (int i = 0; i < count; i++)
        {
            CrosshairPreset p = Presets[i];
            Rectangle rc = GetCardRect(i);
            bool active = (i == ActiveIndex);
            bool hover = (i == hoverIdx);

            Color bg = active ? ThemePalette.AccentSoft : (hover ? ThemePalette.Hover : ThemePalette.Surface);
            Color edge = active ? ThemePalette.Accent : (hover ? ThemePalette.BorderFocus : ThemePalette.BorderSubtle);
            using (var br = new SolidBrush(bg))
                Native.FillRoundedRect(g, br, rc, 6);
            using (var pen = new Pen(edge, active ? 1.5f : 1f))
                Native.DrawRoundedRect(g, pen, new Rectangle(rc.X, rc.Y, rc.Width - 1, rc.Height - 1), 6);

            var iconBox = new Rectangle(rc.X + 6, rc.Y + (rc.Height - 22) / 2, 22, 22);
            CrosshairPreset.DrawMiniIcon(g, p, iconBox);

            bool showModDot = active && IsActiveModified;
            int rightReserve = showModDot ? 14 : 6;
            var textRect = new Rectangle(iconBox.Right + 6, rc.Y, Math.Max(10, rc.Right - iconBox.Right - 6 - rightReserve), rc.Height);
            TextRenderer.DrawText(g, p.Name ?? "", active ? fTitleBold : fTitle, textRect,
                active ? ThemePalette.AccentSoftFg : ThemePalette.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

            if (showModDot)
            {
                SmoothingMode oldSm = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var dotBr = new SolidBrush(ThemePalette.Warning))
                    g.FillEllipse(dotBr, rc.Right - 11, rc.Y + (rc.Height - 6) / 2, 6, 6);
                g.SmoothingMode = oldSm;
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int next = HitTest(e.Location);
        if (next != hoverIdx)
        {
            hoverIdx = next;
            Cursor = (next >= 0) ? Cursors.Hand : Cursors.Default;
            string want = "";
            if (next >= 0 && Presets != null && next < Presets.Count)
                want = Presets[next].DescribeSummary() + "\n左键切换 · 右键管理方案";
            if (want != lastTip) { lastTip = want; tip.SetToolTip(this, want); }
            Invalidate();
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (hoverIdx != -1) { hoverIdx = -1; Cursor = Cursors.Default; Invalidate(); }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        int idx = HitTest(e.Location);
        if (idx >= 0)
        {
            if (e.Button == MouseButtons.Left && PresetClicked != null)
                PresetClicked(idx);
            else if (e.Button == MouseButtons.Right && PresetContextRequested != null)
                PresetContextRequested(idx, PointToScreen(e.Location));
        }
        base.OnMouseUp(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (fTitle != null) { fTitle.Dispose(); fTitle = null; }
            if (fTitleBold != null) { fTitleBold.Dispose(); fTitleBold = null; }
            if (tip != null) { tip.Dispose(); tip = null; }
        }
        base.Dispose(disposing);
    }
}
