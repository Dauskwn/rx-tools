using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

public class Overlay : Form
{
    public static readonly Color[] Palette = { Color.Lime, Color.Cyan, Color.DeepSkyBlue, Color.Yellow, Color.OrangeRed, Color.White };
    public static readonly string[] ColorNames = { "荧光绿", "青色", "天蓝", "亮黄", "橙红", "纯白" };
    public static readonly string[] Styles = { "十字(带缺口)", "完整十字", "圆点", "V 形", "同心圆", "T 形" };

    public Color HairColor = Color.Lime;
    public int Thickness = 2, Length = 9, Gap = 5, DotSize = 2;
    public int OutlineWidth = 1;    // 0..2px 黑色外描边（亮/暗背景全清晰）
    public int OpacityPct = 100;    // 20..100% 不透明度
    public int StyleIndex = 0;
    public const int OffsetMin = -20;
    public const int OffsetMax = 20;
    public int OffsetX = 0, OffsetY = 0;
    // 自定义准星图片（导入 PNG/BMP，保留完整 32-bit 透明通道）；路径为空表示用内置矢量样式
    public Bitmap CustomImage;
    public string CustomImagePath = "";

    public static int ClampOffset(int v)
    {
        return Math.Max(OffsetMin, Math.Min(OffsetMax, v));
    }

    int lastLeft = 0, lastTop = 0;

    public Overlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(240, 240);
        Visible = false;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x20;       // WS_EX_TRANSPARENT（鼠标穿透）
            cp.ExStyle |= 0x80000;    // WS_EX_LAYERED（32-bit 每像素 Alpha）
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
            RedrawLayered();
        }
    }

    public void CycleColor()
    {
        ColorIndex = ColorIndex + 1;
    }

    public void CycleStyle()
    {
        StyleIndex = (StyleIndex + 1) % Styles.Length;
        RedrawLayered();
    }

    public new void Invalidate()
    {
        RedrawLayered();
    }

    public void CenterAndRedraw()
    {
        Rectangle scr = Native.GetTargetScreenBounds();
        lastLeft = scr.X + (scr.Width - Width) / 2 + ClampOffset(OffsetX);
        lastTop = scr.Y + (scr.Height - Height) / 2 + ClampOffset(OffsetY);
        RedrawLayered();
        if (IsHandleCreated) Native.MakeTopmost(Handle);
    }

    public void RedrawLayered()
    {
        if (IsDisposed || !IsHandleCreated || !Visible) return;
        try
        {
            Rectangle scr = Native.GetTargetScreenBounds();
            lastLeft = scr.X + (scr.Width - Width) / 2 + ClampOffset(OffsetX);
            lastTop = scr.Y + (scr.Height - Height) / 2 + ClampOffset(OffsetY);
            int w = Math.Max(32, Width);
            int h = Math.Max(32, Height);
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.FromArgb(0, 0, 0, 0));
                    DrawCrosshair(g, w / 2f, h / 2f);
                }
                int pct = Math.Max(20, Math.Min(100, OpacityPct));
                byte alpha = (byte)Math.Round(pct * 255.0 / 100.0);
                Native.PushLayeredBitmap(Handle, bmp, lastLeft, lastTop, alpha);
            }
        }
        catch { }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            if (!IsHandleCreated) CreateHandle();
            CenterAndRedraw();
        }
    }

    public void DrawCrosshair(Graphics g, float cx, float cy)
    {
        float g0 = Gap, len = Length;

        if (CustomImage != null)
        {
            float side = Math.Max(8f, Math.Min(232f, (Length + Gap) * 2f));
            float sc = side / Math.Max(1, Math.Max(CustomImage.Width, CustomImage.Height));
            float iw = Math.Max(1f, CustomImage.Width * sc);
            float ih = Math.Max(1f, CustomImage.Height * sc);
            var oldInterp = g.InterpolationMode;
            var oldOffset = g.PixelOffsetMode;
            g.InterpolationMode = (sc >= 1.5f) ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            int dx = (int)Math.Round(cx - iw / 2f);
            int dy = (int)Math.Round(cy - ih / 2f);
            int dw = Math.Max(1, (int)Math.Round(iw));
            int dh = Math.Max(1, (int)Math.Round(ih));
            g.DrawImage(CustomImage, new Rectangle(dx, dy, dw, dh), 0, 0, CustomImage.Width, CustomImage.Height, GraphicsUnit.Pixel);
            g.InterpolationMode = oldInterp;
            g.PixelOffsetMode = oldOffset;
            return;
        }

        float drawCx = cx, drawCy = cy;
        if (Thickness % 2 == 1) { drawCx += 0.5f; drawCy += 0.5f; }

        int ow = Math.Max(0, Math.Min(2, OutlineWidth));
        if (ow > 0)
        {
            DrawVectorPass(g, drawCx, drawCy, g0, len, Color.FromArgb(205, 10, 14, 20), Thickness + ow * 2, ow);
        }
        DrawVectorPass(g, drawCx, drawCy, g0, len, HairColor, Thickness, 0);
    }

    void DrawVectorPass(Graphics g, float cx, float cy, float g0, float len, Color col, float penW, float pad)
    {
        var oldSm = g.SmoothingMode;
        bool useAA = (StyleIndex == 2 || StyleIndex == 3 || StyleIndex == 4);
        g.SmoothingMode = useAA ? SmoothingMode.AntiAlias : SmoothingMode.None;

        using (var pen = new Pen(col, penW))
        using (var brush = new SolidBrush(col))
        {
            switch (StyleIndex)
            {
                case 0:
                    g.DrawLine(pen, cx - g0 - len - pad, cy, cx - g0 + pad, cy);
                    g.DrawLine(pen, cx + g0 - pad, cy, cx + g0 + len + pad, cy);
                    g.DrawLine(pen, cx, cy - g0 - len - pad, cx, cy - g0 + pad);
                    g.DrawLine(pen, cx, cy + g0 - pad, cx, cy + g0 + len + pad);
                    if (DotSize > 0)
                    {
                        float d = DotSize + pad * 2f;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.FillEllipse(brush, cx - d / 2f, cy - d / 2f, d, d);
                    }
                    break;
                case 1:
                    g.DrawLine(pen, cx - g0 - len - pad, cy, cx + g0 + len + pad, cy);
                    g.DrawLine(pen, cx, cy - g0 - len - pad, cx, cy + g0 + len + pad);
                    break;
                case 2:
                {
                    float r = Math.Max(2.5f, (DotSize > 0 ? DotSize : Thickness) * 1.6f) + pad;
                    g.FillEllipse(brush, cx - r, cy - r, r * 2f, r * 2f);
                    break;
                }
                case 3:
                {
                    float s = len + g0;
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
                    pen.LineJoin = LineJoin.Miter;
                    pen.StartCap = pad > 0 ? LineCap.Square : LineCap.Flat;
                    pen.EndCap = pad > 0 ? LineCap.Square : LineCap.Flat;
                    g.DrawLines(pen, pts);
                    if (DotSize > 0)
                    {
                        float d = DotSize + pad * 2f;
                        g.FillEllipse(brush, cx - d / 2f, cy - d / 2f, d, d);
                    }
                    break;
                }
                case 4:
                {
                    float rOut = len + g0;
                    float rIn = Math.Max(3.5f, rOut * 0.45f);
                    float tw = Math.Max(1f, Thickness * 0.6f) + pad * 2f;
                    using (var thinPen = new Pen(col, tw))
                    {
                        g.DrawEllipse(thinPen, cx - rOut, cy - rOut, rOut * 2f, rOut * 2f);
                        g.DrawEllipse(thinPen, cx - rIn, cy - rIn, rIn * 2f, rIn * 2f);
                    }
                    if (DotSize > 0)
                    {
                        float d = DotSize + pad * 2f;
                        g.FillEllipse(brush, cx - d / 2f, cy - d / 2f, d, d);
                    }
                    break;
                }
                case 5:
                    g.DrawLine(pen, cx - g0 - len - pad, cy, cx - g0 + pad, cy);
                    g.DrawLine(pen, cx + g0 - pad, cy, cx + g0 + len + pad, cy);
                    g.DrawLine(pen, cx, cy + g0 - pad, cx, cy + g0 + len + pad);
                    if (DotSize > 0)
                    {
                        float d = DotSize + pad * 2f;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.FillEllipse(brush, cx - d / 2f, cy - d / 2f, d, d);
                    }
                    break;
            }
        }
        g.SmoothingMode = oldSm;
    }

    /// <summary>
    /// 准备 32-bit 每像素透明准星位图：
    /// - 带透明通道的 PNG：完整保留原有 8-bit Alpha 渐变与柔和光晕；
    /// - 无透明通道的 JPG/BMP：LockBits 极速将接近左上角底色的像素柔化扣透明。
    /// </summary>
    public static Bitmap PrepareCrosshairImage(string path)
    {
        Bitmap src = null;
        Bitmap work = null;
        try
        {
            src = new Bitmap(path);
            int nw = src.Width, nh = src.Height;
            if (nw > 256 || nh > 256)
            {
                float scale = 256f / Math.Max(nw, nh);
                nw = Math.Max(1, (int)Math.Round(nw * scale));
                nh = Math.Max(1, (int)Math.Round(nh * scale));
            }
            work = new Bitmap(nw, nh, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(work))
            {
                g.Clear(Color.FromArgb(0, 0, 0, 0));
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, nw, nh);
            }
            src.Dispose();
            src = null;

            var rect = new Rectangle(0, 0, nw, nh);
            BitmapData bd = work.LockBits(rect, ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
            try
            {
                int byteCount = Math.Abs(bd.Stride) * nh;
                byte[] buf = new byte[byteCount];
                Marshal.Copy(bd.Scan0, buf, 0, byteCount);

                bool hasAlpha = false;
                for (int y = 0; y < nh && !hasAlpha; y++)
                {
                    int row = y * bd.Stride;
                    for (int x = 0; x < nw; x++)
                    {
                        if (buf[row + x * 4 + 3] < 250) { hasAlpha = true; break; }
                    }
                }

                if (!hasAlpha && byteCount >= 4)
                {
                    byte kb = buf[0], kg = buf[1], kr = buf[2];
                    for (int y = 0; y < nh; y++)
                    {
                        int row = y * bd.Stride;
                        for (int x = 0; x < nw; x++)
                        {
                            int idx = row + x * 4;
                            int d = Math.Abs(buf[idx] - kb) + Math.Abs(buf[idx + 1] - kg) + Math.Abs(buf[idx + 2] - kr);
                            if (d < 36) buf[idx + 3] = 0;
                            else if (d < 64) buf[idx + 3] = (byte)((d - 36) * 255 / 28);
                        }
                    }
                    Marshal.Copy(buf, 0, bd.Scan0, byteCount);
                }
            }
            finally
            {
                work.UnlockBits(bd);
            }

            Bitmap ret = work;
            work = null;
            return ret;
        }
        catch { return null; }
        finally
        {
            if (src != null) try { src.Dispose(); } catch { }
            if (work != null) try { work.Dispose(); } catch { }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (CustomImage != null)
            {
                try { CustomImage.Dispose(); } catch { }
                CustomImage = null;
            }
        }
        base.Dispose(disposing);
    }
}

public class TimerChainNode
{
    public int TriggerMode = 0;   // 0=持续N秒, 1=时间区间, 2=此后一直
    public int StartSec = 90;
    public int EndSec = 0;
    public int DurationSec = 5;
    public int ColorIdx = -1;     // -1=不变, 0..6 对应 CountdownOverlay.TimerPalette
    public int FlashMode = 0;     // 0=不闪烁, 1=低频, 2=中频, 3=高频
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

    /// <summary>查找剩余时间小于 remSec、即将触发的下一条带文字标注的事件。</summary>
    public static TimerChainNode FindNextUpcomingNode(List<TimerChainNode> nodes, int remSec)
    {
        if (nodes == null || nodes.Count == 0) return null;
        TimerChainNode best = null;
        int bestStart = -1;
        for (int i = 0; i < nodes.Count; i++)
        {
            TimerChainNode n = nodes[i];
            if (n == null || string.IsNullOrEmpty(n.LabelText)) continue;
            int st = n.EffectiveStartSec();
            if (st < remSec && st > bestStart)
            {
                bestStart = st;
                best = n;
            }
        }
        return best;
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
    public int TotalSeconds = 1080;
    public int RemainingSeconds = 1080;
    public bool FlashDim = false;
    public float PulseAlpha = 1.0f;       // 0.28..1.0 平滑正弦脉冲
    public bool UseCardStyle = false;     // true=半透明深色圆角胶囊+进度条，false=高清纯数字
    public string ActiveLabel = "";

    Font cachedFont;
    int cachedFontSize = -1;
    Font cachedLabelFont;
    int cachedLabelFontSize = -1;
    int cachedDigitH = 40;
    int lastW = 160, lastH = 64, lastX = 0, lastY = 24;

    public CountdownOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(160, 64);
        Visible = false;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x20;       // WS_EX_TRANSPARENT (鼠标穿透)
            cp.ExStyle |= 0x80000;    // WS_EX_LAYERED (32bpp 每像素 Alpha)
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
        int sz = Math.Max(12, Math.Min(72, FontSize));
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
        int lsz = Math.Max(11, (int)Math.Round(FontSize * 0.52));
        if (cachedLabelFont == null || cachedLabelFontSize != lsz)
        {
            if (cachedLabelFont != null) { try { cachedLabelFont.Dispose(); } catch { } }
            cachedLabelFont = new Font("Microsoft YaHei UI", (float)lsz, FontStyle.Bold);
            cachedLabelFontSize = lsz;
        }
        return cachedLabelFont;
    }

    public new void Invalidate()
    {
        RedrawLayered();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            if (!IsHandleCreated) CreateHandle();
            ApplyBoundsAndTopmost();
            RedrawLayered();
        }
    }

    void ComputeGeometry()
    {
        Font f = EnsureFont();
        string curText = FormatMmSs(RemainingSeconds);
        string sample = curText.Length > 5 ? "888:88" : "88:88";
        Size szD = TextRenderer.MeasureText(sample, f, new Size(2000, 1000), TextFormatFlags.NoPadding);
        int padX = UseCardStyle ? 20 : 12;
        int padTop = UseCardStyle ? 8 : 4;
        int padBot = UseCardStyle ? 12 : 6;
        int digitW = Math.Max(92, szD.Width + padX * 2);
        int digitH = Math.Max(34, szD.Height + 4);
        cachedDigitH = digitH;

        int w = digitW;
        int h = padTop + digitH + padBot;
        if (!string.IsNullOrEmpty(ActiveLabel))
        {
            Font lf = EnsureLabelFont();
            Size szL = TextRenderer.MeasureText(ActiveLabel, lf, new Size(2000, 1000), TextFormatFlags.NoPadding);
            w = Math.Max(w, szL.Width + padX * 2 + 8);
            h = padTop + digitH + szL.Height + padBot + 4;
        }

        lastW = w;
        lastH = h;

        Rectangle scr = Native.GetTargetScreenBounds();
        int sw = scr.Width;
        int sh = scr.Height;
        int margin = 20;
        int bx, by;
        switch (AnchorIndex)
        {
            case 0: bx = scr.X + margin; by = scr.Y + margin; break;
            case 2: bx = scr.X + sw - w - margin; by = scr.Y + margin; break;
            case 3: bx = scr.X + margin; by = scr.Y + sh - h - margin; break;
            case 4: bx = scr.X + sw - w - margin; by = scr.Y + sh - h - margin; break;
            case 5: bx = scr.X + (sw - w) / 2; by = scr.Y + sh / 2 + 24; break;
            default: bx = scr.X + (sw - w) / 2; by = scr.Y + margin; break;
        }
        lastX = bx + OffsetX;
        lastY = by + OffsetY;
    }

    public void ApplyBoundsAndTopmost()
    {
        try
        {
            ComputeGeometry();
            if (IsHandleCreated)
            {
                if (Visible) RedrawLayered();
                Native.MakeTopmost(Handle);
            }
            else
            {
                Size = new Size(lastW, lastH);
                Location = new Point(lastX, lastY);
            }
        }
        catch { }
    }

    static void DrawSmoothOutlinedText(Graphics g, string text, Font f, Color fg, Color outline, RectangleF rc, StringFormat sf)
    {
        using (var ob = new SolidBrush(outline))
        {
            int[] dx = { -1, 1, 0, 0, -1, 1, -1, 1 };
            int[] dy = { 0, 0, -1, 1, -1, -1, 1, 1 };
            for (int i = 0; i < dx.Length; i++)
                g.DrawString(text, f, ob, new RectangleF(rc.X + dx[i], rc.Y + dy[i], rc.Width, rc.Height), sf);
        }
        using (var fb = new SolidBrush(fg))
            g.DrawString(text, f, fb, rc, sf);
    }

    public void RedrawLayered()
    {
        if (IsDisposed || !IsHandleCreated || !Visible) return;
        try
        {
            ComputeGeometry();
            int w = Math.Max(64, lastW);
            int h = Math.Max(32, lastH);
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.FromArgb(0, 0, 0, 0));
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                    int cidx = ((ActiveColorIndex % TimerPalette.Length) + TimerPalette.Length) % TimerPalette.Length;
                    Color baseFg = TimerPalette[cidx];
                    float mul = Math.Max(0.25f, Math.Min(1.0f, PulseAlpha));
                    if (FlashDim && mul > 0.99f) mul = 0.35f;
                    int dAlpha = (int)Math.Round(255f * mul);
                    Color digitFg = Color.FromArgb(dAlpha, baseFg.R, baseFg.G, baseFg.B);

                    if (UseCardStyle)
                    {
                        var cardRect = new Rectangle(1, 1, w - 3, h - 3);
                        using (var bg = new SolidBrush(Color.FromArgb(182, 13, 18, 28)))
                            Native.FillRoundedRect(g, bg, cardRect, 11);
                        using (var border = new Pen(Color.FromArgb(108, baseFg.R, baseFg.G, baseFg.B), 1.2f))
                            Native.DrawRoundedRect(g, border, cardRect, 11);

                        // 底部极细实时进度条
                        int barPad = 12;
                        int barW = Math.Max(12, w - barPad * 2);
                        int barY = h - 7;
                        using (var track = new SolidBrush(Color.FromArgb(55, 255, 255, 255)))
                            Native.FillRoundedRect(g, track, new Rectangle(barPad, barY, barW, 3), 2);
                        if (TotalSeconds > 0)
                        {
                            float frac = Math.Max(0f, Math.Min(1f, (float)RemainingSeconds / TotalSeconds));
                            int fillW = (int)Math.Round(barW * frac);
                            if (fillW > 2)
                            {
                                using (var fill = new SolidBrush(Color.FromArgb(225, baseFg.R, baseFg.G, baseFg.B)))
                                    Native.FillRoundedRect(g, fill, new Rectangle(barPad, barY, fillW, 3), 2);
                            }
                        }
                    }

                    string text = FormatMmSs(RemainingSeconds);
                    Font f = EnsureFont();
                    Color outline = UseCardStyle ? Color.FromArgb(160, 0, 0, 0) : Color.FromArgb(220, 12, 16, 22);
                    int padTop = UseCardStyle ? 6 : 2;

                    using (var sf = new StringFormat())
                    {
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;
                        sf.FormatFlags = StringFormatFlags.NoWrap;

                        if (!string.IsNullOrEmpty(ActiveLabel))
                        {
                            DrawSmoothOutlinedText(g, text, f, digitFg, outline, new RectangleF(0, padTop, w, cachedDigitH), sf);
                            Font lf = EnsureLabelFont();
                            float ly = padTop + cachedDigitH;
                            DrawSmoothOutlinedText(g, ActiveLabel, lf, baseFg, outline, new RectangleF(4, ly, w - 8, Math.Max(16, h - ly - (UseCardStyle ? 10 : 2))), sf);
                        }
                        else
                        {
                            DrawSmoothOutlinedText(g, text, f, digitFg, outline, new RectangleF(0, padTop, w, h - padTop - (UseCardStyle ? 8 : 2)), sf);
                        }
                    }
                }
                Native.PushLayeredBitmap(Handle, bmp, lastX, lastY, 255);
            }
        }
        catch { }
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

public class CrosshairPreset
{
    public string Name = "新方案";
    public int StyleIndex = 0;
    public int ColorIndex = 0;
    public int Thickness = 2;
    public int Length = 9;
    public int Gap = 5;
    public int DotSize = 2;
    public int OutlineWidth = 1;
    public int OpacityPct = 100;
    public int OffsetX = 0;
    public int OffsetY = 0;
    public string CustomImagePath = "";

    public CrosshairPreset Clone()
    {
        var p = new CrosshairPreset();
        p.Name = Name;
        p.StyleIndex = StyleIndex;
        p.ColorIndex = ColorIndex;
        p.Thickness = Thickness;
        p.Length = Length;
        p.Gap = Gap;
        p.DotSize = DotSize;
        p.OutlineWidth = OutlineWidth;
        p.OpacityPct = OpacityPct;
        p.OffsetX = OffsetX;
        p.OffsetY = OffsetY;
        p.CustomImagePath = CustomImagePath ?? "";
        return p;
    }

    public void ClampFields()
    {
        Name = string.IsNullOrEmpty(Name) ? "新方案" : Name.Trim();
        if (Name.Length == 0) Name = "新方案";
        StyleIndex = Math.Max(0, Math.Min(Overlay.Styles.Length - 1, StyleIndex));
        ColorIndex = Math.Max(0, Math.Min(Overlay.Palette.Length - 1, ColorIndex));
        Thickness = Math.Max(1, Math.Min(6, Thickness));
        Length = Math.Max(3, Math.Min(40, Length));
        Gap = Math.Max(0, Math.Min(30, Gap));
        DotSize = Math.Max(0, Math.Min(8, DotSize));
        OutlineWidth = Math.Max(0, Math.Min(2, OutlineWidth));
        OpacityPct = Math.Max(20, Math.Min(100, OpacityPct));
        OffsetX = Overlay.ClampOffset(OffsetX);
        OffsetY = Overlay.ClampOffset(OffsetY);
        if (CustomImagePath == null) CustomImagePath = "";
    }

    public static CrosshairPreset FromOverlay(string name, Overlay o)
    {
        var p = new CrosshairPreset();
        p.Name = string.IsNullOrEmpty(name) ? "新方案" : name.Trim();
        if (o != null)
        {
            p.StyleIndex = o.StyleIndex;
            p.ColorIndex = o.ColorIndex;
            p.Thickness = o.Thickness;
            p.Length = o.Length;
            p.Gap = o.Gap;
            p.DotSize = o.DotSize;
            p.OutlineWidth = o.OutlineWidth;
            p.OpacityPct = o.OpacityPct;
            p.OffsetX = o.OffsetX;
            p.OffsetY = o.OffsetY;
            p.CustomImagePath = o.CustomImagePath ?? "";
        }
        p.ClampFields();
        return p;
    }

    public void ApplyScalarToOverlay(Overlay o)
    {
        if (o == null) return;
        ClampFields();
        o.StyleIndex = StyleIndex;
        o.ColorIndex = ColorIndex;
        o.Thickness = Thickness;
        o.Length = Length;
        o.Gap = Gap;
        o.DotSize = DotSize;
        o.OutlineWidth = OutlineWidth;
        o.OpacityPct = OpacityPct;
        o.OffsetX = OffsetX;
        o.OffsetY = OffsetY;
    }

    public bool MatchesOverlay(Overlay o)
    {
        if (o == null) return false;
        ClampFields();
        return StyleIndex == o.StyleIndex
            && ColorIndex == o.ColorIndex
            && Thickness == o.Thickness
            && Length == o.Length
            && Gap == o.Gap
            && DotSize == o.DotSize
            && OutlineWidth == o.OutlineWidth
            && OpacityPct == o.OpacityPct
            && OffsetX == Overlay.ClampOffset(o.OffsetX)
            && OffsetY == Overlay.ClampOffset(o.OffsetY)
            && string.Equals(CustomImagePath ?? "", o.CustomImagePath ?? "", StringComparison.OrdinalIgnoreCase);
    }

    public string DescribeSummary()
    {
        ClampFields();
        string baseDesc = string.IsNullOrEmpty(CustomImagePath)
            ? (Overlay.Styles[StyleIndex] + " · " + Overlay.ColorNames[ColorIndex] + " · " + OpacityPct + "%")
            : ("自定义图片(" + Path.GetFileName(CustomImagePath) + ") · " + OpacityPct + "%");
        if (OffsetX != 0 || OffsetY != 0)
            baseDesc += " · 偏移(" + (OffsetX >= 0 ? "+" : "") + OffsetX + "," + (OffsetY >= 0 ? "+" : "") + OffsetY + ")";
        return Name + "：" + baseDesc;
    }

    public static List<CrosshairPreset> CreateDefaultPresets()
    {
        var list = new List<CrosshairPreset>();
        list.Add(new CrosshairPreset
        {
            Name = "步枪十字", StyleIndex = 0, ColorIndex = 0, Thickness = 2, Length = 9, Gap = 5, DotSize = 2, OutlineWidth = 1, OpacityPct = 100
        });
        list.Add(new CrosshairPreset
        {
            Name = "精准圆点", StyleIndex = 2, ColorIndex = 1, Thickness = 2, Length = 6, Gap = 3, DotSize = 2, OutlineWidth = 1, OpacityPct = 100
        });
        list.Add(new CrosshairPreset
        {
            Name = "散弹同心", StyleIndex = 4, ColorIndex = 4, Thickness = 2, Length = 12, Gap = 6, DotSize = 2, OutlineWidth = 1, OpacityPct = 90
        });
        list.Add(new CrosshairPreset
        {
            Name = "经典白十", StyleIndex = 1, ColorIndex = 5, Thickness = 1, Length = 10, Gap = 0, DotSize = 0, OutlineWidth = 1, OpacityPct = 95
        });
        return list;
    }

    public static string SerializePresets(List<CrosshairPreset> list)
    {
        if (list == null || list.Count == 0) return "";
        var sb = new StringBuilder();
        int cap = Math.Min(6, list.Count);
        for (int i = 0; i < cap; i++)
        {
            CrosshairPreset p = list[i];
            if (p == null) continue;
            p.ClampFields();
            if (sb.Length > 0) sb.Append(';');
            sb.Append(Uri.EscapeDataString(p.Name)).Append(',')
              .Append(p.StyleIndex).Append(',')
              .Append(p.ColorIndex).Append(',')
              .Append(p.Thickness).Append(',')
              .Append(p.Length).Append(',')
              .Append(p.Gap).Append(',')
              .Append(p.DotSize).Append(',')
              .Append(p.OutlineWidth).Append(',')
              .Append(p.OpacityPct).Append(',')
              .Append(p.OffsetX).Append(',')
              .Append(p.OffsetY).Append(',')
              .Append(Uri.EscapeDataString(p.CustomImagePath ?? ""));
        }
        return sb.ToString();
    }

    public static List<CrosshairPreset> DeserializePresets(string raw)
    {
        var list = new List<CrosshairPreset>();
        if (!string.IsNullOrEmpty(raw))
        {
            string[] items = raw.Split(';');
            for (int i = 0; i < items.Length && list.Count < 6; i++)
            {
                string one = (items[i] ?? "").Trim();
                if (one.Length == 0) continue;
                string[] parts = one.Split(',');
                if (parts.Length < 11) continue;
                try
                {
                    int st, col, th, ln, gp, dt, ow, op, ox, oy;
                    if (!int.TryParse(parts[1], out st) || !int.TryParse(parts[2], out col)
                        || !int.TryParse(parts[3], out th) || !int.TryParse(parts[4], out ln)
                        || !int.TryParse(parts[5], out gp) || !int.TryParse(parts[6], out dt)
                        || !int.TryParse(parts[7], out ow) || !int.TryParse(parts[8], out op)
                        || !int.TryParse(parts[9], out ox) || !int.TryParse(parts[10], out oy))
                        continue;

                    var p = new CrosshairPreset();
                    p.Name = Uri.UnescapeDataString(parts[0]);
                    p.StyleIndex = st;
                    p.ColorIndex = col;
                    p.Thickness = th;
                    p.Length = ln;
                    p.Gap = gp;
                    p.DotSize = dt;
                    p.OutlineWidth = ow;
                    p.OpacityPct = op;
                    p.OffsetX = ox;
                    p.OffsetY = oy;
                    p.CustomImagePath = parts.Length >= 12 ? Uri.UnescapeDataString(parts[11]) : "";
                    p.ClampFields();
                    list.Add(p);
                }
                catch { }
            }
        }
        if (list.Count == 0) return CreateDefaultPresets();
        return list;
    }

    public static void DrawMiniIcon(Graphics g, CrosshairPreset p, Rectangle box)
    {
        if (g == null || p == null || box.Width < 8 || box.Height < 8) return;
        SmoothingMode oldSm = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var wellBg = new SolidBrush(Color.FromArgb(20, 26, 36)))
            Native.FillRoundedRect(g, wellBg, box, 4);
        using (var wellEdge = new Pen(Color.FromArgb(52, 62, 76), 1f))
            Native.DrawRoundedRect(g, wellEdge, new Rectangle(box.X, box.Y, box.Width - 1, box.Height - 1), 4);

        float cx = box.X + box.Width / 2f;
        float cy = box.Y + box.Height / 2f;
        float r = Math.Min(box.Width, box.Height) * 0.30f;
        int cidx = Math.Max(0, Math.Min(Overlay.Palette.Length - 1, p.ColorIndex));
        Color col = Overlay.Palette[cidx];

        if (!string.IsNullOrEmpty(p.CustomImagePath))
        {
            using (var pen = new Pen(Color.FromArgb(125, 211, 252), 1.4f))
            {
                g.DrawEllipse(pen, cx - r * 0.85f, cy - r * 0.85f, r * 1.7f, r * 1.7f);
                g.DrawLine(pen, cx - r * 1.1f, cy, cx - r * 0.45f, cy);
                g.DrawLine(pen, cx + r * 0.45f, cy, cx + r * 1.1f, cy);
                g.DrawLine(pen, cx, cy - r * 1.1f, cx, cy - r * 0.45f);
                g.DrawLine(pen, cx, cy + r * 0.45f, cx, cy + r * 1.1f);
            }
            using (var badge = new SolidBrush(Color.FromArgb(56, 189, 248)))
                g.FillEllipse(badge, box.Right - 6, box.Y + 2, 4, 4);
            g.SmoothingMode = oldSm;
            return;
        }

        using (var pen = new Pen(col, 1.5f))
        using (var brush = new SolidBrush(col))
        {
            switch (p.StyleIndex)
            {
                case 0:
                    g.DrawLine(pen, cx - r, cy, cx - r * 0.38f, cy);
                    g.DrawLine(pen, cx + r * 0.38f, cy, cx + r, cy);
                    g.DrawLine(pen, cx, cy - r, cx, cy - r * 0.38f);
                    g.DrawLine(pen, cx, cy + r * 0.38f, cx, cy + r);
                    if (p.DotSize > 0) g.FillEllipse(brush, cx - 1.3f, cy - 1.3f, 2.6f, 2.6f);
                    break;
                case 1:
                    g.DrawLine(pen, cx - r, cy, cx + r, cy);
                    g.DrawLine(pen, cx, cy - r, cx, cy + r);
                    break;
                case 2:
                    g.FillEllipse(brush, cx - r * 0.42f, cy - r * 0.42f, r * 0.84f, r * 0.84f);
                    break;
                case 3:
                {
                    var pts = new PointF[]
                    {
                        new PointF(cx - r * 0.85f, cy - r * 0.45f),
                        new PointF(cx, cy + r * 0.55f),
                        new PointF(cx + r * 0.85f, cy - r * 0.45f)
                    };
                    g.DrawLines(pen, pts);
                    break;
                }
                case 4:
                    g.DrawEllipse(pen, cx - r * 0.9f, cy - r * 0.9f, r * 1.8f, r * 1.8f);
                    g.DrawEllipse(pen, cx - r * 0.42f, cy - r * 0.42f, r * 0.84f, r * 0.84f);
                    break;
                default:
                    g.DrawLine(pen, cx - r, cy, cx - r * 0.35f, cy);
                    g.DrawLine(pen, cx + r * 0.35f, cy, cx + r, cy);
                    g.DrawLine(pen, cx, cy + r * 0.35f, cx, cy + r);
                    if (p.DotSize > 0) g.FillEllipse(brush, cx - 1.2f, cy - 1.2f, 2.4f, 2.4f);
                    break;
            }
        }
        g.SmoothingMode = oldSm;
    }
}
