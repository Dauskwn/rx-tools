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
    public string EmptyText = "（暂无条目）";
    public Font HeadFont;
    public Font SubFont;
    public int Sel = -1;

    public event EventHandler SelChanged;
    public event EventHandler ItemActivated;
    public event MouseEventHandler ItemRightClick;

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
    Font prevFontA, prevFontB, noteFont, noteFontS, noteFontXS, majorFont, foldHintFont;
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
    int layoutTotalH = 0;
    bool layoutValid = false;

    public WrapList()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
               | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
               | ControlStyles.Selectable, true);
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

    int ViewWidth { get { return Math.Max(60, ClientSize.Width - (barVisible ? BarWidth : 0)); } }

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

    public bool IsCollapsed(int index)
    {
        return index >= 0 && index < collapsed.Length && collapsed[index];
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
        measuredWidth = -1;
        layoutValid = false;
        scrollY = 0;
        hoverIdx = -1;
        mouseDownIdx = -1;
        if (Sel >= Labels.Length || (Sel >= 0 && IsRowHidden(Sel))) Sel = -1;
        EnsureHeights();
        Invalidate();
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

    void MeasureAll()
    {
        int w = ViewWidth;
        measuredWidth = w;
        heights = new int[Labels.Length];
        previewSize = new int[Labels.Length];
        previewW = new int[Labels.Length];
        previewLines = new int[Labels.Length];
        for (int i = 0; i < Labels.Length; i++)
        {
            bool isHd = (i < Heads.Length && Heads[i]);
            string rawText = Labels[i] ?? "";
            bool isMajor = isHd && rawText.StartsWith("▓");
            bool isCol = isHd && i < canCollapse.Length && canCollapse[i];
            Font f = isMajor ? majorFont : (isHd ? HeadFont : Font);
            int tw = w - (isHd ? (isCol ? 138 : 24) : 36);
            if (tw < 60) tw = 60;
            string text = isMajor ? rawText.Substring(1).Trim() : rawText;
            int h;
            if (!isHd && w >= 400 && text.Length <= 22 && text.IndexOf('\n') < 0)
            {
                h = Math.Max(34, f.Height + 12);
            }
            else
            {
                Size sz = TextRenderer.MeasureText(text, f, new Size(tw, 4000),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                h = Math.Max(34, sz.Height + (isHd ? 14 : 12));
            }

            // 标题旁的注解可能换 2~4 行：用最小字号实测一下需要多高，避免被截断
            string nt = (i < Notes.Length) ? (Notes[i] ?? "") : "";
            if (!isHd && nt.Length > 0)
            {
                int noteW = Math.Max(160, (w - 300) / 2);    // 注解可用宽度（与实际绘制接近）
                Size ns = TextRenderer.MeasureText(nt, noteFont, new Size(noteW, 4000),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                int need = ns.Height + 14;                   // 上下留白
                if (need > h) h = Math.Min(need, 150);       // 行高跟着注解走，最多 5~6 行
            }

            // 右侧指令预览：先缩字号试一行，仍放不下就换成两行并抬高这一行
            if (!isHd && w >= 460 && i < Values.Length && Values[i] != null)
            {
                string cmd = PreviewTextOf(Values[i]);
                if (cmd.Length > 0)
                {
                    int leftPad = 18;
                    Size labelSz = TextRenderer.MeasureText(text, f);
                    int availRight = w - (leftPad + labelSz.Width + 46);
                    if (availRight >= 150)
                    {
                        bool hasNoteRow = (i < Notes.Length && !string.IsNullOrEmpty(Notes[i]));
                        // 有注解的行把指令预览压窄，省出的宽度给注解，少折几行
                        int maxW = Math.Min((int)(w * (hasNoteRow ? 0.34 : 0.60)), availRight);
                        if (maxW < 150) maxW = 150;
                        int chosen = 0;
                        for (int k = 1; k <= 2; k++)
                        {
                            Size ps = TextRenderer.MeasureText(cmd, PreviewFontOf(k));
                            if (ps.Width <= maxW) { chosen = k; break; }
                        }
                        int lines = 1;
                        int ph = 0;
                        if (chosen == 0)
                        {
                            chosen = 2;
                            Font pf = PreviewFontOf(2);
                            Size ps = TextRenderer.MeasureText(cmd, pf, new Size(maxW, 300),
                                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                            int lh = pf.Height + 2;
                            lines = Math.Max(1, Math.Min(2, (ps.Height + lh - 1) / lh));
                            ph = lines * lh;
                        }
                        previewSize[i] = chosen;
                        previewW[i] = maxW;
                        previewLines[i] = lines;
                        if (ph > 0)
                        {
                            int need = ph + 12;
                            if (need > h) h = need;
                        }
                    }
                }
            }
            heights[i] = h;
        }
        layoutValid = false;
    }

    void ComputeSpan(int start, int end, int clipTop, int limitBottom, ref int y, bool writePositions)
    {
        int i = start;
        while (i < end)
        {
            bool isHd = (i < Heads.Length && Heads[i]);
            int rh = (i < heights.Length) ? heights[i] : 34;
            if (writePositions && i < visY.Length)
            {
                int ct = Math.Max(clipTop, y);
                int cb = Math.Min(limitBottom, y + rh);
                if (cb > ct)
                {
                    visY[i] = y;
                    visClipTop[i] = ct;
                    visClipBot[i] = cb;
                }
                else
                {
                    visY[i] = -1;
                    visClipTop[i] = 0;
                    visClipBot[i] = 0;
                }
            }
            y += rh;

            if (isHd && i < canCollapse.Length && canCollapse[i])
            {
                int spanStart = i + 1;
                int spanEnd = Math.Min(end, secEnd[i]);
                int spanTop = y;
                int natY = spanTop;
                ComputeSpan(spanStart, spanEnd, spanTop, int.MaxValue, ref natY, false);
                int naturalSpanH = natY - spanTop;

                int drawerH;
                if (animActive[i])
                {
                    int maxAnim = Math.Min(naturalSpanH, Math.Max(220, ClientSize.Height));
                    drawerH = (int)Math.Round(maxAnim * animFrac[i]);
                }
                else if (collapsed[i])
                {
                    drawerH = 0;
                }
                else
                {
                    drawerH = naturalSpanH;
                }

                if (writePositions && spanEnd > spanStart)
                {
                    int innerLimit = Math.Min(limitBottom, spanTop + drawerH);
                    if (drawerH <= 0 || innerLimit <= spanTop)
                    {
                        for (int k = spanStart; k < spanEnd; k++)
                        {
                            visY[k] = -1;
                            visClipTop[k] = 0;
                            visClipBot[k] = 0;
                        }
                    }
                    else
                    {
                        int dummyY = spanTop;
                        ComputeSpan(spanStart, spanEnd, spanTop, innerLimit, ref dummyY, true);
                    }
                }
                y = spanTop + drawerH;
                i = spanEnd;
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
        if (visY.Length != n)
        {
            visY = new int[n];
            visClipTop = new int[n];
            visClipBot = new int[n];
        }
        int y = 0;
        ComputeSpan(0, n, 0, int.MaxValue, ref y, true);
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
            return;
        }
        animStartFrac[index] = animFrac[index];
        animStartUtc[index] = DateTime.UtcNow.Ticks;
        animActive[index] = true;
        layoutValid = false;
        EnsureHeights();
        Invalidate();
        animTimer.Start();
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
    }

    void EnsureHeights()
    {
        int w = ViewWidth;
        if (heights.Length != Labels.Length || measuredWidth != w)
        {
            MeasureAll();
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
            MeasureAll();
            RebuildLayout();
            total = layoutTotalH;
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

    int HitTest(int y)
    {
        EnsureHeights();
        int py = y + scrollY;
        for (int i = 0; i < visY.Length; i++)
        {
            if (visY[i] >= 0 && py >= visClipTop[i] && py < visClipBot[i]) return i;
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
        int bottom = top + ((index < heights.Length) ? heights[index] : 34);
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
        using (var majorBg = new SolidBrush(dark ? Color.FromArgb(32, 39, 51) : Color.FromArgb(236, 242, 252)))
        using (var majorHoverBg = new SolidBrush(dark ? Color.FromArgb(40, 50, 66) : Color.FromArgb(224, 234, 250)))
        using (var headBg = new SolidBrush(dark ? Color.FromArgb(28, 33, 40) : Color.FromArgb(244, 247, 252)))
        using (var headHoverBg = new SolidBrush(dark ? Color.FromArgb(35, 42, 52) : Color.FromArgb(234, 241, 252)))
        using (var headAccent = new SolidBrush(dark ? Color.FromArgb(88, 166, 255) : Color.FromArgb(37, 99, 235)))
        using (var selBg = new SolidBrush(dark ? Color.FromArgb(26, 42, 68) : Color.FromArgb(235, 244, 255)))
        using (var hoverBg = new SolidBrush(dark ? Color.FromArgb(28, 34, 44) : Color.FromArgb(246, 249, 254)))
        using (var sepPen = new Pen(dark ? Color.FromArgb(33, 38, 45) : Color.FromArgb(241, 245, 249)))
        {
            for (int i = 0; i < Labels.Length; i++)
            {
                if (i >= visY.Length || visY[i] < 0) continue;
                int h = heights[i];
                int y = visY[i] - scrollY;
                int cTop = visClipTop[i] - scrollY;
                int cBot = visClipBot[i] - scrollY;
                if (y > ClientSize.Height) break;
                if (cBot <= 0 || cTop >= ClientSize.Height || cBot <= cTop) continue;

                bool clipped = (visClipTop[i] > visY[i] || visClipBot[i] < visY[i] + h);
                Region savedClip = null;
                if (clipped)
                {
                    savedClip = e.Graphics.Clip;
                    e.Graphics.SetClip(new Rectangle(0, cTop, w, cBot - cTop), CombineMode.Intersect);
                }
                try
                {
                    Rectangle r = new Rectangle(0, y, w, h);
                    bool isHd = (i < Heads.Length && Heads[i]);
                    bool isCol = isHd && i < canCollapse.Length && canCollapse[i];
                    bool isFolded = isCol && i < collapsed.Length && collapsed[i];
                    bool isHeadHov = isCol && (i == hoverIdx);
                    string labelText = Labels[i] ?? "";
                    bool isMajor = isHd && labelText.StartsWith("▓");
                    if (isMajor) labelText = labelText.Substring(1).Trim();
                    if (isHd)
                    {
                        Brush hb = isMajor ? (isHeadHov ? majorHoverBg : majorBg) : (isHeadHov ? headHoverBg : headBg);
                        e.Graphics.FillRectangle(hb, r);
                        Native.FillRoundedRect(e.Graphics, headAccent, new Rectangle(2, y + 6, 3, Math.Max(4, h - 12)), 2);
                    }
                    else if (i == Sel)
                    {
                        e.Graphics.FillRectangle(selBg, r);
                        Native.FillRoundedRect(e.Graphics, headAccent, new Rectangle(2, y + 3, 3, Math.Max(4, h - 6)), 2);
                    }
                    else if (i == hoverIdx)
                    {
                        e.Graphics.FillRectangle(hoverBg, r);
                    }

                    Font f = isMajor ? majorFont : (isHd ? HeadFont : Font);
                    Color c;
                    if (dark)
                    {
                        c = isMajor ? Color.FromArgb(121, 192, 255) : (isHd ? Color.FromArgb(88, 166, 255)
                            : (i == Sel ? Color.FromArgb(240, 246, 252) : Color.FromArgb(230, 237, 243)));
                    }
                    else
                    {
                        c = isMajor ? Color.FromArgb(20, 52, 96) : (isHd ? Color.FromArgb(8, 88, 160)
                            : (i == Sel ? Color.FromArgb(12, 34, 64) : Color.FromArgb(30, 41, 59)));
                    }
                    int leftPad = isHd ? 14 : 18;

                    int rightReserve = isCol ? 132 : 14;
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
                        PointF[] pts = new PointF[3];
                        float[] bx = new float[] { -2.5f, 2.5f, -2.5f };
                        float[] by = new float[] { -4.5f, 0f, 4.5f };
                        for (int k = 0; k < 3; k++)
                        {
                            pts[k] = new PointF(cx + bx[k] * cos - by[k] * sin, cy + bx[k] * sin + by[k] * cos);
                        }
                        Color chevCol;
                        if (isHeadHov || isFolded)
                            chevCol = dark ? Color.FromArgb(121, 192, 255) : Color.FromArgb(37, 99, 235);
                        else
                            chevCol = dark ? Color.FromArgb(125, 140, 160) : Color.FromArgb(118, 134, 156);

                        SmoothingMode oldSm = e.Graphics.SmoothingMode;
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        using (var chevPen = new Pen(chevCol, 1.7f))
                        {
                            chevPen.StartCap = LineCap.Round;
                            chevPen.EndCap = LineCap.Round;
                            chevPen.LineJoin = LineJoin.Round;
                            e.Graphics.DrawLines(chevPen, pts);
                        }
                        e.Graphics.SmoothingMode = oldSm;
                    }
                    else if (!isHd && i < previewSize.Length && previewSize[i] > 0 && i < previewW.Length && previewW[i] > 0)
                    {
                        // 右侧指令预览：字体自适应（8.5/7.5/6.8 三档），放不下就换行，不再硬截断
                        string cmdPreview = PreviewTextOf(i < Values.Length ? Values[i] : null);
                        if (cmdPreview.Length > 0)
                        {
                            Font pf = PreviewFontOf(previewSize[i]);
                            int lines = (i < previewLines.Length && previewLines[i] > 0) ? previewLines[i] : 1;
                            int lh = pf.Height + 2;
                            int ph = lines * lh;
                            int maxW = previewW[i];
                            int py = r.Y + Math.Max(4, (h - ph) / 2);
                            Rectangle prevRect = new Rectangle(w - maxW - 14, py, maxW, ph);
                            TextFormatFlags pflags = TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak
                                | (lines > 1 ? TextFormatFlags.Left : TextFormatFlags.Right);
                            Color pc = dark
                                ? ((i == Sel || i == hoverIdx) ? Color.FromArgb(121, 192, 255) : Color.FromArgb(125, 148, 176))
                                : ((i == Sel || i == hoverIdx) ? Color.FromArgb(36, 96, 168) : Color.FromArgb(118, 130, 148));
                            TextRenderer.DrawText(e.Graphics, cmdPreview, pf, prevRect, pc, pflags);
                            rightReserve = maxW + 24;
                        }
                    }

                    int titleW = Math.Max(60, r.Width - leftPad - rightReserve);
                    TextRenderer.DrawText(e.Graphics, labelText, f,
                        new Rectangle(r.X + leftPad, r.Y + 5, titleW, h - 8), c,
                        TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left);

                    // 标题旁的注解：小一号斜体灰字，只在标题单行放得下时并排显示
                    string noteText = (i < Notes.Length) ? (Notes[i] ?? "") : "";
                    if (!isHd && noteText.Length > 0)
                    {
                        Size one = TextRenderer.MeasureText(e.Graphics, labelText, f,
                            new Size(4000, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                        Size wrapped = TextRenderer.MeasureText(e.Graphics, labelText, f,
                            new Size(titleW, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                        if (wrapped.Height <= f.Height + 6)
                        {
                            int nx = r.X + leftPad + one.Width + 10;
                            int nw = r.Right - rightReserve - nx - 6;   // 右边留给指令预览，不越界
                            if (nw > 40)
                            {
                                // 字号固定 8pt 斜体，不再缩小；放不下就靠行高变高来容纳
                                TextRenderer.DrawText(e.Graphics, noteText, noteFont,
                                    new Rectangle(nx, r.Y, nw, h),
                                    dark ? Color.FromArgb(139, 148, 158) : Color.FromArgb(132, 142, 156),
                                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter);
                            }
                        }
                    }

                    e.Graphics.DrawLine(sepPen, 8, r.Bottom - 1, w - 8, r.Bottom - 1);
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

        // 自绘右侧圆角胶囊滚动条（与列表共用同一双缓冲表面，避免子窗口句柄在分辨率变化后变黑）
        if (barVisible && ClientSize.Width > BarWidth && ClientSize.Height > 0)
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

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        measuredWidth = -1;
        layoutValid = false;
        EnsureHeights();
        Invalidate();
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
                base.OnMouseDown(e);
                return;
            }
            if (e.Button == MouseButtons.Right)
            {
                Sel = idx;
                Invalidate();
                if (SelChanged != null) SelChanged(this, EventArgs.Empty);
                if (ItemRightClick != null) ItemRightClick(this, e);
                return;
            }
            if (Sel != idx)
            {
                Sel = idx;
                Invalidate();
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
                hoverIdx = -1;
                Invalidate();
            }
            Cursor = Cursors.Default;
            hover.SetToolTip(this, "");
            base.OnMouseMove(e);
            return;
        }
        int idx = HitTest(e.Y);
        if (idx != hoverIdx)
        {
            hoverIdx = idx;
            Invalidate();
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
            hoverIdx = -1;
            Invalidate();
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
                Sel = next;
                EnsureVisible(Sel);
                Invalidate();
                if (SelChanged != null) SelChanged(this, EventArgs.Empty);
            }
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Up)
        {
            int prev = FindSelectable((Sel <= 0 ? 0 : Sel - 1), -1);
            if (prev >= 0 && prev != Sel)
            {
                Sel = prev;
                EnsureVisible(Sel);
                Invalidate();
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
            if (hover != null) { hover.Dispose(); hover = null; }
        }
        base.Dispose(disposing);
    }
}

public class Toast : Form
{
    Label lbl;
    System.Windows.Forms.Timer hide;
    System.Windows.Forms.Timer fade;
    bool fadingIn = false;
    bool fadingOut = false;

    public Toast()
    {
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(20, 26, 36);
        Opacity = 0.90;
        Size = new Size(420, 46);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);

        lbl = new Label();
        lbl.Dock = DockStyle.Fill;
        lbl.TextAlign = ContentAlignment.MiddleCenter;
        lbl.ForeColor = Color.White;
        lbl.Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
        lbl.BackColor = Color.Transparent;
        Controls.Add(lbl);

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
                double next = Opacity + 0.14;
                if (next >= 0.90) { Opacity = 0.90; fadingIn = false; fade.Stop(); }
                else Opacity = next;
            }
            else if (fadingOut)
            {
                double next = Opacity - 0.12;
                if (next <= 0.04) { fadingOut = false; fade.Stop(); Visible = false; Opacity = 0.90; }
                else Opacity = next;
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
            cp.ExStyle |= 0x20;
            cp.ExStyle |= 0x80;
            cp.ExStyle |= 0x08000000;
            return cp;
        }
    }

    bool persist = false;

    void Lay(string msg)
    {
        Size sz = TextRenderer.MeasureText(msg, lbl.Font);
        int w = Math.Max(340, Math.Min(820, sz.Width + 56));
        int h = 46;
        if (Width != w || Height != h) Size = new Size(w, h);
        Native.ApplyRoundRegion(this, 10);
        int sw = Native.GetSystemMetrics(0);
        Native.SetWindowPos(Handle, new IntPtr(-1), (sw - w) / 2, 60, w, h, 0x0010);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using (var pen = new Pen(persist ? Color.FromArgb(180, 72, 72) : Color.FromArgb(72, 92, 124), 1f))
            Native.DrawRoundedRect(e.Graphics, pen, new Rectangle(0, 0, Width - 1, Height - 1), 10);
    }

    public void ShowText(string text)
    {
        if (IsDisposed) return;
        string msg = text ?? "";
        persist = false;
        BackColor = Color.FromArgb(20, 26, 36);
        lbl.Text = msg;
        Lay(msg);
        fadingOut = false;
        if (!Visible) Opacity = 0.18;
        fadingIn = (Opacity < 0.89);
        Visible = true;
        if (fadingIn) fade.Start();
        hide.Stop();
        hide.Start();
        Invalidate();
    }

    // 常驻提示：不自动消失，重复调用同一条文本时不做任何重绘（避免闪烁）
    public void ShowPersistent(string text)
    {
        if (IsDisposed) return;
        string msg = text ?? "";
        if (persist && Visible && lbl.Text == msg) return;
        persist = true;
        fadingIn = false;
        fadingOut = false;
        fade.Stop();
        Opacity = 0.90;
        BackColor = Color.FromArgb(96, 30, 30);
        lbl.Text = msg;
        Lay(msg);
        Visible = true;
        hide.Stop();
        Invalidate();
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
            if (lbl != null && lbl.Font != null) { lbl.Font.Dispose(); }
        }
        base.Dispose(disposing);
    }
}

public class CrosshairPreview : Panel
{
    public Overlay Source;

    public CrosshairPreview(Overlay src)
    {
        Source = src;
        BackColor = Color.FromArgb(26, 30, 38);
        BorderStyle = BorderStyle.FixedSingle;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        int w = ClientSize.Width, h = ClientSize.Height;
        float cx = w / 2f, cy = h / 2f;

        using (var gridPen = new Pen(Color.FromArgb(46, 52, 62), 1f))
        {
            g.DrawLine(gridPen, 0, cy, w, cy);
            g.DrawLine(gridPen, cx, 0, cx, h);
            g.DrawRectangle(gridPen, 16, 16, Math.Max(10, w - 32), Math.Max(10, h - 32));
        }

        if (Source != null)
        {
            Source.DrawCrosshair(g, cx, cy);
        }

        TextRenderer.DrawText(g, "实时预览（点击换色）", Font, new Point(6, h - 22), Color.FromArgb(160, 170, 182));
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
