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

public class BufferedListBox : ListBox
{
    public BufferedListBox()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.ApplyDarkScrollBar(Handle, MainForm.IsDark);
    }
}

public class BufferedListView : ListView
{
    class HeaderHook : NativeWindow
    {
        readonly BufferedListView owner;
        public HeaderHook(BufferedListView lv) { owner = lv; }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0014 && m.WParam != IntPtr.Zero)   // WM_ERASEBKGND
            {
                try
                {
                    Native.RECT rc;
                    if (Native.GetWindowRect(Handle, out rc))
                    {
                        Color bg = MainForm.IsDark ? Color.FromArgb(28, 33, 40) : Color.FromArgb(248, 250, 252);
                        using (var g = Graphics.FromHdc(m.WParam))
                        using (var br = new SolidBrush(bg))
                            g.FillRectangle(br, 0, 0, rc.R - rc.L, rc.B - rc.T);
                        m.Result = (IntPtr)1;
                        return;
                    }
                }
                catch { }
            }
            base.WndProc(ref m);
            if (m.Msg == 0x000F)   // WM_PAINT: fill trailing strip past last column
            {
                try
                {
                    Native.RECT rc;
                    if (Native.GetWindowRect(Handle, out rc))
                    {
                        int hw = rc.R - rc.L;
                        int hh = rc.B - rc.T;
                        int used = 0;
                        for (int i = 0; i < owner.Columns.Count; i++)
                            used += owner.Columns[i].Width;
                        if (used < hw && hh > 0)
                        {
                            bool dark = MainForm.IsDark;
                            Color bg = dark ? Color.FromArgb(28, 33, 40) : Color.FromArgb(248, 250, 252);
                            Color sep = dark ? Color.FromArgb(48, 54, 61) : Color.FromArgb(226, 232, 240);
                            using (var g = Graphics.FromHwnd(Handle))
                            using (var br = new SolidBrush(bg))
                            using (var pen = new Pen(sep))
                            {
                                g.FillRectangle(br, used, 0, hw - used, hh);
                                g.DrawLine(pen, used, hh - 1, hw, hh - 1);
                            }
                        }
                    }
                }
                catch { }
            }
        }
    }

    HeaderHook headerHook;

    public BufferedListView()
    {
        DoubleBuffered = true;
        OwnerDraw = true;
        BorderStyle = BorderStyle.FixedSingle;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.ApplyDarkScrollBar(Handle, MainForm.IsDark);
        try
        {
            IntPtr hdr = Native.SendMessage(Handle, 0x101F, IntPtr.Zero, IntPtr.Zero);   // LVM_GETHEADER
            if (hdr != IntPtr.Zero)
            {
                if (headerHook == null) headerHook = new HeaderHook(this);
                if (headerHook.Handle != hdr)
                {
                    if (headerHook.Handle != IntPtr.Zero) headerHook.ReleaseHandle();
                    headerHook.AssignHandle(hdr);
                }
            }
        }
        catch { }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        try
        {
            if (headerHook != null && headerHook.Handle != IntPtr.Zero)
                headerHook.ReleaseHandle();
        }
        catch { }
        base.OnHandleDestroyed(e);
    }

    public void RefreshTheme()
    {
        if (IsDisposed) return;
        if (IsHandleCreated) Native.ApplyDarkScrollBar(Handle, MainForm.IsDark);
        try
        {
            if (headerHook != null && headerHook.Handle != IntPtr.Zero)
                Native.InvalidateRect(headerHook.Handle, IntPtr.Zero, true);
        }
        catch { }
        Invalidate(true);
    }

    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
    {
        bool dark = MainForm.IsDark;
        Color bg = dark ? Color.FromArgb(28, 33, 40) : Color.FromArgb(248, 250, 252);
        Color fg = dark ? Color.FromArgb(139, 148, 158) : Color.FromArgb(71, 85, 105);
        Color bottomSep = dark ? Color.FromArgb(48, 54, 61) : Color.FromArgb(226, 232, 240);
        Color colSep = dark ? Color.FromArgb(38, 44, 54) : Color.FromArgb(232, 237, 244);

        using (var br = new SolidBrush(bg))
            e.Graphics.FillRectangle(br, e.Bounds);
        using (var pBot = new Pen(bottomSep))
            e.Graphics.DrawLine(pBot, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        using (var pCol = new Pen(colSep))
            e.Graphics.DrawLine(pCol, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 5);

        TextFormatFlags tf = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
        if (e.Header != null && e.Header.TextAlign == HorizontalAlignment.Right)
            tf |= TextFormatFlags.Right;
        else if (e.Header != null && e.Header.TextAlign == HorizontalAlignment.Center)
            tf |= TextFormatFlags.HorizontalCenter;
        else
            tf |= TextFormatFlags.Left;

        Rectangle tr = new Rectangle(e.Bounds.X + 8, e.Bounds.Y, Math.Max(4, e.Bounds.Width - 16), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, e.Header != null ? e.Header.Text : "", Font, tr, fg, tf);
    }

    protected override void OnDrawItem(DrawListViewItemEventArgs e)
    {
        e.DrawDefault = false;
    }

    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e)
    {
        bool dark = MainForm.IsDark;
        bool sel = e.Item != null && e.Item.Selected;
        int colW = (e.ColumnIndex >= 0 && e.ColumnIndex < Columns.Count) ? Columns[e.ColumnIndex].Width : e.Bounds.Width;
        Rectangle cell = new Rectangle(e.Bounds.X, e.Bounds.Y, Math.Max(0, colW), e.Bounds.Height);

        Color baseBg = dark ? Color.FromArgb(13, 17, 23) : Color.White;
        Color rowBg = sel
            ? (dark ? Color.FromArgb(26, 42, 68) : Color.FromArgb(235, 244, 255))
            : baseBg;
        Color rowSep = dark ? Color.FromArgb(26, 31, 38) : Color.FromArgb(241, 245, 249);

        using (var br = new SolidBrush(rowBg))
            e.Graphics.FillRectangle(br, cell);
        using (var sp = new Pen(rowSep))
            e.Graphics.DrawLine(sp, cell.Left, cell.Bottom - 1, cell.Right, cell.Bottom - 1);

        if (sel && e.ColumnIndex == 0)
        {
            using (var ab = new SolidBrush(dark ? Color.FromArgb(88, 166, 255) : Color.FromArgb(37, 99, 235)))
                e.Graphics.FillRectangle(ab, cell.X, cell.Y + 1, 3, Math.Max(4, cell.Height - 2));
        }

        int textLeft = cell.X + 8;
        if (e.ColumnIndex == 0 && CheckBoxes && e.Item != null)
        {
            int cy = cell.Y + (cell.Height - 14) / 2;
            Rectangle boxRect = new Rectangle(cell.X + 6, cy, 14, 14);
            if (e.Item.Checked)
            {
                using (var cb = new SolidBrush(dark ? Color.FromArgb(56, 139, 253) : Color.FromArgb(37, 99, 235)))
                    Native.FillRoundedRect(e.Graphics, cb, boxRect, 3);
                SmoothingMode oldSm = e.Graphics.SmoothingMode;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var ck = new Pen(Color.White, 1.8f))
                {
                    e.Graphics.DrawLine(ck, boxRect.X + 3, boxRect.Y + 7, boxRect.X + 6, boxRect.Y + 10);
                    e.Graphics.DrawLine(ck, boxRect.X + 6, boxRect.Y + 10, boxRect.X + 11, boxRect.Y + 4);
                }
                e.Graphics.SmoothingMode = oldSm;
            }
            else
            {
                Color boxFill = dark ? Color.FromArgb(22, 27, 34) : Color.White;
                Color boxEdge = dark ? Color.FromArgb(72, 82, 98) : Color.FromArgb(180, 192, 208);
                using (var cb = new SolidBrush(boxFill))
                    Native.FillRoundedRect(e.Graphics, cb, boxRect, 3);
                using (var cp = new Pen(boxEdge, 1f))
                    Native.DrawRoundedRect(e.Graphics, cp, boxRect, 3);
            }
            textLeft = cell.X + 25;
        }

        if (e.SubItem == null) return;
        Color cellBg = e.SubItem.BackColor;
        bool hasBadge = (cellBg != Color.Empty && cellBg != Color.Transparent
            && cellBg != e.Item.BackColor
            && cellBg.ToArgb() != baseBg.ToArgb()
            && cellBg.ToArgb() != Color.FromArgb(21, 25, 34).ToArgb());

        Font f = e.SubItem.Font ?? Font;
        Color fg = e.SubItem.ForeColor;
        if (fg == Color.Empty || fg == SystemColors.WindowText || (dark && fg == Color.Black)
            || (dark && !hasBadge && fg.R < 90 && fg.G < 90 && fg.B < 95))
            fg = dark ? Color.FromArgb(230, 237, 243) : Color.FromArgb(30, 41, 59);
        else if (!dark && !hasBadge && fg.R > 195 && fg.G > 195 && fg.B > 195)
            fg = Color.FromArgb(30, 41, 59);

        if (hasBadge)
        {
            Rectangle pill = new Rectangle(cell.X + 6, cell.Y + 2, Math.Max(20, cell.Width - 12), Math.Max(12, cell.Height - 5));
            using (var pb = new SolidBrush(cellBg))
                Native.FillRoundedRect(e.Graphics, pb, pill, 5);

            var cap = e.SubItem.Tag as int[];
            if (cap != null && cap.Length >= 2 && cap[1] > 0 && pill.Width >= 40)
            {
                float ratio = Math.Max(0f, Math.Min(1f, (float)cap[0] / cap[1]));
                int barW = Math.Max(4, (int)Math.Round((pill.Width - 6) * ratio));
                Color barCol = (cap[0] >= cap[1])
                    ? ThemePalette.Danger
                    : (ratio >= 0.85f ? ThemePalette.Warning : ThemePalette.Success);
                using (var bb = new SolidBrush(Color.FromArgb(dark ? 165 : 145, barCol.R, barCol.G, barCol.B)))
                    Native.FillRoundedRect(e.Graphics, bb, new Rectangle(pill.X + 3, pill.Bottom - 3, barW, 2), 1);
            }

            TextRenderer.DrawText(e.Graphics, e.SubItem.Text ?? "", f, pill, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
            return;
        }

        if (sel)
            fg = dark ? Color.FromArgb(240, 246, 252) : Color.FromArgb(15, 23, 42);

        TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine;
        HorizontalAlignment align = (e.ColumnIndex >= 0 && e.ColumnIndex < Columns.Count)
            ? Columns[e.ColumnIndex].TextAlign : HorizontalAlignment.Left;
        if (align == HorizontalAlignment.Right) flags |= TextFormatFlags.Right;
        else if (align == HorizontalAlignment.Center) flags |= TextFormatFlags.HorizontalCenter;
        else flags |= TextFormatFlags.Left;

        int padRight = 8;
        Rectangle tr = new Rectangle(textLeft, cell.Y, Math.Max(4, cell.Right - padRight - textLeft), cell.Height);
        TextRenderer.DrawText(e.Graphics, e.SubItem.Text ?? "", f, tr, fg, flags);
    }
}

public class ThemedComboBox : ComboBox
{
    bool hovered = false;

    public ThemedComboBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.ApplyDarkScrollBar(Handle, MainForm.IsDark);
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnDropDown(EventArgs e) { Invalidate(); base.OnDropDown(e); }
    protected override void OnDropDownClosed(EventArgs e) { Invalidate(); base.OnDropDownClosed(e); }
    protected override void OnSelectedIndexChanged(EventArgs e) { Invalidate(); base.OnSelectedIndexChanged(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    void PaintFace(Graphics g)
    {
        bool dark = MainForm.IsDark;
        bool active = hovered || Focused || DroppedDown;
        Color bg = dark ? Color.FromArgb(13, 17, 23) : Color.White;
        Color border = active
            ? (dark ? Color.FromArgb(88, 166, 255) : Color.FromArgb(59, 130, 246))
            : (dark ? Color.FromArgb(48, 54, 61) : Color.FromArgb(203, 213, 225));
        Color fg = Enabled
            ? (dark ? Color.FromArgb(230, 237, 243) : Color.FromArgb(15, 23, 42))
            : (dark ? Color.FromArgb(110, 118, 129) : Color.FromArgb(148, 163, 184));
        Color arrowCol = dark ? Color.FromArgb(139, 148, 158) : Color.FromArgb(100, 116, 139);

        using (var br = new SolidBrush(bg))
            g.FillRectangle(br, 0, 0, Width, Height);
        using (var pen = new Pen(border))
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

        string txt = (SelectedIndex >= 0 && SelectedIndex < Items.Count) ? GetItemText(Items[SelectedIndex]) : Text;
        Rectangle tr = new Rectangle(6, 1, Math.Max(4, Width - 26), Math.Max(4, Height - 2));
        TextRenderer.DrawText(g, txt ?? "", Font, tr, fg,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

        int ax = Width - 15;
        int ay = Height / 2 - 2;
        SmoothingMode oldSm = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var ap = new Pen(arrowCol, 1.5f))
        {
            g.DrawLine(ap, ax, ay, ax + 4, ay + 4);
            g.DrawLine(ap, ax + 4, ay + 4, ax + 8, ay);
        }
        g.SmoothingMode = oldSm;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0014)   // WM_ERASEBKGND
        {
            m.Result = (IntPtr)1;
            return;
        }
        if (m.Msg == 0x000F)   // WM_PAINT
        {
            Native.PAINTSTRUCT ps;
            IntPtr hdc = Native.BeginPaint(Handle, out ps);
            if (hdc != IntPtr.Zero)
            {
                try
                {
                    using (var g = Graphics.FromHdc(hdc))
                        PaintFace(g);
                }
                finally
                {
                    Native.EndPaint(Handle, ref ps);
                }
            }
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count) return;
        bool dark = MainForm.IsDark;
        bool sel = (e.State & DrawItemState.Selected) != 0;
        Color bg = sel
            ? (dark ? Color.FromArgb(31, 50, 77) : Color.FromArgb(235, 244, 255))
            : (dark ? Color.FromArgb(22, 27, 34) : Color.White);
        Color fg = sel
            ? (dark ? Color.FromArgb(240, 246, 252) : Color.FromArgb(15, 23, 42))
            : (dark ? Color.FromArgb(201, 209, 217) : Color.FromArgb(30, 41, 59));
        using (var br = new SolidBrush(bg))
            e.Graphics.FillRectangle(br, e.Bounds);
        Rectangle tr = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, Math.Max(4, e.Bounds.Width - 10), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, GetItemText(Items[e.Index]) ?? "", Font, tr, fg,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }
}

public class ThemedTabControl : TabControl
{
    int hoverIdx = -1;

    public ThemedTabControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        DrawMode = TabDrawMode.OwnerDrawFixed;
        SizeMode = TabSizeMode.Normal;
        Padding = new Point(10, 6);
        DoubleBuffered = true;
    }

    int StripBottom
    {
        get
        {
            int top = 30;
            try
            {
                if (TabCount > 0 && IsHandleCreated)
                {
                    int b = GetTabRect(0).Bottom;
                    if (b > 0) top = b;
                }
            }
            catch { }
            return top;
        }
    }

    public override Rectangle DisplayRectangle
    {
        get
        {
            int top = StripBottom + 1;
            return new Rectangle(0, top, Width, Math.Max(0, Height - top));
        }
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        base.OnSelectedIndexChanged(e);
        Invalidate(new Rectangle(0, 0, Width, StripBottom + 4));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int next = -1;
        if (e.Y <= StripBottom + 2)
        {
            for (int i = 0; i < TabPages.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(TabPages[i].Text)) continue;
                if (GetTabRect(i).Contains(e.Location)) { next = i; break; }
            }
        }
        if (next != hoverIdx)
        {
            hoverIdx = next;
            Invalidate(new Rectangle(0, 0, Width, StripBottom + 2));
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (hoverIdx != -1)
        {
            hoverIdx = -1;
            Invalidate(new Rectangle(0, 0, Width, StripBottom + 2));
        }
        base.OnMouseLeave(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x0014 && m.WParam != IntPtr.Zero)   // WM_ERASEBKGND
        {
            m.Result = (IntPtr)1;
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        bool dark = MainForm.IsDark;
        Color stripBg = dark ? Color.FromArgb(13, 17, 23) : Color.FromArgb(241, 245, 249);
        Color pageBg = dark ? Color.FromArgb(22, 27, 34) : Color.FromArgb(250, 251, 253);
        Color hoverBg = dark ? Color.FromArgb(28, 33, 40) : Color.FromArgb(226, 232, 240);
        Color borderCol = dark ? Color.FromArgb(48, 54, 61) : Color.FromArgb(226, 232, 240);
        Color activeFg = dark ? Color.FromArgb(240, 246, 252) : Color.FromArgb(15, 23, 42);
        Color idleFg = dark ? Color.FromArgb(139, 148, 158) : Color.FromArgb(100, 116, 139);
        Color accent = dark ? Color.FromArgb(88, 166, 255) : Color.FromArgb(37, 99, 235);

        int sb = StripBottom;
        using (var bg = new SolidBrush(stripBg))
            e.Graphics.FillRectangle(bg, 0, 0, Width, sb + 1);
        using (var pbg = new SolidBrush(pageBg))
            e.Graphics.FillRectangle(pbg, 0, sb + 1, Width, Math.Max(0, Height - sb - 1));
        using (var sep = new Pen(borderCol))
            e.Graphics.DrawLine(sep, 0, sb, Width, sb);

        for (int i = 0; i < TabPages.Count; i++)
        {
            TabPage pg = TabPages[i];
            if (string.IsNullOrWhiteSpace(pg.Text)) continue;
            Rectangle r;
            try { r = GetTabRect(i); }
            catch { continue; }
            bool sel = (i == SelectedIndex);
            bool hov = (!sel && i == hoverIdx);

            if (sel)
            {
                Rectangle card = new Rectangle(r.X + 1, Math.Max(1, r.Y + 1), Math.Max(8, r.Width - 2), Math.Max(8, sb - Math.Max(1, r.Y + 1)));
                using (var tb = new SolidBrush(pageBg))
                    e.Graphics.FillRectangle(tb, card.X, card.Y, card.Width, card.Height + 1);
                using (var bp = new Pen(borderCol))
                {
                    e.Graphics.DrawLine(bp, card.X, card.Bottom, card.X, card.Y);
                    e.Graphics.DrawLine(bp, card.X, card.Y, card.Right - 1, card.Y);
                    e.Graphics.DrawLine(bp, card.Right - 1, card.Y, card.Right - 1, card.Bottom);
                }
                using (var ab = new SolidBrush(accent))
                    e.Graphics.FillRectangle(ab, card.X + 1, card.Y, Math.Max(4, card.Width - 2), 2);
            }
            else if (hov)
            {
                Rectangle hRect = new Rectangle(r.X + 2, r.Y + 3, Math.Max(8, r.Width - 4), Math.Max(8, sb - r.Y - 5));
                using (var hb = new SolidBrush(hoverBg))
                    Native.FillRoundedRect(e.Graphics, hb, hRect, 5);
            }

            Rectangle tr = new Rectangle(r.X + 1, r.Y + 2, Math.Max(8, r.Width - 2), Math.Max(8, sb - r.Y - 2));
            TextRenderer.DrawText(e.Graphics, pg.Text, Font, tr, sel ? activeFg : (hov ? activeFg : idleFg),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        }
    }
}

public class WrapList : Control
{
    public string[] Labels = new string[0];
    public string[] Values = new string[0];
    public bool[] Heads = new bool[0];
    public string[] Notes = new string[0];   // 标题后的注解：小一号、斜体、灰色
    public string[] HighlightTokens = new string[0];   // 搜索关键词局部高亮
    public Func<int, bool> IsRowPinned;                // 外部查询某行是否已收藏
    public string EmptyText = "（暂无条目）";
    public bool DefaultCollapsed = false;
    public Font HeadFont;
    public Font SubFont;
    public int Sel = -1;

    public event EventHandler SelChanged;
    public event EventHandler ItemActivated;
    public event MouseEventHandler ItemRightClick;
    public event EventHandler CollapseChanged;
    public event Action<int> ItemPinToggled;

    const int BarWidth = 16;
    bool barVisible = false;
    bool barHover = false;
    bool barDragging = false;
    int barDragOffsetY = 0;
    ToolTip hover = new ToolTip();
    int[] heights = new int[0];
    int[] previewSize = new int[0];    // 0=不画预览，1..3=用第几档小字体
    int[] previewW = new int[0];
    int[] previewLines = new int[0];
    bool[] rowMeasured = new bool[0];
    int[] previewChipW = new int[0];
    string[] previewTexts = new string[0];
    int[] labelOneW = new int[0];
    bool[] titleSingleLine = new bool[0];
    bool[] noteSingleLine = new bool[0];
    bool measuredWithPin = false;
    bool monoMetricsReady = false;
    double monoCwA, monoPadA, monoCwB, monoPadB;
    Font prevFontA, prevFontB, noteFont, noteFontS, noteFontXS, majorFont, foldHintFont, bannerFont, bannerBadgeFont;
    int measuredWidth = -1;
    int scrollY = 0;
    int hoverIdx = -1;
    int mouseDownIdx = -1;
    bool panning = false;
    int panStartY = 0;
    int panStartScroll = 0;

    int[] secEnd = new int[0];
    int[] childItemCount = new int[0];
    bool[] canCollapse = new bool[0];
    int[] parentMajor = new int[0];
    int[] parentMinor = new int[0];
    bool[] collapsed = new bool[0];

    float[] animFrac = new float[0];
    bool[] animActive = new bool[0];
    float[] animStartFrac = new float[0];
    long[] animStartUtc = new long[0];
    System.Windows.Forms.Timer animTimer;
    const int AnimDurationMs = 150;

    int[] visY = new int[0];
    int[] visClipTop = new int[0];
    int[] visClipBot = new int[0];
    int[] visOrder = new int[0];
    int visCount = 0;
    int layoutTotalH = 0;
    bool layoutValid = false;
    bool inPaint = false;
    internal int TestMeasureCount { get; private set; }

    public WrapList()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
               | ControlStyles.Selectable, true);
        SetStyle(ControlStyles.StandardDoubleClick, false);
        TabStop = true;
        BackColor = Color.White;
        HeadFont = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
        SubFont = new Font("Consolas", 9f, FontStyle.Regular);
        // 右侧指令预览的三档自适应字号（只建一次，避免每帧 new Font 泄漏 GDI）
        noteFont = new Font("Microsoft YaHei UI", 8f, FontStyle.Italic);
        noteFontS = new Font("Microsoft YaHei UI", 7f, FontStyle.Italic);
        noteFontXS = new Font("Microsoft YaHei UI", 6.2f, FontStyle.Italic);
        majorFont = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
        foldHintFont = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular);
        bannerFont = new Font("Microsoft YaHei UI", 8.8f, FontStyle.Regular);
        bannerBadgeFont = new Font("Microsoft YaHei UI", 7.6f, FontStyle.Bold);
        prevFontA = new Font("Consolas", 8.5f, FontStyle.Regular);
        prevFontB = new Font("Consolas", 7.5f, FontStyle.Regular);

        animTimer = new System.Windows.Forms.Timer();
        animTimer.Interval = 15;
        animTimer.Tick += OnAnimTick;

        hover.AutoPopDelay = 20000;
        hover.InitialDelay = 260;
        hover.ReshowDelay = 80;
        hover.ShowAlways = true;
    }

    // 固定预留右侧滚动条槽位宽度：避免展开/收起跨越视口高度时 ViewWidth 突变触发全表重测
    int ViewWidth { get { return Math.Max(60, ClientSize.Width - BarWidth); } }
    internal int TestViewWidth { get { return ViewWidth; } }
    internal int TestHitTest(int y) { return HitTest(y); }

    public bool IsRowHidden(int index)
    {
        if (index < 0 || index >= Labels.Length) return true;
        int pm = (index < parentMajor.Length) ? parentMajor[index] : -1;
        if (pm >= 0 && pm < collapsed.Length && collapsed[pm]) return true;
        int ps = (index < parentMinor.Length) ? parentMinor[index] : -1;
        if (ps >= 0 && ps < collapsed.Length && collapsed[ps]) return true;
        return false;
    }

    public bool IsCollapsible(int index)
    {
        return index >= 0 && index < canCollapse.Length && canCollapse[index];
    }

    public bool IsInfoBanner(int index)
    {
        if (index < 0 || index >= Heads.Length || !Heads[index]) return false;
        if (index < canCollapse.Length && canCollapse[index]) return false;
        string raw = (index < Labels.Length && Labels[index] != null) ? Labels[index] : "";
        return !raw.StartsWith("▓");
    }

    public bool IsCollapsed(int index)
    {
        return index >= 0 && index < collapsed.Length && collapsed[index];
    }

    public bool HasCollapsible()
    {
        for (int i = 0; i < canCollapse.Length; i++)
            if (canCollapse[i]) return true;
        return false;
    }

    public bool AreAllCollapsed()
    {
        bool any = false;
        for (int i = 0; i < canCollapse.Length; i++)
        {
            if (!canCollapse[i]) continue;
            any = true;
            if (!collapsed[i]) return false;
        }
        return any;
    }

    public void SetAllCollapsed(bool collapse)
    {
        if (animTimer != null) animTimer.Stop();
        bool changed = false;
        for (int i = 0; i < canCollapse.Length; i++)
        {
            if (!canCollapse[i]) continue;
            if (collapsed[i] != collapse || animActive[i])
            {
                collapsed[i] = collapse;
                animActive[i] = false;
                animFrac[i] = collapse ? 0f : 1f;
                changed = true;
            }
        }
        if (collapse && Sel >= 0 && IsRowHidden(Sel))
        {
            Sel = -1;
            if (SelChanged != null) SelChanged(this, EventArgs.Empty);
        }
        if (changed)
        {
            layoutValid = false;
            EnsureHeights();
            Invalidate();
            if (CollapseChanged != null) CollapseChanged(this, EventArgs.Empty);
        }
    }

    internal bool TestHasStandardDoubleClick()
    {
        return GetStyle(ControlStyles.StandardDoubleClick);
    }

    internal int TestRowTop(int index)
    {
        return ItemTop(index);
    }

    internal void TestSimulateFastClickAtY(int y, int driftYDuringHold)
    {
        var down = new MouseEventArgs(MouseButtons.Left, 1, 16, y, 0);
        OnMouseDown(down);
        var up = new MouseEventArgs(MouseButtons.Left, 1, 16, y + driftYDuringHold, 0);
        OnMouseClick(up);
    }

    bool IsMajorHead(int index)
    {
        return index >= 0 && index < Heads.Length && Heads[index]
            && index < Labels.Length && Labels[index] != null && Labels[index].StartsWith("▓");
    }

    static bool HeadsMatch(string[] oldL, bool[] oldH, string[] newL, bool[] newH)
    {
        if (oldL == null || newL == null || oldH == null || newH == null) return false;
        if (oldL.Length != newL.Length || oldH.Length != newH.Length) return false;
        for (int i = 0; i < newL.Length; i++)
        {
            bool h1 = (i < oldH.Length && oldH[i]);
            bool h2 = (i < newH.Length && newH[i]);
            if (h1 != h2) return false;
            if (h1 && !string.Equals(oldL[i] ?? "", newL[i] ?? "", StringComparison.Ordinal)) return false;
        }
        return true;
    }

    void RebuildHierarchy(bool preserveCollapsed, bool[] oldCollapsed)
    {
        int n = Labels.Length;
        secEnd = new int[n];
        childItemCount = new int[n];
        canCollapse = new bool[n];
        parentMajor = new int[n];
        parentMinor = new int[n];
        collapsed = new bool[n];
        animFrac = new float[n];
        animActive = new bool[n];
        animStartFrac = new float[n];
        animStartUtc = new long[n];
        visY = new int[n];
        visClipTop = new int[n];
        visClipBot = new int[n];
        visOrder = new int[n];
        visCount = 0;

        int curMajor = -1;
        int curMinor = -1;
        for (int i = 0; i < n; i++)
        {
            bool isHd = (i < Heads.Length && Heads[i]);
            bool isMaj = IsMajorHead(i);
            if (isMaj)
            {
                parentMajor[i] = -1;
                parentMinor[i] = -1;
                curMajor = i;
                curMinor = -1;
            }
            else if (isHd)
            {
                parentMajor[i] = curMajor;
                parentMinor[i] = -1;
                curMinor = i;
            }
            else
            {
                parentMajor[i] = curMajor;
                parentMinor[i] = curMinor;
            }
            secEnd[i] = -1;
            animFrac[i] = 1f;
        }

        for (int i = 0; i < n; i++)
        {
            if (!(i < Heads.Length && Heads[i])) continue;
            bool isMaj = IsMajorHead(i);
            int end = n;
            for (int j = i + 1; j < n; j++)
            {
                bool jHd = (j < Heads.Length && Heads[j]);
                if (isMaj) { if (IsMajorHead(j)) { end = j; break; } }
                else { if (jHd) { end = j; break; } }
            }
            secEnd[i] = end;
            int items = 0;
            for (int j = i + 1; j < end; j++)
            {
                if (!(j < Heads.Length && Heads[j])) items++;
            }
            childItemCount[i] = items;
            canCollapse[i] = (items > 0);
        }

        if (preserveCollapsed && oldCollapsed != null && oldCollapsed.Length == n)
        {
            for (int i = 0; i < n; i++)
            {
                if (canCollapse[i] && oldCollapsed[i])
                {
                    collapsed[i] = true;
                    animFrac[i] = 0f;
                }
            }
        }
        else if (DefaultCollapsed)
        {
            for (int i = 0; i < n; i++)
            {
                if (canCollapse[i])
                {
                    collapsed[i] = true;
                    animFrac[i] = 0f;
                }
            }
        }
    }

    public void SetData(string[] labels, string[] values, bool[] heads)
    {
        SetData(labels, values, heads, null);
    }

    public void SetData(string[] labels, string[] values, bool[] heads, string[] notes)
    {
        string[] oldLabels = Labels;
        bool[] oldHeads = Heads;
        bool[] oldCollapsed = collapsed;

        Labels = labels ?? new string[0];
        Values = values ?? new string[0];
        Heads = heads ?? new bool[0];
        Notes = notes ?? new string[0];

        bool sameStructure = HeadsMatch(oldLabels, oldHeads, Labels, Heads);
        if (!sameStructure && animTimer != null) animTimer.Stop();

        RebuildHierarchy(sameStructure, oldCollapsed);
        heights = new int[0];
        rowMeasured = new bool[0];
        measuredWidth = -1;
        layoutValid = false;
        scrollY = 0;
        hoverIdx = -1;
        mouseDownIdx = -1;
        TestMeasureCount = 0;
        if (Sel >= Labels.Length || (Sel >= 0 && IsRowHidden(Sel))) Sel = -1;
        EnsureHeights();
        Invalidate();
        if (CollapseChanged != null) CollapseChanged(this, EventArgs.Empty);
    }

    Font PreviewFontOf(int k)
    {
        return (k == 1) ? prevFontA : prevFontB;
    }

    // 预览只取第一行；过长就截断（避免测量超长文本），剩下的交给换行
    static string PreviewTextOf(string v)
    {
        if (string.IsNullOrEmpty(v)) return "";
        string t = v.Trim();
        if (t.Length == 0) return "";
        int nl = t.IndexOfAny(new char[] { (char)13, (char)10 });
        if (nl >= 0) t = t.Substring(0, nl).TrimEnd() + " …";
        if (t.Length > 160) t = t.Substring(0, 160) + "…";
        return t;
    }

    void ResetMeasureCache(int w)
    {
        int n = Labels.Length;
        measuredWidth = w;
        measuredWithPin = (ItemPinToggled != null);
        heights = new int[n];
        rowMeasured = new bool[n];
        previewSize = new int[n];
        previewW = new int[n];
        previewLines = new int[n];
        previewChipW = new int[n];
        previewTexts = new string[n];
        labelOneW = new int[n];
        titleSingleLine = new bool[n];
        noteSingleLine = new bool[n];
        for (int i = 0; i < n; i++) labelOneW[i] = -1;
        layoutValid = false;
    }

    void EnsureMonoMetrics(Graphics g)
    {
        if (monoMetricsReady) return;
        Size sz = new Size(4000, 100);
        TextFormatFlags fl = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
        const string s10 = "aaaaaaaaaa";
        const string s30 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        int wA10 = TextRenderer.MeasureText(g, s10, prevFontA, sz, fl).Width;
        int wA30 = TextRenderer.MeasureText(g, s30, prevFontA, sz, fl).Width;
        monoCwA = (wA30 - wA10) / 20.0;
        monoPadA = wA10 - 10.0 * monoCwA;

        int wB10 = TextRenderer.MeasureText(g, s10, prevFontB, sz, fl).Width;
        int wB30 = TextRenderer.MeasureText(g, s30, prevFontB, sz, fl).Width;
        monoCwB = (wB30 - wB10) / 20.0;
        monoPadB = wB10 - 10.0 * monoCwB;
        monoMetricsReady = true;
    }

    static bool IsPrintableAscii(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c < (char)32 || c > (char)126) return false;
        }
        return true;
    }

    int MeasurePreviewSingleLineW(Graphics g, string cmd, int k, bool isAscii)
    {
        if (isAscii)
        {
            EnsureMonoMetrics(g);
            double cw = (k == 1) ? monoCwA : monoCwB;
            double pad = (k == 1) ? monoPadA : monoPadB;
            return (int)Math.Ceiling(pad + cmd.Length * cw);
        }
        return TextRenderer.MeasureText(g, cmd, PreviewFontOf(k),
            new Size(4000, 100), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;
    }

    void MeasureRow(int i, int w, ref Graphics measureG)
    {
        if (i < 0 || i >= Labels.Length || i >= rowMeasured.Length || rowMeasured[i]) return;
        if (measureG == null)
            measureG = Graphics.FromHwnd(IsHandleCreated ? Handle : IntPtr.Zero);

        bool isHd = (i < Heads.Length && Heads[i]);
        string rawText = Labels[i] ?? "";
        bool isMajor = isHd && rawText.StartsWith("▓");
        bool isCol = isHd && i < canCollapse.Length && canCollapse[i];
        bool isInfoNote = isHd && !isCol && !isMajor;
        Font f = isMajor ? majorFont : (isInfoNote ? bannerFont : (isHd ? HeadFont : Font));
        int leftPad = isInfoNote ? 58 : (isHd ? 14 : (measuredWithPin ? 32 : 18));
        int tw = w - (isInfoNote ? 76 : (isHd ? (isCol ? 146 : 38) : (leftPad + 18)));
        if (tw < 60) tw = 60;
        string text = isMajor ? rawText.Substring(1).Trim() : rawText;

        int h;
        bool titleOneMeasured = false;
        if (!isHd && w >= 400 && text.Length <= 22 && text.IndexOf('\n') < 0)
        {
            h = Math.Max(34, f.Height + 12);
            titleOneMeasured = true;
        }
        else
        {
            Size sz = TextRenderer.MeasureText(measureG, text, f, new Size(tw, 4000),
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            h = Math.Max(isInfoNote ? 32 : 34, sz.Height + (isInfoNote ? 16 : (isHd ? 14 : 12)));
            if (text.IndexOf('\n') < 0 && sz.Height <= f.Height + 6)
                titleOneMeasured = true;
        }

        // 标题旁的注解可能换 2~4 行：短注解必单行且不超基线行高，跳过测量
        string nt = (!isHd && i < Notes.Length) ? (Notes[i] ?? "") : "";
        bool noteShortFast = false;
        bool noteOneMeasured = false;
        int noteW = 0;
        if (nt.Length > 0)
        {
            noteW = Math.Max(160, (w - 300) / 2);
            if (nt.Length <= 12 && nt.IndexOf('\n') < 0 && noteW >= 180)
            {
                noteShortFast = true;
            }
            else
            {
                Size ns = TextRenderer.MeasureText(measureG, nt, noteFont, new Size(noteW, 4000),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                int need = ns.Height + 14;
                if (need > h) h = Math.Min(need, 150);
                if (nt.IndexOf('\n') < 0 && ns.Height <= noteFont.Height + 4)
                    noteOneMeasured = true;
            }
        }

        // 右侧指令预览与胶囊宽度 + 注解并排判定 + 单行快速绘制标记均在此测量缓存
        string cmd = (!isHd && i < Values.Length && Values[i] != null) ? PreviewTextOf(Values[i]) : "";
        if (cmd.Length > 0) previewTexts[i] = cmd;
        bool needPreview = (!isHd && w >= 460 && cmd.Length > 0);
        bool needNoteLayout = (!isHd && nt.Length > 0);
        int chipW = 0;

        if (needPreview || needNoteLayout)
        {
            Size labelOne = TextRenderer.MeasureText(measureG, text, f,
                new Size(4000, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

            if (needPreview)
            {
                int availRight = w - (leftPad + labelOne.Width + 46);
                if (availRight >= 150)
                {
                    int maxW = Math.Min((int)(w * (needNoteLayout ? 0.34 : 0.60)), availRight);
                    if (maxW < 150) maxW = 150;
                    int chosen = 0;
                    int measuredW = 0;
                    bool cmdAscii = IsPrintableAscii(cmd);
                    for (int k = 1; k <= 2; k++)
                    {
                        int pw1 = MeasurePreviewSingleLineW(measureG, cmd, k, cmdAscii);
                        if (pw1 <= maxW) { chosen = k; measuredW = pw1; break; }
                    }
                    int lines = 1;
                    int ph = 0;
                    if (chosen == 0)
                    {
                        chosen = 2;
                        Font pf = PreviewFontOf(2);
                        Size ps = TextRenderer.MeasureText(measureG, cmd, pf, new Size(maxW, 300),
                            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                        int lh = pf.Height + 2;
                        lines = Math.Max(1, Math.Min(2, (ps.Height + lh - 1) / lh));
                        ph = lines * lh;
                        if (lines == 1 || ps.Height <= ph + 8)
                        {
                            measuredW = ps.Width;
                        }
                        else
                        {
                            Size psClipped = TextRenderer.MeasureText(measureG, cmd, pf, new Size(maxW, ph + 8),
                                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                            measuredW = psClipped.Width;
                        }
                    }
                    chipW = Math.Min(maxW + 12, Math.Max(42, measuredW + 14));
                    previewSize[i] = chosen;
                    previewW[i] = maxW;
                    previewLines[i] = lines;
                    previewChipW[i] = chipW;
                    if (ph > 0)
                    {
                        int need = ph + 12;
                        if (need > h) h = need;
                    }
                }
            }

            int rightReserve = (chipW > 0) ? (chipW + 22) : 14;
            int titleW = Math.Max(60, w - leftPad - rightReserve);
            titleSingleLine[i] = (text.IndexOf('\n') < 0 && labelOne.Width <= titleW - 2);

            if (needNoteLayout)
            {
                if (text.IndexOf('\n') < 0 && labelOne.Width <= titleW - 4)
                {
                    labelOneW[i] = labelOne.Width;
                }
                else if (labelOne.Width <= titleW + 8)
                {
                    Size wrapped = TextRenderer.MeasureText(measureG, text, f,
                        new Size(titleW, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                    if (wrapped.Height <= f.Height + 6)
                        labelOneW[i] = labelOne.Width;
                }

                if (labelOneW[i] >= 0 && nt.IndexOf('\n') < 0)
                {
                    int actualNoteW = w - rightReserve - (leftPad + labelOneW[i] + 10) - 6;
                    if ((noteShortFast && actualNoteW >= 160) || (noteOneMeasured && actualNoteW >= noteW))
                        noteSingleLine[i] = true;
                }
            }
        }
        else
        {
            titleSingleLine[i] = titleOneMeasured;
        }

        heights[i] = h;
        rowMeasured[i] = true;
        TestMeasureCount++;
    }

    void MeasureAll()
    {
        int w = ViewWidth;
        ResetMeasureCache(w);
        Graphics measureG = null;
        try
        {
            for (int i = 0; i < Labels.Length; i++)
                MeasureRow(i, w, ref measureG);
        }
        finally
        {
            if (measureG != null) measureG.Dispose();
        }
        layoutValid = false;
    }

    void ComputeSpan(int start, int end, int clipTop, int limitBottom, int stopAtY, ref int y, bool writePositions, int w, ref Graphics measureG)
    {
        int i = start;
        while (i < end)
        {
            if (!writePositions && y >= stopAtY) break;
            if (writePositions && limitBottom < int.MaxValue && y >= limitBottom) break;

            bool isHd = (i < Heads.Length && Heads[i]);
            if (!rowMeasured[i]) MeasureRow(i, w, ref measureG);
            int rh = heights[i];
            if (writePositions && i < visY.Length)
            {
                int ct = Math.Max(clipTop, y);
                int cb = Math.Min(limitBottom, y + rh);
                if (cb > ct)
                {
                    visY[i] = y;
                    visClipTop[i] = ct;
                    visClipBot[i] = cb;
                    visOrder[visCount++] = i;
                }
            }
            y += rh;

            if (isHd && i < canCollapse.Length && canCollapse[i])
            {
                int spanStart = i + 1;
                int spanEnd = Math.Min(end, secEnd[i]);
                int spanTop = y;

                if (animActive[i])
                {
                    int cap = Math.Max(220, ClientSize.Height);
                    int natY = spanTop;
                    ComputeSpan(spanStart, spanEnd, spanTop, int.MaxValue, spanTop + cap, ref natY, false, w, ref measureG);
                    int naturalSpanH = natY - spanTop;
                    int maxAnim = Math.Min(naturalSpanH, cap);
                    int drawerH = (int)Math.Round(maxAnim * animFrac[i]);
                    if (writePositions && drawerH > 0 && spanEnd > spanStart)
                    {
                        int innerLimit = Math.Min(limitBottom, spanTop + drawerH);
                        if (innerLimit > spanTop)
                        {
                            int dummyY = spanTop;
                            ComputeSpan(spanStart, spanEnd, spanTop, innerLimit, int.MaxValue, ref dummyY, true, w, ref measureG);
                        }
                    }
                    y = spanTop + drawerH;
                    i = spanEnd;
                }
                else if (collapsed[i])
                {
                    i = spanEnd;
                }
                else
                {
                    if (spanEnd > spanStart)
                    {
                        int childClip = writePositions ? Math.Max(clipTop, spanTop) : spanTop;
                        ComputeSpan(spanStart, spanEnd, childClip, limitBottom, stopAtY, ref y, writePositions, w, ref measureG);
                    }
                    i = spanEnd;
                }
            }
            else
            {
                i++;
            }
        }
    }

    void RebuildLayout()
    {
        int n = Labels.Length;
        int w = ViewWidth;
        if (heights.Length != n || measuredWidth != w || measuredWithPin != (ItemPinToggled != null))
            ResetMeasureCache(w);

        if (visY.Length != n || visOrder.Length != n)
        {
            visY = new int[n];
            visClipTop = new int[n];
            visClipBot = new int[n];
            visOrder = new int[n];
        }
        for (int k = 0; k < n; k++)
        {
            visY[k] = -1;
            visClipTop[k] = 0;
            visClipBot[k] = 0;
        }
        visCount = 0;

        int y = 0;
        Graphics measureG = null;
        try
        {
            ComputeSpan(0, n, 0, int.MaxValue, int.MaxValue, ref y, true, w, ref measureG);
        }
        finally
        {
            if (measureG != null) measureG.Dispose();
        }
        layoutTotalH = y;
        layoutValid = true;
    }

    public void ToggleSection(int index, bool animate)
    {
        if (index < 0 || index >= canCollapse.Length || !canCollapse[index]) return;
        collapsed[index] = !collapsed[index];
        if (collapsed[index] && Sel >= 0 && IsRowHidden(Sel))
        {
            Sel = -1;
            if (SelChanged != null) SelChanged(this, EventArgs.Empty);
        }
        if (!animate || animTimer == null)
        {
            animActive[index] = false;
            animFrac[index] = collapsed[index] ? 0f : 1f;
            layoutValid = false;
            EnsureHeights();
            Invalidate();
            if (CollapseChanged != null) CollapseChanged(this, EventArgs.Empty);
            return;
        }
        animStartFrac[index] = animFrac[index];
        animStartUtc[index] = DateTime.UtcNow.Ticks;
        animActive[index] = true;
        layoutValid = false;
        EnsureHeights();
        Invalidate();
        animTimer.Start();
        if (CollapseChanged != null) CollapseChanged(this, EventArgs.Empty);
    }

    void OnAnimTick(object sender, EventArgs e)
    {
        if (IsDisposed || animActive == null)
        {
            if (animTimer != null) animTimer.Stop();
            return;
        }
        long now = DateTime.UtcNow.Ticks;
        long durTicks = AnimDurationMs * TimeSpan.TicksPerMillisecond;
        bool anyRunning = false;
        for (int i = 0; i < animActive.Length; i++)
        {
            if (!animActive[i]) continue;
            double t = (double)(now - animStartUtc[i]) / durTicks;
            if (t >= 1.0)
            {
                animActive[i] = false;
                animFrac[i] = collapsed[i] ? 0f : 1f;
            }
            else
            {
                if (t < 0.0) t = 0.0;
                double inv = 1.0 - t;
                float ease = (float)(1.0 - inv * inv * inv);
                float target = collapsed[i] ? 0f : 1f;
                animFrac[i] = animStartFrac[i] + (target - animStartFrac[i]) * ease;
                anyRunning = true;
            }
        }
        layoutValid = false;
        EnsureHeights();
        if (!anyRunning && animTimer != null)
        {
            animTimer.Stop();
            SyncHoverToCursor();
        }
        Invalidate();
    }

    void SyncHoverToCursor()
    {
        if (!IsHandleCreated) return;
        try
        {
            Point pt = PointToClient(Cursor.Position);
            if (!ClientRectangle.Contains(pt) || InBar(pt.X))
            {
                hoverIdx = -1;
                Cursor = Cursors.Default;
                hover.SetToolTip(this, "");
                return;
            }
            int idx = HitTest(pt.Y);
            hoverIdx = idx;
            bool isHd = (idx >= 0 && idx < Heads.Length && Heads[idx]);
            bool isCol = isHd && IsCollapsible(idx);
            Cursor = (idx >= 0 && (!isHd || isCol)) ? Cursors.Hand : Cursors.Default;
            if (isHd || idx < 0) hover.SetToolTip(this, "");
        }
        catch { }
    }

    public int ScrollTop
    {
        get { return scrollY; }
        set
        {
            scrollY = Math.Max(0, value);
            UpdateBar();
            Invalidate();
        }
    }

    /// <summary>把第 idx 条滚到顶部（用于分类跳转）</summary>
    public void ScrollToIndex(int idx)
    {
        if (idx < 0 || idx >= Labels.Length) return;
        bool changed = false;
        int pm = (idx < parentMajor.Length) ? parentMajor[idx] : -1;
        if (pm >= 0 && pm < collapsed.Length && (collapsed[pm] || animActive[pm]))
        {
            collapsed[pm] = false; animActive[pm] = false; animFrac[pm] = 1f; changed = true;
        }
        int ps = (idx < parentMinor.Length) ? parentMinor[idx] : -1;
        if (ps >= 0 && ps < collapsed.Length && (collapsed[ps] || animActive[ps]))
        {
            collapsed[ps] = false; animActive[ps] = false; animFrac[ps] = 1f; changed = true;
        }
        if (idx < canCollapse.Length && canCollapse[idx] && (collapsed[idx] || animActive[idx]))
        {
            collapsed[idx] = false; animActive[idx] = false; animFrac[idx] = 1f; changed = true;
        }
        if (changed) layoutValid = false;
        EnsureHeights();
        int y = ItemTop(idx);
        if (y < 0) y = 0;
        int maxScroll = Math.Max(0, TotalHeight() - ClientSize.Height);
        if (y > maxScroll) y = maxScroll;
        if (y < 0) y = 0;
        scrollY = y;
        UpdateBar();
        Invalidate();
        if (changed && CollapseChanged != null) CollapseChanged(this, EventArgs.Empty);
    }

    void EnsureHeights()
    {
        int w = ViewWidth;
        bool withPin = (ItemPinToggled != null);
        if (heights.Length != Labels.Length || measuredWidth != w || measuredWithPin != withPin)
        {
            ResetMeasureCache(w);
            RebuildLayout();
            UpdateBar();
        }
        else if (!layoutValid)
        {
            RebuildLayout();
            UpdateBar();
        }
    }

    int TotalHeight()
    {
        if (!layoutValid) RebuildLayout();
        return layoutTotalH;
    }

    void UpdateBar()
    {
        int total = TotalHeight();
        int h = ClientSize.Height;
        bool need = (h > 0 && total > h + 2);
        if (barVisible != need)
        {
            barVisible = need;
            if (!inPaint && ClientSize.Width > BarWidth && ClientSize.Height > 0)
                Invalidate(new Rectangle(ClientSize.Width - BarWidth, 0, BarWidth, ClientSize.Height));
        }
        if (!need) { scrollY = 0; return; }

        int maxScroll = Math.Max(0, total - h);
        if (scrollY < 0) scrollY = 0;
        if (scrollY > maxScroll) scrollY = maxScroll;
    }

    bool InBar(int x)
    {
        return barVisible && ClientSize.Width > BarWidth && x >= ClientSize.Width - BarWidth;
    }

    Rectangle GetBarThumbRect()
    {
        if (!barVisible) return Rectangle.Empty;
        int h = ClientSize.Height;
        int total = TotalHeight();
        if (h <= 0 || total <= h || ClientSize.Width <= BarWidth) return Rectangle.Empty;
        const int padY = 2;
        const int padX = 3;
        int trackH = Math.Max(1, h - padY * 2);
        int thumbH = (int)Math.Max(28, (long)h * trackH / total);
        if (thumbH > trackH) thumbH = trackH;
        int maxScroll = Math.Max(1, total - h);
        int travel = Math.Max(0, trackH - thumbH);
        int thumbY = padY + (travel > 0 ? (int)((long)scrollY * travel / maxScroll) : 0);
        return new Rectangle(ClientSize.Width - BarWidth + padX, thumbY, BarWidth - padX * 2, thumbH);
    }

    void ScrollTo(int y)
    {
        int maxScroll = Math.Max(0, TotalHeight() - ClientSize.Height);
        if (y < 0) y = 0;
        if (y > maxScroll) y = maxScroll;
        if (y == scrollY) return;
        scrollY = y;
        Invalidate();
    }

    void InvalidateRow(int idx)
    {
        if (idx < 0 || idx >= visY.Length || !IsHandleCreated || !Visible) return;
        if (visY[idx] < 0) return;
        int top = visClipTop[idx] - scrollY;
        int bot = visClipBot[idx] - scrollY;
        if (bot <= 0 || top >= ClientSize.Height || bot <= top) return;
        int rw = barVisible ? ViewWidth : ClientSize.Width;
        if (rw <= 0) return;
        int cTop = Math.Max(0, top);
        int cBot = Math.Min(ClientSize.Height, bot);
        if (cBot > cTop) Invalidate(new Rectangle(0, cTop, rw, cBot - cTop));
    }

    int HitTest(int y)
    {
        EnsureHeights();
        if (visCount <= 0) return -1;
        int py = y + scrollY;
        if (py < 0) return -1;
        int lo = 0, hi = visCount - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            int idx = visOrder[mid];
            if (py < visClipTop[idx]) hi = mid - 1;
            else if (py >= visClipBot[idx]) lo = mid + 1;
            else return idx;
        }
        return -1;
    }

    int ItemTop(int index)
    {
        EnsureHeights();
        if (index < 0 || index >= visY.Length) return -1;
        return visY[index];
    }

    public void EnsureVisible(int index)
    {
        if (index < 0 || index >= Labels.Length) return;
        EnsureHeights();
        int top = ItemTop(index);
        if (top < 0) return;
        int rh = (index < heights.Length && heights[index] > 0) ? heights[index] : 34;
        int bottom = top + rh;
        if (top < scrollY) ScrollTo(top);
        else if (bottom > scrollY + ClientSize.Height) ScrollTo(bottom - ClientSize.Height);
    }

    public int FindSelectable(int start, int dir)
    {
        int n = Labels.Length;
        if (n == 0 || dir == 0) return -1;
        int i = start;
        while (i >= 0 && i < n)
        {
            bool isHd = (i < Heads.Length && Heads[i]);
            if (!isHd && !IsRowHidden(i)) return i;
            i += dir;
        }
        return -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        inPaint = true;
        try
        {
            bool dark = MainForm.IsDark;
            Color pageBg = dark ? Color.FromArgb(22, 27, 34) : Color.White;
            e.Graphics.Clear(pageBg);
            EnsureHeights();
            if (heights.Length == 0)
            {
                TextRenderer.DrawText(e.Graphics, EmptyText, Font, new Point(16, 18),
                    dark ? Color.FromArgb(139, 148, 158) : Color.FromArgb(120, 130, 145));
                return;
            }

            int w = ViewWidth;
            int bgW = barVisible ? w : ClientSize.Width;
            int paintTop = e.ClipRectangle.Top;
            int paintBot = e.ClipRectangle.Bottom;

            if (visCount > 0 && paintBot > paintTop)
            {
                int firstVis = visCount;
                int lo = 0, hi = visCount - 1;
                int targetTop = scrollY + paintTop;
                while (lo <= hi)
                {
                    int mid = (lo + hi) >> 1;
                    int idx = visOrder[mid];
                    if (visClipBot[idx] > targetTop)
                    {
                        firstVis = mid;
                        hi = mid - 1;
                    }
                    else
                    {
                        lo = mid + 1;
                    }
                }

                if (firstVis < visCount)
                {
                    PointF[] chevPts = new PointF[3];
                    float[] bx = new float[] { -2.5f, 2.5f, -2.5f };
                    float[] by = new float[] { -4.5f, 0f, 4.5f };

                    using (var majorBg = new SolidBrush(dark ? Color.FromArgb(32, 39, 51) : Color.FromArgb(236, 242, 252)))
                    using (var majorHoverBg = new SolidBrush(dark ? Color.FromArgb(40, 50, 66) : Color.FromArgb(224, 234, 250)))
                    using (var headBg = new SolidBrush(dark ? Color.FromArgb(28, 33, 40) : Color.FromArgb(244, 247, 252)))
                    using (var headHoverBg = new SolidBrush(dark ? Color.FromArgb(35, 42, 52) : Color.FromArgb(234, 241, 252)))
                    using (var headAccent = new SolidBrush(dark ? Color.FromArgb(88, 166, 255) : Color.FromArgb(37, 99, 235)))
                    using (var noteCardBg = new SolidBrush(dark ? Color.FromArgb(40, 33, 22) : Color.FromArgb(255, 250, 237)))
                    using (var noteCardBorder = new Pen(dark ? Color.FromArgb(88, 66, 36) : Color.FromArgb(238, 214, 162), 1f))
                    using (var noteCardAccent = new SolidBrush(dark ? Color.FromArgb(230, 170, 72) : Color.FromArgb(217, 119, 6)))
                    using (var noteBadgeBg = new SolidBrush(dark ? Color.FromArgb(72, 53, 24) : Color.FromArgb(252, 234, 194)))
                    using (var selBg = new SolidBrush(dark ? Color.FromArgb(26, 42, 68) : Color.FromArgb(235, 244, 255)))
                    using (var hoverBg = new SolidBrush(dark ? Color.FromArgb(28, 34, 44) : Color.FromArgb(246, 249, 254)))
                    using (var sepPen = new Pen(dark ? Color.FromArgb(33, 38, 45) : Color.FromArgb(241, 245, 249)))
                    using (var codeChipBg = new SolidBrush(ThemePalette.CodeChipBg))
                    using (var codeChipEdgePen = new Pen(ThemePalette.CodeChipEdge, 1f))
                    using (var codeChipActivePen = new Pen(ThemePalette.Accent, 1f))
                    using (var chevPenActive = new Pen(dark ? Color.FromArgb(121, 192, 255) : Color.FromArgb(37, 99, 235), 1.7f))
                    using (var chevPenMuted = new Pen(dark ? Color.FromArgb(125, 140, 160) : Color.FromArgb(118, 134, 156), 1.7f))
                    {
                        chevPenActive.StartCap = LineCap.Round;
                        chevPenActive.EndCap = LineCap.Round;
                        chevPenActive.LineJoin = LineJoin.Round;
                        chevPenMuted.StartCap = LineCap.Round;
                        chevPenMuted.EndCap = LineCap.Round;
                        chevPenMuted.LineJoin = LineJoin.Round;

                        for (int k = firstVis; k < visCount; k++)
                        {
                            int i = visOrder[k];
                            int h = heights[i];
                            int y = visY[i] - scrollY;
                            int cTop = visClipTop[i] - scrollY;
                            int cBot = visClipBot[i] - scrollY;
                            if (cTop >= paintBot) break;
                            if (cBot <= paintTop || cBot <= cTop) continue;

                            bool clipped = (visClipTop[i] > visY[i] || visClipBot[i] < visY[i] + h);
                            Region savedClip = null;
                            if (clipped)
                            {
                                savedClip = e.Graphics.Clip;
                                e.Graphics.SetClip(new Rectangle(0, cTop, bgW, cBot - cTop), CombineMode.Intersect);
                            }
                            try
                            {
                                Rectangle r = new Rectangle(0, y, w, h);
                                Rectangle bgRect = (bgW == w) ? r : new Rectangle(0, y, bgW, h);
                                bool isHd = (i < Heads.Length && Heads[i]);
                                bool isCol = isHd && i < canCollapse.Length && canCollapse[i];
                                bool isFolded = isCol && i < collapsed.Length && collapsed[i];
                                bool isHeadHov = isCol && (i == hoverIdx);
                                string labelText = Labels[i] ?? "";
                                bool isMajor = isHd && labelText.StartsWith("▓");
                                bool isInfoNote = isHd && !isCol && !isMajor;
                                if (isMajor) labelText = labelText.Substring(1).Trim();
                                if (isInfoNote)
                                {
                                    Rectangle cardRc = new Rectangle(8, y + 3, Math.Max(40, bgW - 16), Math.Max(18, h - 6));
                                    Native.FillRoundedRect(e.Graphics, noteCardBg, cardRc, 6);
                                    Native.DrawRoundedRect(e.Graphics, noteCardBorder, new Rectangle(cardRc.X, cardRc.Y, cardRc.Width - 1, cardRc.Height - 1), 6);
                                    Native.FillRoundedRect(e.Graphics, noteCardAccent, new Rectangle(cardRc.X + 4, cardRc.Y + 5, 3, Math.Max(6, cardRc.Height - 10)), 2);
                                    Rectangle badgeRc = new Rectangle(cardRc.X + 12, cardRc.Y + 5, 34, 18);
                                    Native.FillRoundedRect(e.Graphics, noteBadgeBg, badgeRc, 4);
                                    TextRenderer.DrawText(e.Graphics, "说明", bannerBadgeFont, badgeRc,
                                        dark ? Color.FromArgb(242, 194, 104) : Color.FromArgb(162, 82, 12),
                                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                                }
                                else if (isHd)
                                {
                                    Brush hb = isMajor ? (isHeadHov ? majorHoverBg : majorBg) : (isHeadHov ? headHoverBg : headBg);
                                    e.Graphics.FillRectangle(hb, bgRect);
                                    Native.FillRoundedRect(e.Graphics, headAccent, new Rectangle(2, y + 6, 3, Math.Max(4, h - 12)), 2);
                                }
                                else if (i == Sel)
                                {
                                    e.Graphics.FillRectangle(selBg, bgRect);
                                    Native.FillRoundedRect(e.Graphics, headAccent, new Rectangle(2, y + 3, 3, Math.Max(4, h - 6)), 2);
                                }
                                else if (i == hoverIdx)
                                {
                                    e.Graphics.FillRectangle(hoverBg, bgRect);
                                }

                                Font f = isMajor ? majorFont : (isInfoNote ? bannerFont : (isHd ? HeadFont : Font));
                                Color c;
                                if (isInfoNote)
                                {
                                    c = dark ? Color.FromArgb(226, 200, 156) : Color.FromArgb(112, 74, 24);
                                }
                                else if (dark)
                                {
                                    c = isMajor ? Color.FromArgb(121, 192, 255) : (isHd ? Color.FromArgb(88, 166, 255)
                                        : (i == Sel ? Color.FromArgb(240, 246, 252) : Color.FromArgb(230, 237, 243)));
                                }
                                else
                                {
                                    c = isMajor ? Color.FromArgb(20, 52, 96) : (isHd ? Color.FromArgb(8, 88, 160)
                                        : (i == Sel ? Color.FromArgb(12, 34, 64) : Color.FromArgb(30, 41, 59)));
                                }
                                int leftPad = isInfoNote ? 58 : (isHd ? 14 : 18);

                                int rightReserve = isCol ? 132 : (isInfoNote ? 18 : 14);
                                if (isCol)
                                {
                                    float frac = (i < animFrac.Length) ? animFrac[i] : (isFolded ? 0f : 1f);
                                    if (isFolded || frac < 0.45f)
                                    {
                                        int cnt = (i < childItemCount.Length) ? childItemCount[i] : 0;
                                        string hintStr = "已收起 · " + cnt + " 项";
                                        Color hintCol = dark ? Color.FromArgb(139, 148, 158) : Color.FromArgb(116, 128, 146);
                                        int hx = Math.Max(r.X + leftPad + 40, w - 134);
                                        int hw = Math.Max(40, (w - 32) - hx);
                                        TextRenderer.DrawText(e.Graphics, hintStr, foldHintFont,
                                            new Rectangle(hx, r.Y, hw, h), hintCol,
                                            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                                    }

                                    float cx = w - 20f;
                                    float cy = r.Y + h * 0.5f;
                                    double theta = frac * (Math.PI * 0.5);
                                    float cos = (float)Math.Cos(theta);
                                    float sin = (float)Math.Sin(theta);
                                    for (int p = 0; p < 3; p++)
                                    {
                                        chevPts[p] = new PointF(cx + bx[p] * cos - by[p] * sin, cy + bx[p] * sin + by[p] * cos);
                                    }
                                    Pen chevPen = (isHeadHov || isFolded) ? chevPenActive : chevPenMuted;

                                    SmoothingMode oldSm = e.Graphics.SmoothingMode;
                                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                                    e.Graphics.DrawLines(chevPen, chevPts);
                                    e.Graphics.SmoothingMode = oldSm;
                                }
                                else if (!isHd && i < previewSize.Length && previewSize[i] > 0 && i < previewChipW.Length && previewChipW[i] > 0)
                                {
                                    // 右侧指令预览：直接复用测量阶段缓存的 Code Chip 宽度，零 MeasureText
                                    string cmdPreview = (i < previewTexts.Length) ? (previewTexts[i] ?? "") : "";
                                    if (cmdPreview.Length > 0)
                                    {
                                        Font pf = PreviewFontOf(previewSize[i]);
                                        int lines = (i < previewLines.Length && previewLines[i] > 0) ? previewLines[i] : 1;
                                        int lh = pf.Height + 2;
                                        int ph = lines * lh;
                                        int chipW = previewChipW[i];
                                        int chipH = Math.Min(h - 6, ph + 6);
                                        int chipX = w - chipW - 10;
                                        int chipY = r.Y + Math.Max(3, (h - chipH) / 2);
                                        Rectangle chipRect = new Rectangle(chipX, chipY, chipW, chipH);
                                        Native.FillRoundedRect(e.Graphics, codeChipBg, chipRect, 5);
                                        Native.DrawRoundedRect(e.Graphics, (i == Sel || i == hoverIdx) ? codeChipActivePen : codeChipEdgePen, chipRect, 5);

                                        Rectangle prevRect = new Rectangle(chipRect.X + 6, chipRect.Y + Math.Max(1, (chipRect.Height - ph) / 2), Math.Max(20, chipRect.Width - 12), ph);
                                        TextFormatFlags pflags = TextFormatFlags.NoPrefix
                                            | (lines > 1
                                                ? (TextFormatFlags.WordBreak | TextFormatFlags.Left)
                                                : (TextFormatFlags.SingleLine | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter));
                                        Color pc = (i == Sel || i == hoverIdx) ? ThemePalette.AccentSoftFg : ThemePalette.CodeChipFg;
                                        TextRenderer.DrawText(e.Graphics, cmdPreview, pf, prevRect, pc, pflags);
                                        rightReserve = chipW + 22;
                                    }
                                }

                                bool hasPinFeature = (ItemPinToggled != null);
                                bool pinned = (!isHd && IsRowPinned != null && IsRowPinned(i));
                                if (!isHd && hasPinFeature)
                                {
                                    leftPad = 32;
                                    if (pinned || i == hoverIdx || i == Sel)
                                    {
                                        Rectangle starRc = new Rectangle(9, r.Y + (h - 14) / 2, 14, 14);
                                        Color starCol = pinned ? ThemePalette.StarActive : ThemePalette.TextMuted;
                                        UiIcons.Draw(e.Graphics, NavIconKind.Star, starRc, starCol, pinned);
                                    }
                                }

                                int titleW = Math.Max(60, r.Width - leftPad - rightReserve);
                                Rectangle titleRect = new Rectangle(r.X + leftPad, isInfoNote ? (r.Y + 7) : (r.Y + 5), titleW, isInfoNote ? Math.Max(16, h - 12) : (h - 8));
                                bool titleSingle = (i < titleSingleLine.Length && titleSingleLine[i]);
                                DrawHighlightedTitle(e.Graphics, labelText, f, titleRect, c, !isHd, titleSingle);

                                // 标题旁的注解：直接复用测量阶段缓存的 labelOneW，零 MeasureText
                                if (!isHd && i < Notes.Length && !string.IsNullOrEmpty(Notes[i]) && i < labelOneW.Length && labelOneW[i] >= 0)
                                {
                                    int nx = r.X + leftPad + labelOneW[i] + 10;
                                    int nw = r.Right - rightReserve - nx - 6;
                                    if (nw > 40)
                                    {
                                        TextFormatFlags nflags = TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter
                                            | ((i < noteSingleLine.Length && noteSingleLine[i]) ? TextFormatFlags.SingleLine : TextFormatFlags.WordBreak);
                                        TextRenderer.DrawText(e.Graphics, Notes[i], noteFont,
                                            new Rectangle(nx, r.Y, nw, h),
                                            dark ? Color.FromArgb(139, 148, 158) : Color.FromArgb(132, 142, 156),
                                            nflags);
                                    }
                                }

                                if (!isInfoNote)
                                    e.Graphics.DrawLine(sepPen, 8, r.Bottom - 1, bgW - 8, r.Bottom - 1);
                            }
                            finally
                            {
                                if (savedClip != null)
                                {
                                    e.Graphics.Clip = savedClip;
                                    savedClip.Dispose();
                                }
                            }
                        }
                    }
                }
            }

            // 自绘右侧圆角胶囊滚动条（仅在脏区覆盖右侧槽位时重绘）
            if (barVisible && ClientSize.Width > BarWidth && ClientSize.Height > 0
                && e.ClipRectangle.Right > ClientSize.Width - BarWidth)
            {
                int bx = ClientSize.Width - BarWidth;
                using (var trackBg = new SolidBrush(dark ? Color.FromArgb(18, 22, 29) : Color.FromArgb(245, 247, 250)))
                using (var borderPen = new Pen(dark ? Color.FromArgb(33, 38, 45) : Color.FromArgb(232, 236, 242)))
                {
                    e.Graphics.FillRectangle(trackBg, bx, 0, BarWidth, ClientSize.Height);
                    e.Graphics.DrawLine(borderPen, bx, 0, bx, ClientSize.Height);
                }
                Rectangle thumb = GetBarThumbRect();
                if (!thumb.IsEmpty)
                {
                    Color tc = dark
                        ? (barDragging ? Color.FromArgb(125, 138, 156) : (barHover ? Color.FromArgb(84, 96, 114) : Color.FromArgb(56, 65, 78)))
                        : (barDragging ? Color.FromArgb(120, 134, 154) : (barHover ? Color.FromArgb(152, 165, 182) : Color.FromArgb(186, 196, 210)));
                    using (var tb = new SolidBrush(tc))
                        Native.FillRoundedRect(e.Graphics, tb, thumb, 4);
                }
            }
        }
        finally
        {
            inPaint = false;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (measuredWidth != ViewWidth) layoutValid = false;
        if (!Visible) return;
        EnsureHeights();
        UpdateBar();
        Invalidate();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible && (!layoutValid || measuredWidth != ViewWidth))
        {
            EnsureHeights();
            UpdateBar();
            Invalidate();
        }
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        measuredWidth = -1;
        monoMetricsReady = false;
        layoutValid = false;
        if (Visible)
        {
            EnsureHeights();
            Invalidate();
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ScrollTo(scrollY - e.Delta);
        base.OnMouseWheel(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        mouseDownIdx = -1;
        if (e.Button == MouseButtons.Middle)
        {
            mouseDownIdx = -2;
            panning = true;
            panStartY = e.Y;
            panStartScroll = scrollY;
            Capture = true;
            Cursor = Cursors.SizeNS;
            return;
        }
        if (InBar(e.X))
        {
            mouseDownIdx = -2;
            if (e.Button == MouseButtons.Left)
            {
                Rectangle thumb = GetBarThumbRect();
                if (!thumb.IsEmpty)
                {
                    if (e.Y >= thumb.Top && e.Y <= thumb.Bottom)
                    {
                        barDragging = true;
                        barDragOffsetY = e.Y - thumb.Top;
                        Capture = true;
                        Invalidate(new Rectangle(ClientSize.Width - BarWidth, 0, BarWidth, ClientSize.Height));
                    }
                    else
                    {
                        int step = Math.Max(40, ClientSize.Height - 40);
                        ScrollTo(e.Y < thumb.Top ? (scrollY - step) : (scrollY + step));
                    }
                }
            }
            return;
        }
        int idx = HitTest(e.Y);
        if (e.Button == MouseButtons.Left) mouseDownIdx = idx;
        if (idx >= 0)
        {
            bool isHd = (idx < Heads.Length && Heads[idx]);
            if (isHd)
            {
                if (e.Button == MouseButtons.Left && IsCollapsible(idx))
                {
                    mouseDownIdx = -2;
                    ToggleSection(idx, true);
                }
                base.OnMouseDown(e);
                return;
            }
            if (e.Button == MouseButtons.Left && ItemPinToggled != null && e.X >= 2 && e.X <= 28)
            {
                mouseDownIdx = -2;
                ItemPinToggled(idx);
                InvalidateRow(idx);
                return;
            }
            if (e.Button == MouseButtons.Right)
            {
                if (Sel != idx)
                {
                    int prev = Sel;
                    Sel = idx;
                    InvalidateRow(prev);
                    InvalidateRow(Sel);
                    if (SelChanged != null) SelChanged(this, EventArgs.Empty);
                }
                if (ItemRightClick != null) ItemRightClick(this, e);
                return;
            }
            if (Sel != idx)
            {
                int prev = Sel;
                Sel = idx;
                InvalidateRow(prev);
                InvalidateRow(Sel);
                if (SelChanged != null) SelChanged(this, EventArgs.Empty);
            }
        }
        base.OnMouseDown(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && !InBar(e.X) && mouseDownIdx != -2)
        {
            int idx = HitTest(e.Y);
            if (idx >= 0 && (mouseDownIdx == -1 || idx == mouseDownIdx))
            {
                if (IsCollapsible(idx))
                {
                    ToggleSection(idx, true);
                }
                else if (!(idx < Heads.Length && Heads[idx]) && ItemActivated != null)
                {
                    ItemActivated(this, EventArgs.Empty);
                }
            }
        }
        mouseDownIdx = -1;
        base.OnMouseClick(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (barDragging)
        {
            int h = ClientSize.Height;
            int total = TotalHeight();
            const int padY = 2;
            Rectangle thumb = GetBarThumbRect();
            int trackH = Math.Max(1, h - padY * 2);
            int travel = Math.Max(1, trackH - thumb.Height);
            int top = Math.Max(padY, Math.Min(padY + travel, e.Y - barDragOffsetY));
            int maxScroll = Math.Max(0, total - h);
            ScrollTo((int)Math.Round((double)(top - padY) * maxScroll / travel));
            return;
        }
        if (panning)
        {
            ScrollTo(panStartScroll - (e.Y - panStartY));
            return;
        }
        bool overBar = InBar(e.X);
        if (overBar != barHover)
        {
            barHover = overBar;
            if (ClientSize.Width > BarWidth)
                Invalidate(new Rectangle(ClientSize.Width - BarWidth, 0, BarWidth, ClientSize.Height));
        }
        if (overBar)
        {
            if (hoverIdx != -1)
            {
                int prev = hoverIdx;
                hoverIdx = -1;
                InvalidateRow(prev);
            }
            Cursor = Cursors.Default;
            hover.SetToolTip(this, "");
            base.OnMouseMove(e);
            return;
        }
        int idx = HitTest(e.Y);
        if (idx != hoverIdx)
        {
            int prev = hoverIdx;
            hoverIdx = idx;
            InvalidateRow(prev);
            InvalidateRow(hoverIdx);
            bool isHd = (idx >= 0 && idx < Heads.Length && Heads[idx]);
            bool isCol = isHd && IsCollapsible(idx);
            Cursor = (idx >= 0 && (!isHd || isCol)) ? Cursors.Hand : Cursors.Default;
            if (idx >= 0 && idx < Values.Length && !isHd)
            {
                string t = Values[idx] ?? "";
                if (t.Length > 600) t = t.Substring(0, 600) + "…";
                hover.SetToolTip(this, t.Length == 0 ? "" : t + "\r\n（左键单击复制，右键更多选项）");
            }
            else hover.SetToolTip(this, "");
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (barDragging && e.Button == MouseButtons.Left)
        {
            barDragging = false;
            Capture = false;
            if (ClientSize.Width > BarWidth)
                Invalidate(new Rectangle(ClientSize.Width - BarWidth, 0, BarWidth, ClientSize.Height));
            return;
        }
        if (panning && e.Button == MouseButtons.Middle)
        {
            panning = false;
            Capture = false;
            Cursor = Cursors.Default;
            return;
        }
        base.OnMouseUp(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (barHover)
        {
            barHover = false;
            if (ClientSize.Width > BarWidth)
                Invalidate(new Rectangle(ClientSize.Width - BarWidth, 0, BarWidth, ClientSize.Height));
        }
        if (hoverIdx != -1)
        {
            int prev = hoverIdx;
            hoverIdx = -1;
            InvalidateRow(prev);
        }
        Cursor = Cursors.Default;
        hover.SetToolTip(this, "");
        base.OnMouseLeave(e);
    }

    protected override bool IsInputKey(Keys keyData)
    {
        if (keyData == Keys.Up || keyData == Keys.Down
            || keyData == Keys.PageUp || keyData == Keys.PageDown
            || keyData == Keys.Home || keyData == Keys.End) return true;
        return base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down)
        {
            int next = FindSelectable(Sel + 1, 1);
            if (next >= 0 && next != Sel)
            {
                int prevSel = Sel;
                Sel = next;
                int prevScroll = scrollY;
                EnsureVisible(Sel);
                if (scrollY == prevScroll)
                {
                    InvalidateRow(prevSel);
                    InvalidateRow(Sel);
                }
                if (SelChanged != null) SelChanged(this, EventArgs.Empty);
            }
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Up)
        {
            int prev = FindSelectable((Sel <= 0 ? 0 : Sel - 1), -1);
            if (prev >= 0 && prev != Sel)
            {
                int prevSel = Sel;
                Sel = prev;
                int prevScroll = scrollY;
                EnsureVisible(Sel);
                if (scrollY == prevScroll)
                {
                    InvalidateRow(prevSel);
                    InvalidateRow(Sel);
                }
                if (SelChanged != null) SelChanged(this, EventArgs.Empty);
            }
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.PageDown)
        {
            ScrollTo(scrollY + ClientSize.Height - 40);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.PageUp)
        {
            ScrollTo(scrollY - ClientSize.Height + 40);
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Home)
        {
            ScrollTo(0);
            int first = FindSelectable(0, 1);
            if (first >= 0) { Sel = first; Invalidate(); if (SelChanged != null) SelChanged(this, EventArgs.Empty); }
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.End)
        {
            ScrollTo(TotalHeight());
            int last = FindSelectable(Labels.Length - 1, -1);
            if (last >= 0) { Sel = last; Invalidate(); if (SelChanged != null) SelChanged(this, EventArgs.Empty); }
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Enter)
        {
            if (ItemActivated != null && Sel >= 0 && !(Sel < Heads.Length && Heads[Sel]))
                ItemActivated(this, EventArgs.Empty);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    void DrawHighlightedTitle(Graphics g, string text, Font f, Rectangle rect, Color fg, bool allowHighlight, bool singleLine)
    {
        if (string.IsNullOrEmpty(text)) return;
        TextFormatFlags baseFlags = (singleLine && text.IndexOf('\n') < 0)
            ? (TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.Left)
            : (TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left);
        if (!allowHighlight || HighlightTokens == null || HighlightTokens.Length == 0 || text.IndexOf('\n') >= 0)
        {
            TextRenderer.DrawText(g, text, f, rect, fg, baseFlags);
            return;
        }

        // 只在标题单行放得下时做精细分词高亮，避免折行偏移
        Size singleSz = TextRenderer.MeasureText(g, text, f, new Size(4000, rect.Height),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        if (singleSz.Width > rect.Width)
        {
            TextRenderer.DrawText(g, text, f, rect, fg,
                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left);
            return;
        }

        bool[] mask = new bool[text.Length];
        bool anyHit = false;
        for (int t = 0; t < HighlightTokens.Length; t++)
        {
            string tok = HighlightTokens[t];
            if (string.IsNullOrEmpty(tok)) continue;
            int pos = 0;
            while (pos < text.Length)
            {
                int found = text.IndexOf(tok, pos, StringComparison.OrdinalIgnoreCase);
                if (found < 0) break;
                anyHit = true;
                for (int k = found; k < found + tok.Length && k < mask.Length; k++) mask[k] = true;
                pos = found + tok.Length;
            }
        }
        if (!anyHit)
        {
            TextRenderer.DrawText(g, text, f, rect, fg, baseFlags);
            return;
        }

        int cx = rect.X;
        int cy = rect.Y;
        int idx = 0;
        var flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        while (idx < text.Length && cx < rect.Right)
        {
            bool hi = mask[idx];
            int end = idx + 1;
            while (end < text.Length && mask[end] == hi) end++;
            string seg = text.Substring(idx, end - idx);
            Size segSz = TextRenderer.MeasureText(g, seg, f, new Size(rect.Right - cx + 20, rect.Height), flags);
            int drawW = Math.Min(segSz.Width, rect.Right - cx);
            if (hi)
            {
                using (var hb = new SolidBrush(ThemePalette.HitHighlight))
                    Native.FillRoundedRect(g, hb, new Rectangle(cx - 1, cy + 1, Math.Max(4, drawW + 2), Math.Min(rect.Height - 2, f.Height + 2)), 3);
                TextRenderer.DrawText(g, seg, f, new Rectangle(cx, cy + 1, drawW + 4, rect.Height), ThemePalette.HitHighlightFg, flags);
            }
            else
            {
                TextRenderer.DrawText(g, seg, f, new Rectangle(cx, cy + 1, drawW + 4, rect.Height), fg, flags);
            }
            cx += segSz.Width;
            idx = end;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (animTimer != null) { animTimer.Stop(); animTimer.Dispose(); animTimer = null; }
            if (HeadFont != null) { HeadFont.Dispose(); HeadFont = null; }
            if (SubFont != null) { SubFont.Dispose(); SubFont = null; }
            if (prevFontA != null) { prevFontA.Dispose(); prevFontA = null; }
            if (prevFontB != null) { prevFontB.Dispose(); prevFontB = null; }
            if (noteFont != null) { noteFont.Dispose(); noteFont = null; }
            if (noteFontS != null) { noteFontS.Dispose(); noteFontS = null; }
            if (noteFontXS != null) { noteFontXS.Dispose(); noteFontXS = null; }
            if (majorFont != null) { majorFont.Dispose(); majorFont = null; }
            if (foldHintFont != null) { foldHintFont.Dispose(); foldHintFont = null; }
            if (bannerFont != null) { bannerFont.Dispose(); bannerFont = null; }
            if (bannerBadgeFont != null) { bannerBadgeFont.Dispose(); bannerBadgeFont = null; }
            if (hover != null) { hover.Dispose(); hover = null; }
        }
        base.Dispose(disposing);
    }
}

public class Toast : Form
{
    Font font;
    string currentMsg = "";
    System.Windows.Forms.Timer hide;
    System.Windows.Forms.Timer fade;
    bool fadingIn = false;
    bool fadingOut = false;
    bool persist = false;
    float curAlpha = 0f;
    int lastW = 360, lastH = 44, lastX = 0, lastY = 58;

    public Toast()
    {
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(360, 44);
        font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);

        hide = new System.Windows.Forms.Timer();
        hide.Interval = 1800;
        hide.Tick += delegate
        {
            hide.Stop();
            fadingIn = false;
            fadingOut = true;
            fade.Start();
        };

        fade = new System.Windows.Forms.Timer();
        fade.Interval = 16;
        fade.Tick += delegate
        {
            if (IsDisposed) { fade.Stop(); return; }
            if (fadingIn)
            {
                curAlpha += 0.16f;
                if (curAlpha >= 0.96f) { curAlpha = 0.96f; fadingIn = false; fade.Stop(); }
                PushLayer();
            }
            else if (fadingOut)
            {
                curAlpha -= 0.13f;
                if (curAlpha <= 0.04f) { curAlpha = 0f; fadingOut = false; fade.Stop(); Visible = false; }
                else PushLayer();
            }
            else
            {
                fade.Stop();
            }
        };

        Visible = false;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x20;       // WS_EX_TRANSPARENT
            cp.ExStyle |= 0x80000;    // WS_EX_LAYERED
            cp.ExStyle |= 0x80;       // WS_EX_TOOLWINDOW
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            return cp;
        }
    }

    Color AccentForMessage(string msg)
    {
        if (persist) return Color.FromArgb(248, 113, 113);
        if (string.IsNullOrEmpty(msg)) return Color.FromArgb(88, 166, 255);
        if (msg.Contains("失败") || msg.Contains("错误") || msg.Contains("无法") || msg.Contains("冲突") || msg.Contains("占用"))
            return Color.FromArgb(248, 113, 113);
        if (msg.Contains("完成") || msg.Contains("已保存") || msg.Contains("已恢复") || msg.Contains("已收藏") || msg.Contains("开启"))
            return Color.FromArgb(74, 222, 128);
        if (msg.Contains("已复制"))
            return Color.FromArgb(96, 165, 250);
        return Color.FromArgb(250, 204, 21);
    }

    void ComputeBounds(string msg)
    {
        Size sz = TextRenderer.MeasureText(msg, font);
        lastW = Math.Max(260, Math.Min(820, sz.Width + 64));
        lastH = 44;
        Rectangle scr = Native.GetTargetScreenBounds();
        lastX = scr.X + (scr.Width - lastW) / 2;
        lastY = scr.Y + 56;
    }

    void PushLayer()
    {
        if (IsDisposed || !IsHandleCreated) return;
        try
        {
            int w = Math.Max(120, lastW);
            int h = Math.Max(36, lastH);
            using (var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.FromArgb(0, 0, 0, 0));
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                    Color dot = AccentForMessage(currentMsg);
                    Color fillCol = persist ? Color.FromArgb(236, 78, 22, 26) : Color.FromArgb(236, 17, 24, 36);
                    Color edgeCol = persist ? Color.FromArgb(210, 220, 82, 82) : Color.FromArgb(145, dot.R, dot.G, dot.B);

                    var pill = new Rectangle(1, 1, w - 3, h - 3);
                    using (var br = new SolidBrush(fillCol))
                        Native.FillRoundedRect(g, br, pill, 12);
                    using (var pen = new Pen(edgeCol, 1.2f))
                        Native.DrawRoundedRect(g, pen, pill, 12);

                    int dotY = (h - 8) / 2;
                    using (var halo = new SolidBrush(Color.FromArgb(65, dot.R, dot.G, dot.B)))
                        g.FillEllipse(halo, 14, dotY - 3, 14, 14);
                    using (var db = new SolidBrush(dot))
                        g.FillEllipse(db, 17, dotY, 8, 8);

                    using (var sf = new StringFormat())
                    using (var tb = new SolidBrush(Color.FromArgb(245, 248, 252)))
                    {
                        sf.Alignment = StringAlignment.Near;
                        sf.LineAlignment = StringAlignment.Center;
                        sf.Trimming = StringTrimming.EllipsisCharacter;
                        sf.FormatFlags = StringFormatFlags.NoWrap;
                        g.DrawString(currentMsg, font, tb, new RectangleF(34, 0, w - 48, h), sf);
                    }
                }
                byte alpha = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(curAlpha * 255f)));
                Native.PushLayeredBitmap(Handle, bmp, lastX, lastY, alpha);
                Native.MakeTopmost(Handle);
            }
        }
        catch { }
    }

    public void ShowText(string text)
    {
        if (IsDisposed) return;
        currentMsg = text ?? "";
        persist = false;
        ComputeBounds(currentMsg);
        if (!IsHandleCreated) CreateHandle();
        fadingOut = false;
        if (!Visible) curAlpha = 0.22f;
        fadingIn = (curAlpha < 0.94f);
        Visible = true;
        PushLayer();
        if (fadingIn) fade.Start();
        hide.Stop();
        hide.Start();
    }

    public void ShowPersistent(string text)
    {
        if (IsDisposed) return;
        string msg = text ?? "";
        if (persist && Visible && currentMsg == msg) return;
        currentMsg = msg;
        persist = true;
        fadingIn = false;
        fadingOut = false;
        fade.Stop();
        curAlpha = 0.96f;
        ComputeBounds(currentMsg);
        if (!IsHandleCreated) CreateHandle();
        Visible = true;
        hide.Stop();
        PushLayer();
    }

    public bool IsPersistent { get { return persist; } }

    public void HideIfPersistent()
    {
        if (IsDisposed || !persist) return;
        persist = false;
        fadingIn = false;
        fadingOut = false;
        fade.Stop();
        hide.Stop();
        Visible = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (hide != null) { hide.Stop(); hide.Dispose(); hide = null; }
            if (fade != null) { fade.Stop(); fade.Dispose(); fade = null; }
            if (font != null) { font.Dispose(); font = null; }
        }
        base.Dispose(disposing);
    }
}

public class CrosshairPreview : Panel
{
    public Overlay Source;
    int bgMode = 0;   // 0=暗夜, 1=亮底, 2=棋盘

    public CrosshairPreview(Overlay src)
    {
        Source = src;
        BackColor = Color.FromArgb(20, 24, 32);
        BorderStyle = BorderStyle.None;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right)
        {
            bgMode = (bgMode + 1) % 3;
            Invalidate();
            return;
        }
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int w = ClientSize.Width, h = ClientSize.Height;
        float cx = w / 2f, cy = h / 2f;

        if (bgMode == 0)
        {
            g.Clear(Color.FromArgb(20, 24, 32));
        }
        else if (bgMode == 1)
        {
            g.Clear(Color.FromArgb(236, 240, 245));
        }
        else
        {
            int sq = 12;
            using (var b1 = new SolidBrush(Color.FromArgb(205, 212, 222)))
            using (var b2 = new SolidBrush(Color.FromArgb(165, 174, 188)))
            {
                for (int y = 0; y < h; y += sq)
                    for (int x = 0; x < w; x += sq)
                        g.FillRectangle((((x / sq) + (y / sq)) % 2 == 0) ? b1 : b2, x, y, sq, sq);
            }
        }

        Color gridCol = (bgMode == 0) ? Color.FromArgb(44, 52, 66) : Color.FromArgb(120, 130, 145);
        using (var gridPen = new Pen(gridCol, 1f))
        {
            g.DrawLine(gridPen, 0, cy, w, cy);
            g.DrawLine(gridPen, cx, 0, cx, h);
            g.DrawRectangle(gridPen, 16, 16, Math.Max(10, w - 32), Math.Max(10, h - 32));
        }

        if (Source != null)
        {
            int ox = Overlay.ClampOffset(Source.OffsetX);
            int oy = Overlay.ClampOffset(Source.OffsetY);
            Source.DrawCrosshair(g, cx + ox, cy + oy);

            if (ox != 0 || oy != 0)
            {
                string offTxt = "偏移 " + (ox >= 0 ? "+" : "") + ox + ", " + (oy >= 0 ? "+" : "") + oy + " px";
                Size osz = TextRenderer.MeasureText(offTxt, Font, new Size(w, 24), TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                int pw = Math.Min(w - 12, osz.Width + 10);
                var pillRect = new Rectangle(w - pw - 6, 6, pw, 18);
                using (var pillBg = new SolidBrush(Color.FromArgb(180, 15, 23, 42)))
                    Native.FillRoundedRect(g, pillBg, pillRect, 4);
                TextRenderer.DrawText(g, offTxt, Font, pillRect, Color.FromArgb(125, 211, 252),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
        }

        using (var borderPen = new Pen(ThemePalette.Border, 1f))
            g.DrawRectangle(borderPen, 0, 0, w - 1, h - 1);

        string bgLabel = (bgMode == 0) ? "暗底" : (bgMode == 1 ? "亮底" : "棋盘");
        using (var pillBg = new SolidBrush(Color.FromArgb(165, 15, 20, 28)))
            Native.FillRoundedRect(g, pillBg, new Rectangle(6, h - 25, w - 12, 19), 4);
        TextRenderer.DrawText(g, "左键换色 · 右键切底(" + bgLabel + ")", Font,
            new Rectangle(6, h - 25, w - 12, 19), Color.FromArgb(225, 232, 242),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
    }
}

public class TextBoxFrame : NativeWindow
{
    readonly TextBox tb;
    bool attached = false;

    public TextBoxFrame(TextBox box)
    {
        tb = box;
        if (tb.IsHandleCreated) Attach();
        tb.HandleCreated += delegate { Attach(); };
        tb.HandleDestroyed += delegate { Detach(); };
        tb.GotFocus += delegate { RedrawFrame(); };
        tb.LostFocus += delegate { RedrawFrame(); };
    }

    void Attach()
    {
        if (tb == null || tb.IsDisposed || !tb.IsHandleCreated) return;
        if (attached && Handle == tb.Handle) return;
        Detach();
        AssignHandle(tb.Handle);
        attached = true;
        Native.ApplyDarkScrollBar(tb.Handle, MainForm.IsDark);
        RedrawFrame();
    }

    void Detach()
    {
        if (attached)
        {
            try { ReleaseHandle(); } catch { }
            attached = false;
        }
    }

    public void RedrawFrame()
    {
        if (tb == null || tb.IsDisposed || !tb.IsHandleCreated) return;
        try { Native.SetWindowPos(tb.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0037); }
        catch { }
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == 0x0085 || m.Msg == 0x000F)   // WM_NCPAINT / WM_PAINT
            PaintNcBorder();
    }

    void PaintNcBorder()
    {
        if (tb == null || tb.IsDisposed || !tb.IsHandleCreated) return;
        if (tb.BorderStyle == BorderStyle.None) return;
        Native.RECT rc;
        if (!Native.GetWindowRect(tb.Handle, out rc)) return;
        int w = rc.R - rc.L;
        int h = rc.B - rc.T;
        if (w <= 2 || h <= 2) return;
        IntPtr hdc = Native.GetWindowDC(tb.Handle);
        if (hdc == IntPtr.Zero) return;
        try
        {
            bool dark = MainForm.IsDark;
            Color border = tb.Focused
                ? (dark ? Color.FromArgb(88, 166, 255) : Color.FromArgb(59, 130, 246))
                : (dark ? Color.FromArgb(48, 54, 61) : Color.FromArgb(203, 213, 225));
            using (var g = Graphics.FromHdc(hdc))
            {
                if (tb.BorderStyle == BorderStyle.Fixed3D)
                {
                    using (var innerPen = new Pen(tb.BackColor))
                        g.DrawRectangle(innerPen, 1, 1, w - 3, h - 3);
                }
                using (var outerPen = new Pen(border))
                    g.DrawRectangle(outerPen, 0, 0, w - 1, h - 1);
            }
        }
        catch { }
        finally
        {
            Native.ReleaseDC(tb.Handle, hdc);
        }
    }
}
