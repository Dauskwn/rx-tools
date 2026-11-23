using System;
using System.Drawing;
using System.Drawing.Drawing2D;

/// <summary>
/// 统一语义设计令牌（Design Tokens）：护眼浅色 / 夜间深色单一真相源。
/// 所有自绘现代控件共享此色板，杜绝散落硬编码与 RGB 启发式猜色。
/// </summary>
public static class ThemePalette
{
    public static bool Dark { get { return MainForm.IsDark; } }

    // 表面层阶（Surface Elevation）
    public static Color Canvas      { get { return Dark ? Color.FromArgb(13, 17, 23)  : Color.FromArgb(244, 246, 250); } }
    public static Color Rail        { get { return Dark ? Color.FromArgb(17, 22, 30)  : Color.FromArgb(237, 241, 248); } }
    public static Color Surface     { get { return Dark ? Color.FromArgb(22, 27, 34)  : Color.FromArgb(255, 255, 255); } }
    public static Color SurfaceAlt  { get { return Dark ? Color.FromArgb(28, 33, 41)  : Color.FromArgb(248, 250, 253); } }
    public static Color Elevated    { get { return Dark ? Color.FromArgb(33, 40, 50)  : Color.FromArgb(255, 255, 255); } }
    public static Color InputBg     { get { return Dark ? Color.FromArgb(13, 17, 23)  : Color.FromArgb(255, 255, 255); } }
    public static Color Hover       { get { return Dark ? Color.FromArgb(34, 42, 54)  : Color.FromArgb(228, 236, 247); } }
    public static Color Selected    { get { return Dark ? Color.FromArgb(26, 44, 72)  : Color.FromArgb(224, 237, 255); } }

    // 边框阶梯
    public static Color BorderSubtle { get { return Dark ? Color.FromArgb(36, 43, 53)  : Color.FromArgb(226, 232, 240); } }
    public static Color Border       { get { return Dark ? Color.FromArgb(52, 60, 72)  : Color.FromArgb(203, 213, 225); } }
    public static Color BorderFocus  { get { return Dark ? Color.FromArgb(88, 166, 255): Color.FromArgb(37, 99, 235); } }

    // 文字阶梯
    public static Color TextPrimary   { get { return Dark ? Color.FromArgb(234, 240, 247) : Color.FromArgb(15, 23, 42); } }
    public static Color TextSecondary { get { return Dark ? Color.FromArgb(166, 177, 191) : Color.FromArgb(71, 85, 105); } }
    public static Color TextMuted     { get { return Dark ? Color.FromArgb(118, 130, 145) : Color.FromArgb(124, 138, 156); } }

    // 品牌与语义色
    public static Color Accent       { get { return Dark ? Color.FromArgb(88, 166, 255) : Color.FromArgb(37, 99, 235); } }
    public static Color AccentHover  { get { return Dark ? Color.FromArgb(116, 182, 255): Color.FromArgb(29, 78, 216); } }
    public static Color AccentSoft   { get { return Dark ? Color.FromArgb(27, 47, 74)   : Color.FromArgb(224, 237, 255); } }
    public static Color AccentSoftFg { get { return Dark ? Color.FromArgb(136, 198, 255): Color.FromArgb(24, 68, 148); } }

    public static Color Success      { get { return Dark ? Color.FromArgb(63, 185, 80)  : Color.FromArgb(22, 163, 74); } }
    public static Color SuccessSoft  { get { return Dark ? Color.FromArgb(22, 54, 34)   : Color.FromArgb(220, 252, 231); } }
    public static Color Warning      { get { return Dark ? Color.FromArgb(227, 179, 65) : Color.FromArgb(217, 119, 6); } }
    public static Color WarningSoft  { get { return Dark ? Color.FromArgb(58, 42, 18)   : Color.FromArgb(254, 243, 199); } }
    public static Color Danger       { get { return Dark ? Color.FromArgb(248, 113, 113): Color.FromArgb(220, 38, 38); } }
    public static Color DangerSoft   { get { return Dark ? Color.FromArgb(62, 26, 32)   : Color.FromArgb(254, 226, 226); } }

    // 代码胶囊与搜索命中高亮
    public static Color CodeChipBg   { get { return Dark ? Color.FromArgb(30, 37, 48)   : Color.FromArgb(241, 245, 250); } }
    public static Color CodeChipEdge { get { return Dark ? Color.FromArgb(46, 56, 70)   : Color.FromArgb(222, 230, 240); } }
    public static Color CodeChipFg   { get { return Dark ? Color.FromArgb(147, 197, 253): Color.FromArgb(30, 64, 150); } }
    public static Color HitHighlight { get { return Dark ? Color.FromArgb(110, 82, 20)  : Color.FromArgb(254, 240, 138); } }
    public static Color HitHighlightFg { get { return Dark ? Color.FromArgb(255, 245, 200) : Color.FromArgb(113, 63, 18); } }
    public static Color StarActive   { get { return Dark ? Color.FromArgb(250, 204, 21) : Color.FromArgb(234, 154, 8); } }

    /// <summary>按数据表名返回精致来源徽标配色（前景 + 柔底）。</summary>
    public static void GetSourceBadgeColors(string source, out Color bg, out Color fg)
    {
        string s = source ?? "";
        if (s.IndexOf("指令", StringComparison.Ordinal) >= 0)
        {
            bg = Dark ? Color.FromArgb(26, 48, 80) : Color.FromArgb(219, 234, 254);
            fg = Dark ? Color.FromArgb(125, 195, 255) : Color.FromArgb(29, 78, 216);
        }
        else if (s.IndexOf("技能", StringComparison.Ordinal) >= 0)
        {
            bg = Dark ? Color.FromArgb(46, 32, 74) : Color.FromArgb(237, 233, 254);
            fg = Dark ? Color.FromArgb(196, 165, 255) : Color.FromArgb(109, 40, 217);
        }
        else if (s.IndexOf("物品", StringComparison.Ordinal) >= 0)
        {
            bg = Dark ? Color.FromArgb(18, 52, 62) : Color.FromArgb(207, 250, 254);
            fg = Dark ? Color.FromArgb(103, 232, 249) : Color.FromArgb(14, 116, 144);
        }
        else if (s.IndexOf("模型", StringComparison.Ordinal) >= 0)
        {
            bg = Dark ? Color.FromArgb(62, 40, 20) : Color.FromArgb(255, 237, 213);
            fg = Dark ? Color.FromArgb(253, 186, 116) : Color.FromArgb(194, 65, 12);
        }
        else if (s.IndexOf("SCP", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            bg = Dark ? Color.FromArgb(66, 26, 32) : Color.FromArgb(254, 226, 226);
            fg = Dark ? Color.FromArgb(252, 165, 165) : Color.FromArgb(185, 28, 28);
        }
        else if (s.IndexOf("角色", StringComparison.Ordinal) >= 0)
        {
            bg = Dark ? Color.FromArgb(20, 56, 38) : Color.FromArgb(220, 252, 231);
            fg = Dark ? Color.FromArgb(134, 239, 172) : Color.FromArgb(21, 128, 61);
        }
        else
        {
            bg = Dark ? Color.FromArgb(60, 28, 52) : Color.FromArgb(252, 231, 243);
            fg = Dark ? Color.FromArgb(249, 168, 212) : Color.FromArgb(190, 24, 93);
        }
    }
}

public enum NavIconKind
{
    Star,
    Terminal,
    Bolt,
    Box,
    Cube,
    User,
    Biohazard,
    Bookmark,
    Server,
    Crosshair,
    Timer,
    Wrench,
    Info,
    Search,
    SidebarToggle,
    Theme,
    Settings
}

/// <summary>
/// 纯 GDI+ 抗锯齿矢量线框图标绘制器：零字体依赖、不出现豆腐块、深浅色自适应。
/// </summary>
public static class UiIcons
{
    public static void Draw(Graphics g, NavIconKind kind, Rectangle rc, Color color, bool filled)
    {
        if (g == null) return;
        SmoothingMode old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cx = rc.X + rc.Width * 0.5f;
        float cy = rc.Y + rc.Height * 0.5f;
        float r = Math.Min(rc.Width, rc.Height) * 0.44f;

        using (var pen = new Pen(color, 1.55f))
        using (var br = new SolidBrush(color))
        {
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;

            switch (kind)
            {
                case NavIconKind.Star:
                {
                    var pts = new PointF[10];
                    for (int i = 0; i < 10; i++)
                    {
                        double ang = -Math.PI / 2.0 + i * Math.PI / 5.0;
                        float rad = (i % 2 == 0) ? r * 1.05f : r * 0.45f;
                        pts[i] = new PointF(cx + (float)Math.Cos(ang) * rad, cy + (float)Math.Sin(ang) * rad);
                    }
                    if (filled) g.FillPolygon(br, pts);
                    else g.DrawPolygon(pen, pts);
                    break;
                }
                case NavIconKind.Terminal:
                {
                    var box = new RectangleF(cx - r, cy - r * 0.82f, r * 2f, r * 1.64f);
                    using (var p = Native.RoundedPath(Rectangle.Round(box), 3))
                        g.DrawPath(pen, p);
                    g.DrawLine(pen, cx - r * 0.52f, cy - r * 0.28f, cx - r * 0.14f, cy);
                    g.DrawLine(pen, cx - r * 0.52f, cy + r * 0.28f, cx - r * 0.14f, cy);
                    g.DrawLine(pen, cx + r * 0.08f, cy + r * 0.28f, cx + r * 0.56f, cy + r * 0.28f);
                    break;
                }
                case NavIconKind.Bolt:
                {
                    var pts = new PointF[]
                    {
                        new PointF(cx + r * 0.12f, cy - r),
                        new PointF(cx - r * 0.60f, cy + r * 0.10f),
                        new PointF(cx + r * 0.04f, cy + r * 0.10f),
                        new PointF(cx - r * 0.14f, cy + r),
                        new PointF(cx + r * 0.62f, cy - r * 0.12f),
                        new PointF(cx - r * 0.02f, cy - r * 0.12f)
                    };
                    if (filled) g.FillPolygon(br, pts);
                    else g.DrawPolygon(pen, pts);
                    break;
                }
                case NavIconKind.Box:
                {
                    var b = new RectangleF(cx - r * 0.9f, cy - r * 0.72f, r * 1.8f, r * 1.44f);
                    g.DrawRectangle(pen, b.X, b.Y, b.Width, b.Height);
                    g.DrawLine(pen, b.X, cy - r * 0.18f, b.Right, cy - r * 0.18f);
                    g.DrawLine(pen, cx - r * 0.28f, cy + r * 0.18f, cx + r * 0.28f, cy + r * 0.18f);
                    break;
                }
                case NavIconKind.Cube:
                {
                    float dx = r * 0.86f, dy = r * 0.48f;
                    var top = new PointF(cx, cy - r);
                    var bot = new PointF(cx, cy + r);
                    var tl = new PointF(cx - dx, cy - dy);
                    var tr = new PointF(cx + dx, cy - dy);
                    var bl = new PointF(cx - dx, cy + dy);
                    var brp = new PointF(cx + dx, cy + dy);
                    g.DrawPolygon(pen, new PointF[] { top, tr, brp, bot, bl, tl });
                    g.DrawLine(pen, tl, new PointF(cx, cy));
                    g.DrawLine(pen, tr, new PointF(cx, cy));
                    g.DrawLine(pen, new PointF(cx, cy), bot);
                    break;
                }
                case NavIconKind.User:
                {
                    float hr = r * 0.42f;
                    g.DrawEllipse(pen, cx - hr, cy - r * 0.92f, hr * 2f, hr * 2f);
                    g.DrawArc(pen, cx - r * 0.88f, cy + r * 0.12f, r * 1.76f, r * 1.4f, 190, 160);
                    break;
                }
                case NavIconKind.Biohazard:
                {
                    float ir = r * 0.30f;
                    g.DrawEllipse(pen, cx - ir, cy - ir, ir * 2f, ir * 2f);
                    for (int k = 0; k < 3; k++)
                    {
                        double a = -Math.PI / 2.0 + k * (Math.PI * 2.0 / 3.0);
                        float ox = cx + (float)Math.Cos(a) * r * 0.56f;
                        float oy = cy + (float)Math.Sin(a) * r * 0.56f;
                        float lr = r * 0.42f;
                        g.DrawEllipse(pen, ox - lr, oy - lr, lr * 2f, lr * 2f);
                    }
                    break;
                }
                case NavIconKind.Bookmark:
                {
                    var pts = new PointF[]
                    {
                        new PointF(cx - r * 0.62f, cy - r * 0.88f),
                        new PointF(cx + r * 0.62f, cy - r * 0.88f),
                        new PointF(cx + r * 0.62f, cy + r * 0.88f),
                        new PointF(cx, cy + r * 0.42f),
                        new PointF(cx - r * 0.62f, cy + r * 0.88f)
                    };
                    if (filled) g.FillPolygon(br, pts);
                    else g.DrawPolygon(pen, pts);
                    break;
                }
                case NavIconKind.Server:
                {
                    float rw = r * 1.8f, rh = r * 0.72f;
                    g.DrawRectangle(pen, cx - rw / 2f, cy - r * 0.82f, rw, rh);
                    g.DrawRectangle(pen, cx - rw / 2f, cy + r * 0.10f, rw, rh);
                    g.FillEllipse(br, cx - rw / 2f + 3f, cy - r * 0.56f, 2.4f, 2.4f);
                    g.FillEllipse(br, cx - rw / 2f + 3f, cy + r * 0.36f, 2.4f, 2.4f);
                    break;
                }
                case NavIconKind.Crosshair:
                {
                    float cr = r * 0.66f;
                    g.DrawEllipse(pen, cx - cr, cy - cr, cr * 2f, cr * 2f);
                    g.DrawLine(pen, cx - r * 1.05f, cy, cx - cr * 0.55f, cy);
                    g.DrawLine(pen, cx + cr * 0.55f, cy, cx + r * 1.05f, cy);
                    g.DrawLine(pen, cx, cy - r * 1.05f, cx, cy - cr * 0.55f);
                    g.DrawLine(pen, cx, cy + cr * 0.55f, cx, cy + r * 1.05f);
                    break;
                }
                case NavIconKind.Timer:
                {
                    float cr = r * 0.78f;
                    g.DrawEllipse(pen, cx - cr, cy - cr + 1f, cr * 2f, cr * 2f);
                    g.DrawLine(pen, cx - r * 0.3f, cy - r * 0.98f, cx + r * 0.3f, cy - r * 0.98f);
                    g.DrawLine(pen, cx, cy + 1f, cx, cy - cr * 0.48f);
                    g.DrawLine(pen, cx, cy + 1f, cx + cr * 0.45f, cy + cr * 0.25f);
                    break;
                }
                case NavIconKind.Wrench:
                {
                    g.DrawLine(pen, cx - r * 0.65f, cy + r * 0.65f, cx + r * 0.25f, cy - r * 0.25f);
                    g.DrawArc(pen, cx - r * 0.05f, cy - r * 0.95f, r * 1.0f, r * 1.0f, -15, 250);
                    break;
                }
                case NavIconKind.Info:
                {
                    g.DrawEllipse(pen, cx - r * 0.88f, cy - r * 0.88f, r * 1.76f, r * 1.76f);
                    g.FillEllipse(br, cx - 1.1f, cy - r * 0.48f, 2.2f, 2.2f);
                    g.DrawLine(pen, cx, cy - r * 0.12f, cx, cy + r * 0.48f);
                    break;
                }
                case NavIconKind.Search:
                {
                    float cr = r * 0.62f;
                    float sx = cx - r * 0.18f, sy = cy - r * 0.18f;
                    g.DrawEllipse(pen, sx - cr, sy - cr, cr * 2f, cr * 2f);
                    g.DrawLine(pen, sx + cr * 0.72f, sy + cr * 0.72f, cx + r * 0.92f, cy + r * 0.92f);
                    break;
                }
                case NavIconKind.SidebarToggle:
                {
                    var box = new RectangleF(cx - r * 0.9f, cy - r * 0.75f, r * 1.8f, r * 1.5f);
                    g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
                    g.DrawLine(pen, box.X + box.Width * 0.36f, box.Y, box.X + box.Width * 0.36f, box.Bottom);
                    break;
                }
                case NavIconKind.Theme:
                {
                    if (ThemePalette.Dark)
                    {
                        g.DrawArc(pen, cx - r * 0.75f, cy - r * 0.75f, r * 1.5f, r * 1.5f, 40, 240);
                        g.DrawArc(pen, cx - r * 0.25f, cy - r * 0.75f, r * 1.1f, r * 1.2f, 110, 140);
                    }
                    else
                    {
                        float sr = r * 0.44f;
                        g.DrawEllipse(pen, cx - sr, cy - sr, sr * 2f, sr * 2f);
                        for (int k = 0; k < 8; k++)
                        {
                            double a = k * Math.PI / 4.0;
                            float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
                            g.DrawLine(pen, cx + c * r * 0.72f, cy + s * r * 0.72f, cx + c * r * 0.96f, cy + s * r * 0.96f);
                        }
                    }
                    break;
                }
                case NavIconKind.Settings:
                {
                    float innerR = r * 0.30f;
                    float midR = r * 0.62f;
                    g.DrawEllipse(pen, cx - innerR, cy - innerR, innerR * 2f, innerR * 2f);
                    g.DrawEllipse(pen, cx - midR, cy - midR, midR * 2f, midR * 2f);
                    for (int k = 0; k < 8; k++)
                    {
                        double a = k * Math.PI / 4.0;
                        float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
                        g.DrawLine(pen, cx + c * r * 0.65f, cy + s * r * 0.65f, cx + c * r * 0.94f, cy + s * r * 0.94f);
                    }
                    break;
                }
            }
        }
        g.SmoothingMode = old;
    }
}
