using System.Drawing.Drawing2D;
using System.Globalization;

namespace ShutdownTimer;

/// <summary>设计导出的图标 path data(viewBox 均为 0 0 1024 1024),由用户提供的 SVG 转换而来。</summary>
internal static class Icons
{
    public const string Hourglass = "M449.59694 552.460488c-2.589471 0-5.210911-0.319688-7.800382-0.959063C313.729623 519.308862 224.280976 404.668816 224.280976 272.733659L224.280976 160.842927c0-17.646767 14.322014-31.96878 31.96878-31.96878s31.96878 14.322014 31.96878 31.96878l0 111.890732c0 102.587817 69.564066 191.716777 169.114849 216.748332 17.134267 4.315785 27.52512 21.674833 23.241303 38.778131C476.930248 542.805916 463.886985 552.460488 449.59694 552.460488L449.59694 552.460488zM574.40306 552.460488c-14.290045 0-27.301339-9.654572-30.977748-24.168398-4.315785-17.134267 6.074068-34.493315 23.208336-38.777132C666.217397 464.450435 735.781463 375.321475 735.781463 272.733659L735.781463 160.842927c0-17.646767 14.290045-31.96878 31.96878-31.96878s31.96878 14.322014 31.96878 31.96878l0 111.890732c0 131.935157-89.448648 246.575204-217.483614 278.767766C579.613971 552.1408 576.992531 552.460488 574.40306 552.460488zM767.750244 896.124878c-17.678736 0-31.96878-14.322014-31.96878-31.96878l0-95.906341c0-102.619785-69.564066-191.748745-169.114849-216.748332-17.134267-4.283817-27.52512-21.674833-23.241303-38.778131 4.347754-17.134267 21.930583-27.397245 38.777132-23.208336C710.270377 521.642583 799.719024 636.28263 799.719024 768.249756l0 95.906341C799.719024 881.802864 785.42898 896.124878 767.750244 896.124878zM256.249756 896.124878c-17.646767 0-31.96878-14.322014-31.96878-31.96878l0-95.906341c0-131.967126 89.448648-246.607173 217.516581-278.767766 17.167235-4.18791 34.461346 6.074068 38.777132 23.209335 4.315785 17.134267-6.074068 34.493315-23.241303 38.777132C357.750634 576.501011 288.218537 665.629971 288.218537 768.249756l0 95.906341C288.218537 881.802864 273.896523 896.124878 256.249756 896.124878zM863.656585 192.811707 160.343415 192.811707c-17.646767 0-31.96878-14.322014-31.96878-31.96878L128.374634 32.967805c0-17.646767 14.322014-31.96878 31.96878-31.96878l703.313171 0c17.678736 0 31.96878 14.322014 31.96878 31.96878l0 127.875122C895.625366 178.489694 881.335321 192.811707 863.656585 192.811707zM192.312195 128.874146l639.37561 0L831.687805 64.936585 192.312195 64.936585 192.312195 128.874146zM863.656585 1024 160.343415 1024c-17.646767 0-31.96878-14.322014-31.96878-31.96878L128.374634 864.156098c0-17.646767 14.322014-31.96878 31.96878-31.96878l703.313171 0c17.678736 0 31.96878 14.322014 31.96878 31.96878l0 127.875122C895.625366 1009.677986 881.335321 1024 863.656585 1024zM192.312195 960.062439l639.37561 0 0-63.937561L192.312195 896.124878 192.312195 960.062439z";
}

/// <summary>
/// 极简 SVG path data 解析器:把设计导出的 d 属性直接转成 GraphicsPath,避免为两个图标引入光栅资源。
/// 支持 M/m L/l H/h V/v C/c S/s A/a Z/z;未识别命令(Q/T 等)会抛错,由 Build 兜住只画不出该图标。
/// 全程在 SVG 用户坐标里运算,只在落笔时映射到目标矩形,避免两套坐标系来回换算。
/// </summary>
internal static class SvgPath
{
    /// <summary>解析图标路径。调用点全在 OnPaint 里,所以畸形数据只能画不出这个图标,
    /// 绝不能把整窗重绘炸掉(无人值守工具,重绘中断等于面板空白)。</summary>
    public static GraphicsPath Build(string d, RectangleF box, float viewBox = 1024f)
    {
        try { return BuildInner(d, box, viewBox); }
        catch (Exception ex) { Logger.Warn($"图标解析失败: {ex.Message}"); return new GraphicsPath(); }
    }

    private static GraphicsPath BuildInner(string d, RectangleF box, float viewBox)
    {
        var path = new GraphicsPath { FillMode = FillMode.Alternate };
        try { ParseInto(path, d, box, viewBox); }
        catch { path.Dispose(); throw; }
        return path;
    }

    private static void ParseInto(GraphicsPath path, string d, RectangleF box, float viewBox)
    {
        float sx = box.Width / viewBox, sy = box.Height / viewBox;
        PointF Map(PointF p) => new(box.X + p.X * sx, box.Y + p.Y * sy);

        var r = new Cursor(d);
        PointF cur = new(float.NaN, float.NaN), start = PointF.Empty, ctrl = PointF.Empty;
        char cmd = '\0';
        bool lastWasCubic = false;

        while (!r.End)
        {
            int before = r.Pos;
            r.SkipSep();
            if (!r.End && char.IsLetter(r.Peek)) cmd = r.Next();
            if (cmd == '\0') break;
            bool rel = char.IsLower(cmd);
            switch (char.ToUpperInvariant(cmd))
            {
                case 'M':
                {
                    bool first = true;
                    while (r.Num(out float x) && r.Num(out float y))
                    {
                        if (rel) { x += cur.X; y += cur.Y; }
                        var p = new PointF(x, y);
                        if (first) { path.StartFigure(); start = p; first = false; }
                        else path.AddLine(Map(cur), Map(p));
                        cur = p; lastWasCubic = false;
                    }
                    cmd = rel ? 'l' : 'L';
                    break;
                }
                case 'L':
                    while (r.Num(out float x) && r.Num(out float y))
                    {
                        if (rel) { x += cur.X; y += cur.Y; }
                        var p = new PointF(x, y);
                        path.AddLine(Map(cur), Map(p));
                        cur = p; lastWasCubic = false;
                    }
                    break;
                case 'H':
                    while (r.Num(out float x))
                    {
                        if (rel) x += cur.X;
                        var p = new PointF(x, cur.Y);
                        path.AddLine(Map(cur), Map(p));
                        cur = p; lastWasCubic = false;
                    }
                    break;
                case 'V':
                    while (r.Num(out float y))
                    {
                        if (rel) y += cur.Y;
                        var p = new PointF(cur.X, y);
                        path.AddLine(Map(cur), Map(p));
                        cur = p; lastWasCubic = false;
                    }
                    break;
                case 'C':
                    while (r.Num(out float x1) && r.Num(out float y1) && r.Num(out float x2) && r.Num(out float y2)
                           && r.Num(out float x) && r.Num(out float y))
                    {
                        if (rel)
                        {
                            x1 += cur.X; y1 += cur.Y; x2 += cur.X; y2 += cur.Y; x += cur.X; y += cur.Y;
                        }
                        var c1 = new PointF(x1, y1); var c2 = new PointF(x2, y2); var e = new PointF(x, y);
                        path.AddBezier(Map(cur), Map(c1), Map(c2), Map(e));
                        ctrl = c2; cur = e; lastWasCubic = true;
                    }
                    break;
                case 'S':
                    while (r.Num(out float x2) && r.Num(out float y2) && r.Num(out float x) && r.Num(out float y))
                    {
                        if (rel) { x2 += cur.X; y2 += cur.Y; x += cur.X; y += cur.Y; }
                        var c2 = new PointF(x2, y2); var e = new PointF(x, y);
                        var c1 = lastWasCubic
                            ? new PointF(2 * cur.X - ctrl.X, 2 * cur.Y - ctrl.Y)
                            : cur;
                        path.AddBezier(Map(cur), Map(c1), Map(c2), Map(e));
                        ctrl = c2; cur = e; lastWasCubic = true;
                    }
                    break;
                case 'A':
                case 'a':
                    while (r.Num(out float arx) && r.Num(out float ary) && r.Num(out float arot)
                           && r.Num(out float alarge) && r.Num(out float asweep)
                           && r.Num(out float ax) && r.Num(out float ay))
                    {
                        if (rel) { ax += cur.X; ay += cur.Y; }
                        var p2u = new PointF(ax, ay);
                        if (!float.IsNaN(cur.X) && (cur.X != p2u.X || cur.Y != p2u.Y))
                            AppendArc(path, cur, Math.Abs(arx), Math.Abs(ary), arot, alarge != 0, asweep != 0, p2u, Map);
                        cur = p2u; ctrl = p2u; lastWasCubic = false;
                    }
                    break;
                case 'Z':
                    if (!float.IsNaN(cur.X)) path.CloseFigure();
                    cur = start; lastWasCubic = false;
                    break;
                default:
                    throw new NotSupportedException($"SvgPath 不支持命令 '{cmd}'(Q/T/A 未实现)");
            }
            if (r.Pos == before) break;   // 没有推进就退出,避免畸形数据死循环
        }
    }

    /// <summary>
    /// SVG 椭圆弧(A/a)转三次贝塞尔:端点参数化 → 圆心参数化 → 按 ≤90° 分段,
    /// 每段用 k=4/3·tan(Δθ/4) 求控制点。所有几何在 SVG 用户坐标里算完再统一过 map。
    /// </summary>
    private static void AppendArc(GraphicsPath path, PointF p1, float rx, float ry, float angleDeg,
                                  bool large, bool sweep, PointF p2, Func<PointF, PointF> map)
    {
        if (rx == 0 || ry == 0) { path.AddLine(map(p1), map(p2)); return; }
        double phi = angleDeg * Math.PI / 180.0;
        double cosP = Math.Cos(phi), sinP = Math.Sin(phi);
        double dx2 = (p1.X - p2.X) / 2.0, dy2 = (p1.Y - p2.Y) / 2.0;
        double x1p = cosP * dx2 + sinP * dy2;
        double y1p = -sinP * dx2 + cosP * dy2;
        double norm = x1p * x1p / (rx * rx) + y1p * y1p / (ry * ry);
        if (norm > 1)
        {
            double sq = Math.Sqrt(norm);
            rx = (float)(rx * sq); ry = (float)(ry * sq);
        }
        double sign = large == sweep ? -1.0 : 1.0;
        double den = rx * rx * y1p * y1p + ry * ry * x1p * x1p;
        double coef = den == 0 ? 0 : sign * Math.Sqrt(Math.Max(0, (rx * rx * ry * ry - den) / den));
        double cxp = coef * rx * y1p / ry;
        double cyp = -coef * ry * x1p / rx;
        double cx = cosP * cxp - sinP * cyp + (p1.X + p2.X) / 2.0;
        double cy = sinP * cxp + cosP * cyp + (p1.Y + p2.Y) / 2.0;
        double th1 = ArcAngle(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
        double dth = ArcAngle((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry);
        if (!sweep && dth > 0) dth -= 2 * Math.PI;
        else if (sweep && dth < 0) dth += 2 * Math.PI;

        int segs = (int)Math.Ceiling(Math.Abs(dth) / (Math.PI / 2.0));
        double delta = dth / segs;
        double t = 4.0 / 3.0 * Math.Tan(delta / 4.0);
        double th = th1;
        var prev = p1;
        for (int i = 0; i < segs; i++)
        {
            double th2 = th + delta;
            double ex = cx + cosP * rx * Math.Cos(th2) - sinP * ry * Math.Sin(th2);
            double ey = cy + sinP * rx * Math.Cos(th2) + cosP * ry * Math.Sin(th2);
            double d1x = -rx * Math.Sin(th), d1y = ry * Math.Cos(th);
            double d2x = -rx * Math.Sin(th2), d2y = ry * Math.Cos(th2);
            double c1x = prev.X + t * (cosP * d1x - sinP * d1y);
            double c1y = prev.Y + t * (sinP * d1x + cosP * d1y);
            double c2x = ex - t * (cosP * d2x - sinP * d2y);
            double c2y = ey - t * (sinP * d2x + cosP * d2y);
            path.AddBezier(map(prev), map(new PointF((float)c1x, (float)c1y)),
                           map(new PointF((float)c2x, (float)c2y)), map(new PointF((float)ex, (float)ey)));
            prev = new PointF((float)ex, (float)ey);
            th = th2;
        }
    }

    /// <summary>两向量夹角(SVG 弧参数化用);叉积定方向。</summary>
    private static double ArcAngle(double ux, double uy, double vx, double vy)
    {
        double dot = ux * vx + uy * vy;
        double len = Math.Sqrt(ux * ux + uy * uy) * Math.Sqrt(vx * vx + vy * vy);
        double a = len == 0 ? 0 : Math.Acos(Math.Clamp(dot / len, -1, 1));
        return (ux * vy - uy * vx) < 0 ? -a : a;
    }

    private sealed class Cursor
    {
        private readonly string _s;
        private int _i;
        public Cursor(string s) => _s = s;
        public bool End => _i >= _s.Length;
        public int Pos => _i;
        public char Peek => _s[_i];
        public char Next() => _s[_i++];

        public void SkipSep()
        {
            while (!End && (char.IsWhiteSpace(_s[_i]) || _s[_i] == ',' || _s[_i] == ';')) _i++;
        }

        /// <summary>读取一个数,支持 -7.6 0-14.8 这类负号即分隔的紧凑写法</summary>
        public bool Num(out float v)
        {
            v = 0f;
            SkipSep();
            if (End) return false;
            int st = _i;
            if (_s[_i] is '+' or '-') _i++;
            bool digits = false;
            while (!End && char.IsDigit(_s[_i])) { _i++; digits = true; }
            if (!End && _s[_i] == '.')
            {
                _i++;
                while (!End && char.IsDigit(_s[_i])) { _i++; digits = true; }
            }
            if (!digits) { _i = st; return false; }
            return float.TryParse(_s.AsSpan(st, _i - st), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }
    }
}
