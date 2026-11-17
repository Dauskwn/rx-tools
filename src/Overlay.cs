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

public class Overlay : Form
{
    public static readonly Color[] Palette = { Color.Lime, Color.Cyan, Color.DeepSkyBlue, Color.Yellow, Color.OrangeRed, Color.White };
    public static readonly string[] ColorNames = { "荧光绿", "青色", "天蓝", "亮黄", "橙红", "纯白" };
    public static readonly string[] Styles = { "十字(带缺口)", "完整十字", "圆点", "V 形", "同心圆", "T 形" };

    public Color HairColor = Color.Lime;
    public int Thickness = 2, Length = 9, Gap = 5, DotSize = 2;
    public int StyleIndex = 0;
    // 自定义准星图片（导入 PNG/BMP）；路径为空表示用内置矢量样式
    public Bitmap CustomImage;
    public string CustomImagePath = "";

    public Overlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        Size = new Size(240, 240);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
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

    public int ColorIndex
    {
        get
        {
            for (int i = 0; i < Palette.Length; i++)
                if (Palette[i].ToArgb() == HairColor.ToArgb()) return i;
            return 0;
        }
        set
        {
            int idx = ((value % Palette.Length) + Palette.Length) % Palette.Length;
            HairColor = Palette[idx];
            Invalidate();
        }
    }

    public void CycleColor()
    {
        ColorIndex = ColorIndex + 1;
    }

    public void CycleStyle()
    {
        StyleIndex = (StyleIndex + 1) % Styles.Length;
        Invalidate();
    }

    public void DrawCrosshair(Graphics g, float cx, float cy)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        float g0 = Gap, len = Length;

        // 导入的图片优先：按「臂长+间隙」的两倍撑开，取最长边等比缩放，最近邻保持像素锐利
        if (CustomImage != null)
        {
            float side = Math.Max(8f, Math.Min(300f, (Length + Gap) * 2f));
            float sc = side / Math.Max(1, Math.Max(CustomImage.Width, CustomImage.Height));
            float iw = Math.Max(1f, CustomImage.Width * sc);
            float ih = Math.Max(1f, CustomImage.Height * sc);
            var oldInterp = g.InterpolationMode;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            // 洋红当透明色：叠色窗口里它是透明键，预览控件里也会真透明
            using (var attrs = new System.Drawing.Imaging.ImageAttributes())
            {
                attrs.SetColorKey(Color.Magenta, Color.Magenta);
                int dx = (int)Math.Round(cx - iw / 2f), dy = (int)Math.Round(cy - ih / 2f);
                int dw = Math.Max(1, (int)Math.Round(iw)), dh = Math.Max(1, (int)Math.Round(ih));
                var dest = new Rectangle(dx, dy, dw, dh);
                g.DrawImage(CustomImage, dest, 0, 0, CustomImage.Width, CustomImage.Height,
                    GraphicsUnit.Pixel, attrs);
            }
            g.InterpolationMode = oldInterp;
            return;
        }

        // 粗线宽为奇数时偏移半像素，避免线条跨到两个像素上显得发虚/发歪
        if (Thickness % 2 == 1) { cx += 0.5f; cy += 0.5f; }

        using (var pen = new Pen(HairColor, Thickness))
        using (var brush = new SolidBrush(HairColor))
        {
            switch (StyleIndex)
            {
                case 0:
                    g.DrawLine(pen, cx - g0 - len, cy, cx - g0, cy);
                    g.DrawLine(pen, cx + g0, cy, cx + g0 + len, cy);
                    g.DrawLine(pen, cx, cy - g0 - len, cx, cy - g0);
                    g.DrawLine(pen, cx, cy + g0, cx, cy + g0 + len);
                    if (DotSize > 0)
                        g.FillEllipse(brush, cx - DotSize / 2f, cy - DotSize / 2f, DotSize, DotSize);
                    break;
                case 1:
                    g.DrawLine(pen, cx - g0 - len, cy, cx + g0 + len, cy);
                    g.DrawLine(pen, cx, cy - g0 - len, cx, cy + g0 + len);
                    break;
                case 2:
                    float r = Math.Max(2.5f, (DotSize > 0 ? DotSize : Thickness) * 1.6f);
                    g.FillEllipse(brush, cx - r, cy - r, r * 2, r * 2);
                    break;
                case 3:
                {
                    // V 形：三点折线（左臂→顶点→右臂），偏移取整保证左右完全对称，
                    // 用 Miter 连接让顶点是一个干净的尖角（两条线分开画会留缺口）
                    float s = len + g0;
                    // 比十字样式收一档，别的样式半径是 len+gap，V 只取一半多一点，
                    // 否则同参数下它比别的准星大一圈
                    float half = (float)Math.Round(s * 0.45f);
                    float top = (float)Math.Round(s * 0.27f);
                    float bot = (float)Math.Round(s * 0.45f);
                    if (half > len + 2f) half = len + 2f;
                    if (half < 2f) half = 2f;
                    if (top < 1f) top = 1f;
                    if (bot < 2f) bot = 2f;
                    var pts = new PointF[]
                    {
                        new PointF(cx - half, cy - top),
                        new PointF(cx,        cy + bot),
                        new PointF(cx + half, cy - top)
                    };
                    var oldJoin = pen.LineJoin;
                    var oldStart = pen.StartCap;
                    var oldEnd = pen.EndCap;
                    pen.LineJoin = System.Drawing.Drawing2D.LineJoin.Miter;
                    pen.StartCap = System.Drawing.Drawing2D.LineCap.Flat;
                    pen.EndCap = System.Drawing.Drawing2D.LineCap.Flat;
                    g.DrawLines(pen, pts);
                    pen.LineJoin = oldJoin;
                    pen.StartCap = oldStart;
                    pen.EndCap = oldEnd;
                    if (DotSize > 0)
                        g.FillEllipse(brush, cx - DotSize / 2f, cy - DotSize / 2f, DotSize, DotSize);
                    break;
                }
                case 4:
                    float rOut = len + g0;
                    float rIn = Math.Max(3.5f, rOut * 0.45f);
                    using (var thinPen = new Pen(HairColor, Math.Max(1f, Thickness * 0.6f)))
                    {
                        g.DrawEllipse(thinPen, cx - rOut, cy - rOut, rOut * 2, rOut * 2);
                        g.DrawEllipse(thinPen, cx - rIn, cy - rIn, rIn * 2, rIn * 2);
                    }
                    if (DotSize > 0)
                        g.FillEllipse(brush, cx - DotSize / 2f, cy - DotSize / 2f, DotSize, DotSize);
                    break;
                case 5:
                    g.DrawLine(pen, cx - g0 - len, cy, cx - g0, cy);
                    g.DrawLine(pen, cx + g0, cy, cx + g0 + len, cy);
                    g.DrawLine(pen, cx, cy + g0, cx, cy + g0 + len);
                    if (DotSize > 0)
                        g.FillEllipse(brush, cx - DotSize / 2f, cy - DotSize / 2f, DotSize, DotSize);
                    break;
            }
        }
    }

    // 把导入的图片处理成"透明键安全"的位图：alpha>=128 视为实心，其余涂成透明色洋红。
    // 叠色窗口（TransparencyKey）只认精确的洋红，半透明像素会混出色边，所以先做二值化。
    public static Bitmap PrepareCrosshairImage(string path)
    {
        Bitmap src = null;
        Bitmap work = null;
        try
        {
            src = new Bitmap(path);
            if (src.Width > 256 || src.Height > 256)
            {
                float scale = 256f / Math.Max(src.Width, src.Height);
                int nw = Math.Max(1, (int)Math.Round(src.Width * scale));
                int nh = Math.Max(1, (int)Math.Round(src.Height * scale));
                work = new Bitmap(nw, nh, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(work))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(src, 0, 0, nw, nh);
                }
                src.Dispose();
                src = null;
            }
            else
            {
                work = src;
                src = null;
            }

            // 先看有没有透明通道；全不透明的图（JPG/不透明 PNG）就拿左上角颜色当底色扣掉
            bool hasAlpha = false;
            Color key = Color.Empty;
            Color corner = work.GetPixel(0, 0);
            for (int y = 0; y < work.Height && !hasAlpha; y++)
                for (int x = 0; x < work.Width; x++)
                    if (work.GetPixel(x, y).A < 250) { hasAlpha = true; break; }
            if (!hasAlpha) key = corner;

            var outBmp = new Bitmap(work.Width, work.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            for (int y = 0; y < work.Height; y++)
            {
                for (int x = 0; x < work.Width; x++)
                {
                    Color c = work.GetPixel(x, y);
                    bool transparent;
                    if (hasAlpha) transparent = c.A < 128;
                    else
                    {
                        int d = Math.Abs(c.R - key.R) + Math.Abs(c.G - key.G) + Math.Abs(c.B - key.B);
                        transparent = d < 40;      // 和底色足够接近就算背景
                    }
                    if (transparent) outBmp.SetPixel(x, y, Color.Magenta);
                    else outBmp.SetPixel(x, y, Color.FromArgb(255, c.R, c.G, c.B));
                }
            }
            return outBmp;
        }
        catch { return null; }
        finally
        {
            if (src != null) try { src.Dispose(); } catch { }
            if (work != null) try { work.Dispose(); } catch { }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        DrawCrosshair(e.Graphics, Width / 2f, Height / 2f);
    }
}

public class TimerChainNode
{
    public int TriggerMode = 0;   // 0=持续N秒, 1=时间区间, 2=此后一直
    public int StartSec = 90;
    public int EndSec = 0;
    public int DurationSec = 5;
    public int ColorIdx = -1;     // -1=不变, 0..6 对应 CountdownOverlay.TimerPalette
    public int FlashMode = 0;     // 0=不闪烁, 1=低频, 2=高频
    public string LabelText = "";

    public TimerChainNode Clone()
    {
        var n = new TimerChainNode();
        n.TriggerMode = TriggerMode;
        n.StartSec = StartSec;
        n.EndSec = EndSec;
        n.DurationSec = DurationSec;
        n.ColorIdx = ColorIdx;
        n.FlashMode = FlashMode;
        n.LabelText = LabelText ?? "";
        return n;
    }

    public int EffectiveStartSec()
    {
        if (TriggerMode == 1) return Math.Max(StartSec, EndSec);
        return Math.Max(0, StartSec);
    }

    public int EffectiveEndSec()
    {
        if (TriggerMode == 0)
            return Math.Max(0, StartSec - Math.Max(1, DurationSec) + 1);
        if (TriggerMode == 1)
            return Math.Max(0, Math.Min(StartSec, EndSec));
        return 0;
    }

    public bool IsActiveAt(int remSec)
    {
        if (remSec < 0) return false;
        int hi = EffectiveStartSec();
        int lo = EffectiveEndSec();
        return remSec >= lo && remSec <= hi;
    }

    public string FormatTimeRange()
    {
        if (TriggerMode == 0)
            return CountdownOverlay.FormatMmSs(StartSec) + " 起持续 " + Math.Max(1, DurationSec) + " 秒";
        if (TriggerMode == 1)
            return CountdownOverlay.FormatMmSs(EffectiveStartSec()) + " ~ " + CountdownOverlay.FormatMmSs(EffectiveEndSec());
        return CountdownOverlay.FormatMmSs(StartSec) + " 以后";
    }

    public string FormatEffects()
    {
        var parts = new List<string>();
        if (ColorIdx >= 0 && ColorIdx < CountdownOverlay.TimerColorNames.Length)
            parts.Add("变色[" + CountdownOverlay.TimerColorNames[ColorIdx] + "]");
        if (FlashMode == 1) parts.Add("低频闪烁");
        else if (FlashMode == 2) parts.Add("中频闪烁");
        else if (FlashMode == 3) parts.Add("高频闪烁");
        if (!string.IsNullOrEmpty(LabelText))
            parts.Add("标注“" + LabelText + "”");
        return parts.Count > 0 ? string.Join(" + ", parts.ToArray()) : "无动作";
    }

    public static int CompareNodes(TimerChainNode a, TimerChainNode b)
    {
        int c = b.EffectiveStartSec().CompareTo(a.EffectiveStartSec());
        if (c != 0) return c;
        return b.EffectiveEndSec().CompareTo(a.EffectiveEndSec());
    }
}

public class TimerChainPreset
{
    public string Name = "新方案";
    public int TotalSec = 1080;
    public List<TimerChainNode> Nodes = new List<TimerChainNode>();

    public TimerChainPreset Clone()
    {
        var p = new TimerChainPreset();
        p.Name = Name;
        p.TotalSec = TotalSec;
        for (int i = 0; i < Nodes.Count; i++) p.Nodes.Add(Nodes[i].Clone());
        return p;
    }

    public static TimerChainNode MakeNode(int mode, int startSec, int endSec, int durSec, int colorIdx, int flashMode, string label)
    {
        var n = new TimerChainNode();
        n.TriggerMode = mode;
        n.StartSec = startSec;
        n.EndSec = endSec;
        n.DurationSec = durSec;
        n.ColorIdx = colorIdx;
        n.FlashMode = flashMode;
        n.LabelText = label ?? "";
        return n;
    }

    public static List<TimerChainPreset> CreateDefaultPresets()
    {
        var list = new List<TimerChainPreset>();

        var pStd = new TimerChainPreset();
        pStd.Name = "标准对局";
        pStd.TotalSec = 1080;
        list.Add(pStd);

        var pAnnot = new TimerChainPreset();
        pAnnot.Name = "标准对局(带标注)";
        pAnnot.TotalSec = 1080;
        pAnnot.Nodes.Add(MakeNode(0, 1050, 0, 5, -1, 0, "17:00 事件①"));
        pAnnot.Nodes.Add(MakeNode(0, 930, 0, 5, -1, 0, "15:00 轻收开门"));
        pAnnot.Nodes.Add(MakeNode(0, 880, 0, 5, -1, 0, "14:30到13:30间 刷支援"));
        pAnnot.Nodes.Add(MakeNode(0, 690, 0, 5, -1, 0, "11:00左右 刷支援"));
        pAnnot.Nodes.Add(MakeNode(0, 690, 0, 5, -1, 0, "11:20 事件②"));
        pAnnot.Nodes.Add(MakeNode(0, 665, 0, 5, -1, 0, "轻收即将放毒倒计时"));
        pAnnot.Nodes.Add(MakeNode(1, 630, 595, 5, -1, 1, ""));
        pAnnot.Nodes.Add(MakeNode(0, 500, 0, 5, -1, 0, "7:50 事件③"));
        pAnnot.Nodes.Add(MakeNode(0, 470, 0, 5, -1, 0, "7:15左右 刷支援"));
        pAnnot.Nodes.Add(MakeNode(0, 210, 0, 5, -1, 0, "3:10 紧急疏散"));
        pAnnot.Nodes.Add(MakeNode(1, 190, 120, 5, 5, 1, ""));
        pAnnot.Nodes.Add(MakeNode(0, 150, 0, 5, -1, 0, "2:00 核弹激活倒计时"));
        pAnnot.Nodes.Add(MakeNode(1, 120, 0, 5, 5, 2, ""));
        pAnnot.Nodes.Add(MakeNode(1, 20, 0, 5, 5, 3, ""));
        list.Add(pAnnot);

        return list;
    }

    public static void EvaluateTimeline(List<TimerChainNode> nodes, int remSec, int defaultColorIdx,
        out int outColorIdx, out int outFlashMode, out string outLabel)
    {
        int defC = ((defaultColorIdx % CountdownOverlay.TimerPalette.Length) + CountdownOverlay.TimerPalette.Length) % CountdownOverlay.TimerPalette.Length;
        outColorIdx = defC;
        outFlashMode = 0;
        outLabel = "";
        if (nodes == null || nodes.Count == 0) return;

        var sorted = new List<TimerChainNode>(nodes);
        sorted.Sort(TimerChainNode.CompareNodes);
        for (int i = 0; i < sorted.Count; i++)
        {
            TimerChainNode n = sorted[i];
            if (!n.IsActiveAt(remSec)) continue;
            if (n.ColorIdx >= 0 && n.ColorIdx < CountdownOverlay.TimerPalette.Length)
                outColorIdx = n.ColorIdx;
            if (n.FlashMode > 0)
                outFlashMode = n.FlashMode;
            if (!string.IsNullOrEmpty(n.LabelText))
                outLabel = n.LabelText;
        }
    }

    static string ToB64(string s)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? ""));
    }

    static string FromB64(string b64)
    {
        if (string.IsNullOrEmpty(b64)) return "";
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(b64)); }
        catch { return ""; }
    }

    public static string SerializeNodes(List<TimerChainNode> nodes)
    {
        if (nodes == null || nodes.Count == 0) return "";
        var arr = new string[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            TimerChainNode n = nodes[i];
            arr[i] = n.TriggerMode + "," + n.StartSec + "," + n.EndSec + ","
                + n.DurationSec + "," + n.ColorIdx + "," + n.FlashMode + "," + ToB64(n.LabelText);
        }
        return string.Join(";", arr);
    }

    public static List<TimerChainNode> DeserializeNodes(string raw)
    {
        var list = new List<TimerChainNode>();
        if (string.IsNullOrEmpty(raw)) return list;
        string[] parts = raw.Split(';');
        for (int i = 0; i < parts.Length; i++)
        {
            string item = parts[i].Trim();
            if (item.Length == 0) continue;
            string[] f = item.Split(',');
            if (f.Length < 6) continue;
            int mode, st, en, dur, col, fl;
            if (!int.TryParse(f[0], out mode) || !int.TryParse(f[1], out st) || !int.TryParse(f[2], out en)
                || !int.TryParse(f[3], out dur) || !int.TryParse(f[4], out col) || !int.TryParse(f[5], out fl))
                continue;
            var n = new TimerChainNode();
            n.TriggerMode = Math.Max(0, Math.Min(2, mode));
            n.StartSec = Math.Max(0, Math.Min(10859, st));
            n.EndSec = Math.Max(0, Math.Min(10859, en));
            n.DurationSec = Math.Max(1, Math.Min(3600, dur));
            n.ColorIdx = (col >= -1 && col < CountdownOverlay.TimerPalette.Length) ? col : -1;
            n.FlashMode = Math.Max(0, Math.Min(3, fl));
            n.LabelText = f.Length >= 7 ? FromB64(f[6]) : "";
            list.Add(n);
        }
        list.Sort(TimerChainNode.CompareNodes);
        return list;
    }

    public static string SerializePresets(List<TimerChainPreset> presets)
    {
        if (presets == null || presets.Count == 0) return "";
        var arr = new string[presets.Count];
        for (int i = 0; i < presets.Count; i++)
        {
            TimerChainPreset p = presets[i];
            arr[i] = ToB64(p.Name) + ":" + p.TotalSec + ":" + SerializeNodes(p.Nodes);
        }
        return string.Join("|", arr);
    }

    public static List<TimerChainPreset> DeserializePresets(string raw)
    {
        var list = new List<TimerChainPreset>();
        if (string.IsNullOrEmpty(raw)) return list;
        string[] parts = raw.Split('|');
        for (int i = 0; i < parts.Length; i++)
        {
            string one = parts[i].Trim();
            if (one.Length == 0) continue;
            int c1 = one.IndexOf(':');
            if (c1 <= 0) continue;
            int c2 = one.IndexOf(':', c1 + 1);
            if (c2 <= c1) continue;
            string nm = FromB64(one.Substring(0, c1));
            int tot;
            if (!int.TryParse(one.Substring(c1 + 1, c2 - c1 - 1), out tot) || tot <= 0) continue;
            var p = new TimerChainPreset();
            p.Name = string.IsNullOrEmpty(nm) ? "未命名方案" : nm;
            p.TotalSec = Math.Max(1, Math.Min(10859, tot));
            p.Nodes = DeserializeNodes(one.Substring(c2 + 1));
            list.Add(p);
        }
        return list;
    }
}

public class CountdownOverlay : Form
{
    public static readonly Color[] TimerPalette = {
        Color.Lime, Color.Cyan, Color.DeepSkyBlue, Color.Yellow,
        Color.FromArgb(255, 165, 0), Color.FromArgb(255, 68, 68), Color.White
    };
    public static readonly string[] TimerColorNames = {
        "荧光绿", "青色", "天蓝", "亮黄", "橙黄", "亮红", "纯白"
    };
    public static readonly string[] AnchorNames = { "左上角", "顶部居中", "右上角", "左下角", "右下角", "屏幕居中(准星下)" };

    public int FontSize = 28;
    public int ActiveColorIndex = 0;
    public int AnchorIndex = 1;
    public int OffsetX = 0;
    public int OffsetY = 24;
    public int RemainingSeconds = 1080;
    public bool FlashDim = false;
    public string ActiveLabel = "";

    Font cachedFont;
    int cachedFontSize = -1;
    Font cachedLabelFont;
    int cachedLabelFontSize = -1;
    int cachedDigitH = 40;

    public CountdownOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        Size = new Size(160, 64);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        Visible = false;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x20;       // WS_EX_TRANSPARENT (鼠标穿透)
            cp.ExStyle |= 0x80000;    // WS_EX_LAYERED
            cp.ExStyle |= 0x80;       // WS_EX_TOOLWINDOW
            cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            return cp;
        }
    }

    public static string FormatMmSs(int totalSec)
    {
        if (totalSec < 0) totalSec = 0;
        int m = totalSec / 60;
        int s = totalSec % 60;
        return m.ToString("00") + ":" + s.ToString("00");
    }

    Font EnsureFont()
    {
        int sz = FontSize;
        if (sz < 12) sz = 12;
        if (sz > 72) sz = 72;
        if (cachedFont == null || cachedFontSize != sz)
        {
            if (cachedFont != null) { try { cachedFont.Dispose(); } catch { } }
            cachedFont = new Font("Consolas", (float)sz, FontStyle.Bold);
            cachedFontSize = sz;
        }
        return cachedFont;
    }

    Font EnsureLabelFont()
    {
        int lsz = Math.Max(11, (int)Math.Round(FontSize * 0.56));
        if (cachedLabelFont == null || cachedLabelFontSize != lsz)
        {
            if (cachedLabelFont != null) { try { cachedLabelFont.Dispose(); } catch { } }
            cachedLabelFont = new Font("Microsoft YaHei UI", (float)lsz, FontStyle.Bold);
            cachedLabelFontSize = lsz;
        }
        return cachedLabelFont;
    }

    public void ApplyBoundsAndTopmost()
    {
        try
        {
            Font f = EnsureFont();
            string curText = FormatMmSs(RemainingSeconds);
            string sample = curText.Length > 5 ? "888:88" : "88:88";
            Size szD = TextRenderer.MeasureText(sample, f, new Size(2000, 1000), TextFormatFlags.NoPadding);
            int digitW = Math.Max(80, szD.Width + 16);
            int digitH = Math.Max(34, szD.Height + 6);
            cachedDigitH = digitH;

            int w = digitW;
            int h = digitH + 8;
            if (!string.IsNullOrEmpty(ActiveLabel))
            {
                Font lf = EnsureLabelFont();
                Size szL = TextRenderer.MeasureText(ActiveLabel, lf, new Size(2000, 1000), TextFormatFlags.NoPadding);
                w = Math.Max(w, szL.Width + 20);
                h = digitH + szL.Height + 12;
            }

            if (ClientSize.Width != w || ClientSize.Height != h)
                ClientSize = new Size(w, h);

            int sw = Native.GetSystemMetrics(0);
            int sh = Native.GetSystemMetrics(1);
            int margin = 20;
            int bx, by;
            switch (AnchorIndex)
            {
                case 0: // 左上角
                    bx = margin;
                    by = margin;
                    break;
                case 2: // 右上角
                    bx = sw - w - margin;
                    by = margin;
                    break;
                case 3: // 左下角
                    bx = margin;
                    by = sh - h - margin;
                    break;
                case 4: // 右下角
                    bx = sw - w - margin;
                    by = sh - h - margin;
                    break;
                case 5: // 屏幕居中(准星下)
                    bx = (sw - w) / 2;
                    by = sh / 2 + 24;
                    break;
                default: // 1: 顶部居中
                    bx = (sw - w) / 2;
                    by = margin;
                    break;
            }
            int x = bx + OffsetX;
            int y = by + OffsetY;
            if (IsHandleCreated)
                Native.SetWindowPos(Handle, new IntPtr(-1), x, y, w, h, 0x0010);
            else
                Location = new Point(x, y);
        }
        catch { }
    }

    static void DrawOutlinedText(Graphics g, string text, Font f, Color fg, Color outline, RectangleF rc, StringFormat sf)
    {
        using (var ob = new SolidBrush(outline))
        {
            int[] dx = { -1, 1, 0, 0, -1, 1, -1, 1 };
            int[] dy = { 0, 0, -1, 1, -1, -1, 1, 1 };
            for (int i = 0; i < dx.Length; i++)
                g.DrawString(text, f, ob, new RectangleF(rc.X + dx[i], rc.Y + dy[i], rc.Width, rc.Height), sf);
        }
        using (var fb = new SolidBrush(fg))
        {
            g.DrawString(text, f, fb, rc, sf);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Color.Magenta);
        // SingleBitPerPixelGridFit 避免边缘抗锯齿像素与洋红背景混合产生紫边
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;

        string text = FormatMmSs(RemainingSeconds);
        Font f = EnsureFont();
        int cidx = ((ActiveColorIndex % TimerPalette.Length) + TimerPalette.Length) % TimerPalette.Length;
        Color baseFg = TimerPalette[cidx];
        Color digitFg = FlashDim
            ? Color.FromArgb(Math.Max(26, baseFg.R / 4), Math.Max(26, baseFg.G / 4), Math.Max(26, baseFg.B / 4))
            : baseFg;
        Color outline = Color.FromArgb(18, 22, 28);

        using (var sf = new StringFormat())
        {
            sf.Alignment = StringAlignment.Center;
            sf.LineAlignment = StringAlignment.Center;
            sf.FormatFlags = StringFormatFlags.NoWrap;

            if (string.IsNullOrEmpty(ActiveLabel))
            {
                DrawOutlinedText(g, text, f, digitFg, outline, new RectangleF(0, 0, Width, Height), sf);
            }
            else
            {
                RectangleF rcDigit = new RectangleF(0, 2, Width, cachedDigitH);
                DrawOutlinedText(g, text, f, digitFg, outline, rcDigit, sf);

                Font lf = EnsureLabelFont();
                float ly = 2 + cachedDigitH;
                RectangleF rcLabel = new RectangleF(0, ly, Width, Math.Max(16, Height - ly - 2));
                DrawOutlinedText(g, ActiveLabel, lf, baseFg, outline, rcLabel, sf);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (cachedFont != null) { try { cachedFont.Dispose(); } catch { } cachedFont = null; }
            if (cachedLabelFont != null) { try { cachedLabelFont.Dispose(); } catch { } cachedLabelFont = null; }
        }
        base.Dispose(disposing);
    }
}
