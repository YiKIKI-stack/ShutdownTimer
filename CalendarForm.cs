using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ShutdownTimer;

/// <summary>
/// 日期选择弹层:无边框不透明小窗,整窗自绘月历。
/// 独立顶层窗口不受主面板亚克力影响,所以这里可以安全用不透明底色。
/// </summary>
public sealed class CalendarForm : Form
{
    // 逻辑度量
    private const int Pad = 10, HeadH = 36, WeekH = 26, CellW = 36, CellH = 32, Cols = 7, Rows = 6;

    private readonly DateTime _selected;
    private readonly Action<DateTime> _onPick;
    private readonly float _scale;
    private DateTime _month;
    private DateTime _hoverDay;
    private Rectangle _rPrevYear, _rPrevMonth, _rNextMonth, _rNextYear, _rTitle;
    private readonly Rectangle[] _cells = new Rectangle[Cols * Rows];
    private readonly DateTime[] _cellDates = new DateTime[Cols * Rows];
    private Font _fHead, _fCell, _fWeek;

    private static readonly string[] WeekLabels = ["一", "二", "三", "四", "五", "六", "日"];
    private static readonly StringFormat FmtCenter = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

    private int Px(float v) => (int)Math.Round(v * _scale);

    public CalendarForm(DateTime selected, Action<DateTime> onPick, float scale)
    {
        _selected = selected.Date;
        _month = new DateTime(selected.Year, selected.Month, 1);
        _onPick = onPick;
        _scale = scale;

        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.FromArgb(250, 250, 250);
        ClientSize = new Size(Px(Pad * 2 + CellW * Cols), Px(Pad * 2 + HeadH + WeekH + CellH * Rows));
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

        _fHead = MakeFont(13, true);
        _fCell = MakeFont(12, false);
        _fWeek = MakeFont(11, false);
    }

    private Font MakeFont(float logicalPx, bool bold)
        => new("Segoe UI", logicalPx * _scale, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);

    /// <summary>
    /// 不抢激活权。若让弹层去激活窗口,主面板一收到鼠标抬起就会重新激活,
    /// 弹层立刻 Deactivate 自杀,用户根本来不及点到日期。
    /// 关闭改由主面板显式管理(选中 / Esc / 再点 ▾ / 点击弹层外部)。
    /// </summary>
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000;   // WS_EX_NOACTIVATE
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        FluentTheme.ApplyRoundedCorners(this);
        LayoutCells();
    }

    private void LayoutCells()
    {
        int x0 = Px(Pad), y0 = Px(Pad);
        int headW = ClientSize.Width - x0 * 2;
        int arrow = Px(26);
        _rPrevYear = new Rectangle(x0, y0, arrow, Px(HeadH));
        _rPrevMonth = new Rectangle(x0 + arrow, y0, arrow, Px(HeadH));
        _rNextMonth = new Rectangle(x0 + headW - arrow * 2, y0, arrow, Px(HeadH));
        _rNextYear = new Rectangle(x0 + headW - arrow, y0, arrow, Px(HeadH));
        _rTitle = new Rectangle(x0 + arrow * 2, y0, headW - arrow * 4, Px(HeadH));

        var first = _month;
        int offset = ((int)first.DayOfWeek + 6) % 7;   // 周一为第一列
        var start = first.AddDays(-offset);
        int gx = x0, gy = y0 + Px(HeadH + WeekH);
        for (int i = 0; i < _cells.Length; i++)
        {
            int col = i % Cols, row = i / Cols;
            _cells[i] = new Rectangle(gx + col * Px(CellW), gy + row * Px(CellH), Px(CellW), Px(CellH));
            _cellDates[i] = start.AddDays(i);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        g.Clear(BackColor);

        var head = new Rectangle(Px(Pad), Px(Pad), ClientSize.Width - Px(Pad * 2), Px(HeadH));
        DrawStr(g, $"{_month:yyyy} 年 {_month.Month} 月", _fHead, FluentTheme.TextPrimary, head, FmtCenter);
        DrawChevronPair(g, _rPrevYear, false, true);
        DrawChevronPair(g, _rPrevMonth, false, false);
        DrawChevronPair(g, _rNextMonth, true, false);
        DrawChevronPair(g, _rNextYear, true, true);

        int wy = Px(Pad + HeadH);
        for (int c = 0; c < Cols; c++)
        {
            var r = new Rectangle(_cells[c].X, wy, Px(CellW), Px(WeekH));
            bool weekend = c >= 5;
            DrawStr(g, WeekLabels[c], _fWeek, weekend ? FluentTheme.TextSecondary : FluentTheme.TextMuted, r, FmtCenter);
        }

        var today = DateTime.Today;
        for (int i = 0; i < _cells.Length; i++)
        {
            var r = _cells[i];
            var d = _cellDates[i];
            bool outside = d.Month != _month.Month;
            bool selected = d.Date == _selected;
            bool isToday = d.Date == today;

            if (selected)
            {
                using var path = FluentTheme.RoundedPath(Rectangle.Inflate(r, -Px(3), -Px(3)), Px(6));
                using var b = new SolidBrush(FluentTheme.Accent);
                g.FillPath(b, path);
            }
            else if (d.Date == _hoverDay)
            {
                using var path = FluentTheme.RoundedPath(Rectangle.Inflate(r, -Px(3), -Px(3)), Px(6));
                using var b = new SolidBrush(Color.FromArgb(235, 235, 237));
                g.FillPath(b, path);
            }

            var color = selected ? Color.White
                : outside ? Color.FromArgb(190, 193, 198)
                : isToday ? FluentTheme.Accent
                : FluentTheme.TextPrimary;
            DrawStr(g, d.Day.ToString(), _fCell, color, r, FmtCenter);
        }

        using var hp = new Pen(Color.FromArgb(205, 205, 208), Math.Max(1f, _scale));
        g.DrawRectangle(hp, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    /// <summary>年月切换箭头;doubleArrow 时画两层表示"年",否则表示"月"。</summary>
    private void DrawChevronPair(Graphics g, Rectangle r, bool right, bool doubleArrow)
    {
        bool hover = r.Contains(PointToClient(Cursor.Position));
        if (hover)
        {
            using var path = FluentTheme.RoundedPath(Rectangle.Inflate(r, -Px(2), -Px(2)), Px(4));
            using var b = new SolidBrush(Color.FromArgb(232, 232, 235));
            g.FillPath(b, path);
        }
        using var pen = new Pen(FluentTheme.TextMuted, Math.Max(1f, 1.4f * _scale));
        pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
        int steps = doubleArrow ? 2 : 1;
        float w = Px(3.5f), h = Px(5f);
        float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f;
        float sign = right ? 1f : -1f;
        for (int i = 0; i < steps; i++)
        {
            float ox = cx + (i - (steps - 1) / 2f) * Px(5f) * sign;
            g.DrawLines(pen, new[]
            {
                new PointF(ox - w * sign, cy - h),
                new PointF(ox + w * sign, cy),
                new PointF(ox - w * sign, cy + h),
            });
        }
    }

    private void DrawStr(Graphics g, string s, Font f, Color c, RectangleF r, StringFormat fmt)
    {
        using var b = new SolidBrush(c);
        g.DrawString(s, f, b, r, fmt);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = DayAt(e.Location);
        if (hit == _hoverDay) return;
        _hoverDay = hit;
        Invalidate();
    }

    private DateTime DayAt(Point p)
    {
        for (int i = 0; i < _cells.Length; i++)
            if (_cells[i].Contains(p)) return _cellDates[i];
        return DateTime.MinValue;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (_rPrevYear.Contains(e.Location)) { ShiftMonths(-12); return; }
        if (_rPrevMonth.Contains(e.Location)) { ShiftMonths(-1); return; }
        if (_rNextMonth.Contains(e.Location)) { ShiftMonths(1); return; }
        if (_rNextYear.Contains(e.Location)) { ShiftMonths(12); return; }
        var d = DayAt(e.Location);
        if (d != DateTime.MinValue)
        {
            _onPick(d);
            Close();
        }
    }

    private void ShiftMonths(int months)
    {
        _month = _month.AddMonths(months);
        LayoutCells();
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fHead.Dispose(); _fCell.Dispose(); _fWeek.Dispose();
        }
        base.Dispose(disposing);
    }
}
