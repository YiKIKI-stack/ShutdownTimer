using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ShutdownTimer;

/// <summary>Fluent 设计令牌与窗口材质基建。色值逐项取自设计稿 1.html 的 CSS。</summary>
internal static class FluentTheme
{
    // ── 主色 ──────────────────────────────────────────────
    public static readonly Color Accent = Color.FromArgb(0x00, 0x5F, 0xB8);       // #005fb8
    public static readonly Color AccentHover = Color.FromArgb(0x00, 0x51, 0x9E);  // hover:bg-[#00519e]
    public static readonly Color AccentDown = Color.FromArgb(0x00, 0x47, 0x8A);   // active:bg-[#00478a]

    // ── 文字(Tailwind gray)───────────────────────────────
    public static readonly Color TextPrimary = Color.FromArgb(0x1F, 0x29, 0x37);   // gray-800
    public static readonly Color TextTitle = Color.FromArgb(0x37, 0x41, 0x51);     // gray-700
    public static readonly Color TextSecondary = Color.FromArgb(0x6B, 0x72, 0x80); // gray-500
    public static readonly Color TextMuted = Color.FromArgb(0x4B, 0x55, 0x63);     // gray-600

    // ── 危险色(Tailwind red)──────────────────────────────
    public static readonly Color Danger = Color.FromArgb(0xDC, 0x26, 0x26);        // red-600
    public static readonly Color DangerBorder = Color.FromArgb(0xFE, 0xCA, 0xCA);  // red-200
    public static readonly Color DangerHover = Color.FromArgb(0xFE, 0xF2, 0xF2);   // red-50
    public static readonly Color DangerCloseBg = Color.FromArgb(0xEF, 0x44, 0x44); // red-500

    // ── 玻璃上的各层(已预合成,不依赖 GDI+ 对 DWM 表面的混合)──
    /// <summary>rgba(243,243,243,0.65):窗口本体,对应 .acrylic-window</summary>
    public static readonly Color Glass = Color.FromArgb(166, 243, 243, 243);
    /// <summary>bg-black/5 落在玻璃上:分段条与设置卡</summary>
    public static readonly Color Card = Over(Glass, Color.FromArgb(13, 0, 0, 0));
    /// <summary>bg-white/40 落在玻璃上:胶囊预设按钮</summary>
    public static readonly Color Pill = Over(Glass, Color.FromArgb(102, 255, 255, 255));
    /// <summary>hover:bg-white/70 落在玻璃上</summary>
    public static readonly Color PillHover = Over(Glass, Color.FromArgb(179, 255, 255, 255));
    /// <summary>border-white/60:胶囊描边</summary>
    public static readonly Color PillBorder = Over(Glass, Color.FromArgb(153, 255, 255, 255));
    /// <summary>rgba(0,0,0,0.2):输入框未聚焦下划线</summary>
    public static readonly Color FieldUnderline = Over(Glass, Color.FromArgb(51, 0, 0, 0));
    /// <summary>rgba(255,255,255,0.5):窗口外描边</summary>
    public static readonly Color Hairline = Over(Glass, Color.FromArgb(128, 255, 255, 255));
    /// <summary>inset 0 1px 0 rgba(255,255,255,0.6):顶部高光线</summary>
    public static readonly Color TopHighlight = Over(Glass, Color.FromArgb(153, 255, 255, 255));
    /// <summary>Win10 / RDP / 关闭透明效果时的不透明回退底色</summary>
    public static readonly Color BgFallback = Color.FromArgb(243, 243, 243);

    /// <summary>src 按自身 alpha 叠加到 dst。GDI+ 在 DWM 背景上做多段半透明混合不可靠,故一次算清。
    /// 通道分量按 0-255 输入并返回,只有 alpha 做 0-1 归一化。</summary>
    public static Color Over(Color dst, Color src)
    {
        float sa = src.A / 255f, da = dst.A / 255f;
        float oa = sa + da * (1 - sa);
        if (oa <= 0.001f) return Color.FromArgb(0, 0, 0, 0);
        static float Ch(float s, float d, float sa, float da, float oa)
            => (s * sa + d * da * (1 - sa)) / oa;
        return Color.FromArgb(
            (int)Math.Round(oa * 255f),
            (int)Math.Round(Ch(src.R, dst.R, sa, da, oa)),
            (int)Math.Round(Ch(src.G, dst.G, sa, da, oa)),
            (int)Math.Round(Ch(src.B, dst.B, sa, da, oa)));
    }

    // ── Win32 ────────────────────────────────────────────
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MARGINS
    {
        public int Left, Right, Top, Bottom;
    }

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMWCP_ROUND = 2;
    private const int DWMSBT_NONE = 1;
    private const int DWMSBT_TRANSIENTWINDOW = 3; // 亚克力

    /// <summary>启用 DWM 亚克力背景。返回 false 表示系统不支持,调用方应改用 BgFallback 实色。</summary>
    public static bool ApplyAcrylic(Form f)
    {
        try
        {
            var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            if (DwmExtendFrameIntoClientArea(f.Handle, ref margins) != 0)
                return false;
            int backdrop = DWMSBT_TRANSIENTWINDOW;
            if (DwmSetWindowAttribute(f.Handle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) != 0)
                return false;
            int dark = 0; // 浅色标题栏字形
            DwmSetWindowAttribute(f.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>材质不可用时显式关掉背景,避免留下脏背景</summary>
    public static void ClearBackdrop(Form f)
    {
        try
        {
            int none = DWMSBT_NONE;
            DwmSetWindowAttribute(f.Handle, DWMWA_SYSTEMBACKDROP_TYPE, ref none, sizeof(int));
        }
        catch { /* 旧系统忽略 */ }
    }


    /// <summary>窗口圆角(Win11)。不要用 Control.Region 裁窗口——会破坏 DWM 边缘与阴影。</summary>
    public static void ApplyRoundedCorners(Form f)
    {
        try
        {
            int pref = DWMWCP_ROUND;
            DwmSetWindowAttribute(f.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
        }
        catch { /* 旧系统忽略 */ }
    }

    public static GraphicsPath RoundedPath(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        if (d <= 0)
        {
            path.AddRectangle(r);
            return path;
        }
        d = Math.Min(d, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>仅只左上角圆角的输入框外形(设计稿 .rounded-t-md)</summary>
    public static GraphicsPath TopRounded(Rectangle r, int rad)
    {
        rad = Math.Min(rad, Math.Min(r.Width / 2, r.Height));
        var p = new GraphicsPath();
        int d = rad * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddLine(r.Right, r.Y + rad, r.Right, r.Bottom);
        p.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
        p.AddLine(r.X, r.Bottom, r.X, r.Y + rad);
        p.CloseFigure();
        return p;
    }

    /// <summary>把控件裁成圆角。仅用于不透明窗口(警告卡);亚克力窗口上禁用,会破坏 DWM 边缘。</summary>
    public static void MakeRounded(Control c, int radius)
        => c.Region = new Region(RoundedPath(new Rectangle(0, 0, c.Width, c.Height), radius));
}

