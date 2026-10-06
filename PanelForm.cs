using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace ShutdownTimer;

/// <summary>
/// 主面板 v3:整窗自绘的 Fluent 亚克力面板,度量与色值逐项对齐设计稿 1.html。
/// 窗口内没有任何子控件:实测 DwmExtendFrameIntoClientArea 的亚克力会把子控件的
/// GDI ClearType 文字一起混进玻璃,文字被冲淡成浅灰,所以一切文字都由本窗自绘。
/// </summary>
public class PanelForm : Form
{
    // ══ 设计稿度量(逻辑 px,对应 Tailwind 类;字号整体比设计稿收小一档)══
    private const int BaseW = 420;      // max-w-[420px]
    private const int TitleH = 36;
    private const int PadX = 24;        // px-6
    private const int Gap = 20;         // gap-5
    private const int StatusH = 40;
    private const int SegH = 34;
    private const int FieldH = 32;
    private const int PillH = 27;
    private const int ActionH = 38;
    private const int CancelW = 100;    // 取消按钮宽度(与启动按钮同行,靠右)
    private const int CardH = 72;
    private const int FooterH = 28;
    private const int Pt = 8, Mt2 = 8, Mt1 = 4, Pb = 24;
    private const int GroupGap = 12;    // 字段组之间 gap-3
    private const int LabelGap = 4;     // 控件与单位之间 gap-1
    private const int PillGap = 8;      // 胶囊之间 gap-2
    private const int WinBtnW = 44;     // w-11
    private const int StepW = 16;       // ▲▼ 步进列宽度
    private const int ChevronW = 22;    // 日期字段的 ▾ 列宽度

    // ══ 热区 id ══════════════════════════════════════════
    private const string IdClose = "close";
    private const string IdSegCd = "segCd", IdSegAt = "segAt";
    private const string IdAction = "action", IdCancel = "cancel", IdSettings = "settings";

    // ══ 文案 ═════════════════════════════════════════════
    private static readonly string[] PillTexts = ["30分", "1小时", "2小时", "4小时"];
    private static readonly (int H, int M)[] PillValues = [(0, 30), (1, 0), (2, 0), (4, 0)];
    private static readonly string[] NudgeTexts = ["+30分", "+1小时", "+3小时"];
    private static readonly int[] NudgeSeconds = [1800, 3600, 10800];
    private const string FooterTop = "启动后可随时点「取消关机」停止。";
    // 页脚第二行跟着护航开关走:护航开着还说"睡眠会暂停计时"就是谎报
    private string FooterText => _settings.KeepAwakeWhileTiming
        ? FooterTop + "\n计时期间已阻止系统自动睡眠,屏幕照常按电源计划省电。"
        : FooterTop + "\n未开「保持系统唤醒」时,电脑睡眠会暂停计时。";
    private const string CancelLabel = "取消关机";

    /// <summary>步进字段:点右侧 ▲▼ 增减(按住连发),也可滚轮与方向键。日期字段改为点 ▾ 弹月历。</summary>
    private sealed class NumField
    {
        public int Val;
        public int Max = 59;
        public int BoxW = 52;       // 数值区逻辑宽度下限,实际按内容加宽
        public string Label = "";
        public bool Pad = true;     // 两位数补零
        public bool IsDate;         // 日期字段:值存在 _atDate,步进按天
        public Rectangle Box, LabelBox, Up, Down, Chevron;
        public int TextW;
    }

    private readonly NumField[] _cd, _at;
    private DateTime _atDate;
    private NumField? _focused;
    private NumField? _hoverStep;
    private int _hoverStepDir;
    private readonly System.Windows.Forms.Timer _stepTick;
    private readonly System.Windows.Forms.Timer _topHold;
    private NumField? _repeatField;
    private int _repeatDir;
    private CalendarForm? _calendar;

    // ══ 布局结果 ═════════════════════════════════════════
    private Rectangle _rTitle, _rClose, _rGear, _rIcon, _rStatus, _rSeg, _rSegL, _rSegR;
    private Rectangle _rAction, _rCancel, _rFooter;
    private readonly Rectangle[] _pillRects = new Rectangle[4];
    private readonly Rectangle[] _nudgeRects = new Rectangle[3];
    private readonly Rectangle[] _rTiles = new Rectangle[4];
    private Rectangle _rCreate;                          // 创建区(分段/字段/胶囊/磁贴):任务进行中整体锁定
    private int _clientW, _segTextW0, _segTextW1, _fieldTextH, _unitTextH;

    // ══ 运行时状态 ═══════════════════════════════════════
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _tick;
    private readonly Dictionary<string, Action> _actions = [];
    private readonly List<(Rectangle R, string Id)> _hits = [];
    private string _hoverId = "", _mode = "countdown";
    private DateTime? _target;
    private bool _reallyExit, _acrylic;
    private string? _shutdownError;   // 到点但动作命令失败,别再谎报"正在关机"
    private bool _atDirty;   // 用户手动改过定时任务时间后就不再自动刷新默认值
    private bool _startMinOn;
    private readonly AppSettings _settings;
    private string _page = "main";                       // main | settings(设置集成进主面板,不再单开窗口)
    private bool _paused;                                // 任务暂停:剩余时间冻结
    private TimeSpan _pausedRemain;
    private bool _warnFired;          // 本次任务的提醒点已触发(只提醒一次)
    private bool _warnEditActive;     // 设置页提醒分钟正在编辑
    private string _warnEditBuf = "";
    private string? _flashMsg; private DateTime _flashUntil; private Color _flashColor = FluentTheme.Accent;
    private string _action = ActionExecutor.Shutdown;   // 四选一动作
    private bool _dryRun;                                // 测试开关
    private string? _lastIconKey;                        // 托盘图标变化检测
    private int _totalSeconds;                           // 本次任务总时长(进度环用)
    private float _scale;
    private Font _fTitle, _fStatus, _fSeg, _fField, _fUnit, _fPill, _fAction, _fLabel, _fFooter;

    private static readonly StringFormat FmtCenter = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
    private static readonly StringFormat FmtLeft = new() { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
    private static readonly StringFormat FmtTop = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Near };

    private int Px(float v) => (int)Math.Round(v * _scale);
    private int Radius(int logical) => Math.Max(1, Px(logical));
    private Color GlassBase() => _acrylic ? FluentTheme.Glass : FluentTheme.BgFallback;
    private NumField[] VisibleFields() => _mode == "countdown" ? _cd : _at;
    private string FieldText(NumField f) => f.IsDate ? _atDate.ToString("yyyy/MM/dd") : f.Pad ? f.Val.ToString("00") : f.Val.ToString();

    // ══ 注册表/设置迁至 AppSettings.cs(v4 ①):ini 持久化 + Run 项封装 ──

    // ══ Windows 关机 API 已集中至 PowerApis.cs(v4 ②③),动作执行走 ActionExecutor ──

    /// <summary>托盘星星:内嵌 app.ico(粉色);取不到就退回系统盾牌。实例持有,统一 Dispose。</summary>
    private Icon? _baseIcon;
    private Icon LoadStarIcon()
    {
        try
        {
            using var s = typeof(PanelForm).Assembly.GetManifestResourceStream("ShutdownTimer.app.ico");
            if (s != null) return new Icon(s, SystemInformation.SmallIconSize);
        }
        catch { /* 资源缺失或被占用 */ }
        return SystemIcons.Shield;
    }

    /// <summary>标题栏用的星星位图。取 64px 那一档再缩小,比直接放大小图清晰。</summary>
    private static readonly Bitmap? AppImage = LoadAppImage();

    private static Bitmap? LoadAppImage()
    {
        try
        {
            using var s = typeof(PanelForm).Assembly.GetManifestResourceStream("ShutdownTimer.app.ico");
            if (s == null) return null;
            using var ico = new Icon(s, new Size(64, 64));
            return ico.ToBitmap();
        }
        catch { return null; }
    }

    // ══ 无边框窗口拖动 ═══════════════════════════════════
    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 0x2;
    private const int WM_DWMCOMPOSITIONCHANGED = 0x031E;

    // ══ 单实例唤醒 ═══════════════════════════════════════
    private delegate bool EnumWindowsProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    private static readonly uint WM_SHOW_PANEL = RegisterWindowMessage("ShutdownTimer.ShowPanel.v1");

    /// <summary>第二个实例被双击起来时,让已经躲在托盘里的实例把面板弹出来。</summary>
    public static void WakeExistingInstance()
    {
        var pids = new HashSet<uint>();
        var me = (uint)Environment.ProcessId;
        foreach (var p in System.Diagnostics.Process.GetProcessesByName("ShutdownTimer"))
        {
            try { if ((uint)p.Id != me) pids.Add((uint)p.Id); } catch { }
        }
        Logger.Info($"Wake: 其他实例 pid=[{string.Join(",", pids)}]");
        if (pids.Count == 0) return;
        var targets = new List<IntPtr>();
        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var pid);
            if (pids.Contains(pid)) targets.Add(h);
            return true;
        }, IntPtr.Zero);
        Logger.Info($"Wake: 枚举到目标窗口 {targets.Count} 个");
        foreach (var h in targets)
        {
            PostMessage(h, WM_SHOW_PANEL, IntPtr.Zero, IntPtr.Zero);
            Logger.Info($"Wake: 已向 0x{h:X} 投递 WM_SHOW_PANEL");
        }
        // 本进程是 shell 直接拉起的,前台权限在我们手上,趁退出前递给对面那个窗口
        Thread.Sleep(250);
        foreach (var h in targets) SetForegroundWindow(h);
    }

    public ApplicationContext Context { get; }

    // ═════════════════════════════════════════════════════
    /// <param name="fromAutoStart">由注册表 Run 项在开机时拉起:这种启动一律不弹面板。</param>
    /// <param name="settings">v4 设置中枢(Program 阶段加载,含便携路径决策)。</param>
    public PanelForm(bool fromAutoStart, AppSettings settings, bool dryRun)
    {
        _settings = settings;
        _dryRun = dryRun;
        _action = settings.LastAction;
        _scale = DeviceDpi / 96f;
        (_fTitle, _fStatus, _fSeg, _fField, _fUnit, _fPill, _fAction, _fLabel, _fFooter) = BuildFonts();

        _cd = [
            new NumField { Val = 1, Max = 99, Label = "时", Pad = false },
            new NumField { Val = 0, Max = 59, Label = "分", Pad = false },
        ];
        _at = [
            new NumField { IsDate = true, BoxW = 96, Label = "日期" },
            new NumField { Max = 23, Label = "时" },
            new NumField { Max = 59, Label = "分" },
        ];
        RefreshAtDefault();

        Text = "定时关机助手";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;   // 自己按 DeviceDpi 缩放,避免二次缩放
        BackColor = FluentTheme.BgFallback;
        Font = new Font("Segoe UI", 9F);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);

        _actions[IdClose] = Hide;
        _actions[IdSegCd] = () => SwitchMode("countdown");
        _actions[IdSegAt] = () => SwitchMode("at");
        _actions[IdAction] = () =>
        {
            if (_paused) { ResumeTask(); return; }
            if (_target != null) { PauseTask(); return; }
            StartCurrentMode();
        };
        _actions[IdCancel] = CancelPending;
        _actions[IdSettings] = () => SwitchPage("settings");
        for (int i = 0; i < ActionExecutor.All.Length; i++)
        {
            string a = ActionExecutor.All[i];
            _actions["act" + i] = () => PickAction(a);
        }
        for (int i = 0; i < PillValues.Length; i++)
        {
            var (h, m) = PillValues[i];
            _actions["preset" + i] = () => Preset(h, m);
        }
        for (int i = 0; i < NudgeSeconds.Length; i++)
        {
            int secs = NudgeSeconds[i];
            _actions["nudge" + i] = () => NudgeAt(TimeSpan.FromSeconds(secs));
        }

        _tick = new System.Windows.Forms.Timer { Interval = 500 };
        _tick.Tick += (_, _) => OnTick();
        // 步进按钮按住连发:先 400ms 起跳,之后每 60ms 一步。
        // 光标一旦离开该箭头就停,否则释放事件丢失时数值会一直涨。
        _stepTick = new System.Windows.Forms.Timer { Interval = 400 };
        _stepTick.Tick += (_, _) =>
        {
            if (_repeatField == null) { _stepTick.Stop(); return; }
            var rect = _repeatDir > 0 ? _repeatField.Up : _repeatField.Down;
            if (!rect.Contains(PointToClient(Cursor.Position))) { StopRepeat(); return; }
            if (_stepTick.Interval != 60) _stepTick.Interval = 60;
            StepField(_repeatField, _repeatDir);
        };
        _topHold = new System.Windows.Forms.Timer { Interval = 300 };
        _topHold.Tick += (_, _) => { _topHold.Stop(); TopMost = false; };

        _baseIcon = LoadStarIcon();
        _tray = new NotifyIcon { Icon = _baseIcon, Text = "定时关机助手", Visible = true };
        var trayMenu = new ContextMenuStrip();
        // 无限护航:不定时长挂机,托盘一键开关(会话级,不跨重启)
        var indefiniteItem = new ToolStripMenuItem("无限护航(系统保持唤醒)") { CheckOnClick = true };
        indefiniteItem.CheckedChanged += (_, _) =>
        {
            _indefiniteGuard = indefiniteItem.Checked;
            RefreshGuard();
            if (_indefiniteGuard) FlashStatus("无限护航开启 · 系统不会自动睡眠", Color.FromArgb(0x66, 0x80, 0x0B));
            Logger.Info($"无限护航: {(_indefiniteGuard ? "开" : "关")}");
        };
        trayMenu.Items.Add(indefiniteItem);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("打开面板", null, (_, _) => ShowPanel());
        trayMenu.Items.Add("取消已设定的关机", null, (_, _) => CancelPending());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("退出", null, (_, _) => ReallyExit());
        _tray.ContextMenuStrip = trayMenu;
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowPanel();
        };

        FormClosing += (_, e) =>
        {
            if (_reallyExit) return;
            e.Cancel = true;
            Hide();
        };

        _startMinOn = settings.StartMinimized;
        // 别把 this 交给 ApplicationContext.MainForm:它一见到不可见的 MainForm 就自己 Show 出来
        Context = new ApplicationContext();
        if (fromAutoStart && _startMinOn)
            _ = Handle;   // 躲托盘时先建好句柄,双击 exe 的第二个实例才有地方发消息唤醒;CreateControl 在构造函数里是空操作
        else
            Show();
    }

    private (Font, Font, Font, Font, Font, Font, Font, Font, Font) BuildFonts() => (
        SegFont(11, true), SegFont(19, true), SegFont(12.5f, true), SegFont(15, false),
        SegFont(11.5f, false), SegFont(11, false), SegFont(13.5f, true), SegFont(12, false), SegFont(10, false));

    private Font SegFont(float logicalPx, bool bold)
        => new("Segoe UI", logicalPx * _scale, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);

    // ══ 材质与缩放 ═══════════════════════════════════════
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _acrylic = FluentTheme.ApplyAcrylic(this);
        if (!_acrylic) FluentTheme.ClearBackdrop(this);
        FluentTheme.ApplyRoundedCorners(this);

        var scale = DeviceDpi / 96f;
        if (Math.Abs(scale - _scale) > 0.001f) { _scale = scale; SwapFonts(); }
        RebuildLayout();
        _tick.Start();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        _scale = e.DeviceDpiNew / 96f;
        SwapFonts();
        RebuildLayout();
    }

    private void SwapFonts()
    {
        var old = new[] { _fTitle, _fStatus, _fSeg, _fField, _fUnit, _fPill, _fAction, _fLabel, _fFooter };
        (_fTitle, _fStatus, _fSeg, _fField, _fUnit, _fPill, _fAction, _fLabel, _fFooter) = BuildFonts();
        foreach (var f in old) f.Dispose();
    }

    // ══ 布局:按设计稿纵向流式排布 ═════════════════════════
    private void RebuildLayout()
    {
        if (_page == "settings") { LayoutSettings(); _hits.Clear(); return; }   // 设置页:布局与命中独立
        bool cd = _mode == "countdown";
        _fieldTextH = Measure("0", _fField).Height;
        _unitTextH = Measure("时", _fUnit).Height;
        int pad = Px(PadX);
        _clientW = Px(BaseW);
        int contentW = _clientW - pad * 2;
        int y = Px(TitleH + Pt);

        _rTitle = new Rectangle(0, 0, _clientW, Px(TitleH));
        _rIcon = new Rectangle(Px(12), Px(12), Px(16), Px(16));
        _rGear = new Rectangle(_clientW - Px(WinBtnW * 2), 0, Px(WinBtnW), Px(TitleH));
        _rClose = new Rectangle(_clientW - Px(WinBtnW), 0, Px(WinBtnW), Px(TitleH));

        _rStatus = new Rectangle(0, y, _clientW, Px(StatusH));
        y += _rStatus.Height + Px(Gap);

        _rSeg = new Rectangle(pad, y, contentW, Px(SegH));
        int segPad = Px(4), innerW = _rSeg.Width - segPad * 2;
        _rSegL = new Rectangle(_rSeg.X + segPad, _rSeg.Y + segPad, innerW / 2, _rSeg.Height - segPad * 2);
        _rSegR = new Rectangle(_rSeg.X + segPad + innerW / 2, _rSeg.Y + segPad, innerW - innerW / 2, _rSeg.Height - segPad * 2);
        y = _rSeg.Bottom + Px(Gap) + Px(Mt2);

        // 两个模式都是"一行字段 + 一行胶囊",窗口高度因此恒定,切换标签不再变大变小
        LayoutFieldRow(VisibleFields(), pad, contentW, y);
        y += Px(FieldH) + Px(Gap) + Px(Mt1);
        if (cd) LayoutPills(_pillRects, PillTexts, pad, contentW, y);
        else LayoutPills(_nudgeRects, NudgeTexts, pad, contentW, y);
        y += Px(PillH) + Px(Gap) + Px(Mt2);

        // 动作磁贴(四选一):Flexoki 语义色,选中色底纸白字
        int tileGap = Px(8), tileH = Px(46);
        int tileW = (contentW - tileGap * 3) / 4;
        for (int i = 0; i < 4; i++)
            _rTiles[i] = new Rectangle(pad + i * (tileW + tileGap), y, tileW, tileH);
        _rCreate = new Rectangle(pad, _rSeg.Y, contentW, _rTiles[3].Bottom - _rSeg.Y);
        y += tileH + Px(Gap) + Px(Mt2);

        // 启动按钮收窄,取消按钮常驻同一行右侧;未设定时置灰不可点,所以状态切换也不会改变窗口高度
        int cancelW = Px(CancelW), btnGap = Px(12);
        _rAction = new Rectangle(pad, y, contentW - cancelW - btnGap, Px(ActionH));
        _rCancel = new Rectangle(_rAction.Right + btnGap, y, cancelW, Px(ActionH));
        y += _rAction.Height + Px(Gap);

        _rFooter = new Rectangle(0, y, _clientW, Px(FooterH));
        y += _rFooter.Height + Px(Pb);

        ClientSize = new Size(_clientW, y);

        _segTextW0 = Measure("倒计时", _fSeg).Width;
        _segTextW1 = Measure("定时任务", _fSeg).Width;
        RegisterHits();
    }

    /// <summary>一行字段:[数值区][▲▼ 或 ▾] 单位标签,整组居中</summary>
    private void LayoutFieldRow(NumField[] fields, int pad, int contentW, int y)
    {
        int h = Px(FieldH);
        int total = Px(GroupGap) * (fields.Length - 1);
        foreach (var f in fields)
        {
            f.TextW = Measure(FieldText(f), _fField).Width;
            f.Box = new Rectangle(0, 0, Math.Max(Px(f.BoxW), f.TextW + Px(18)), h);
            total += f.Box.Width + Px(f.IsDate ? ChevronW : StepW) + Px(LabelGap) + Measure(f.Label, _fUnit).Width;
        }
        int x = pad + Math.Max(0, (contentW - total) / 2);
        foreach (var f in fields)
        {
            int colW = f.IsDate ? Px(ChevronW) : Px(StepW);
            int colX = x + f.Box.Width;
            f.Box = new Rectangle(x, y, f.Box.Width, h);
            f.Up = f.IsDate ? Rectangle.Empty : new Rectangle(colX, y, colW, h / 2);
            f.Down = f.IsDate ? Rectangle.Empty : new Rectangle(colX, y + h / 2, colW, h - h / 2);
            f.Chevron = f.IsDate ? new Rectangle(colX, y, colW, h) : Rectangle.Empty;
            x += f.Box.Width + colW + Px(LabelGap);
            int lw = Measure(f.Label, _fUnit).Width;
            f.LabelBox = new Rectangle(x, y + h - _unitTextH - Px(4), lw + Px(4), _unitTextH);
            x += f.LabelBox.Width + Px(GroupGap);
        }
    }

    private void LayoutPills(Rectangle[] target, string[] texts, int pad, int contentW, int y)
    {
        int gap = Px(PillGap), total = gap * (texts.Length - 1);
        var widths = new int[texts.Length];
        for (int i = 0; i < texts.Length; i++)
        {
            widths[i] = Measure(texts[i], _fPill).Width + Px(32) + Px(2);
            total += widths[i];
        }
        int x = pad + Math.Max(0, (contentW - total) / 2);
        for (int i = 0; i < texts.Length; i++)
        {
            target[i] = new Rectangle(x, y, widths[i], Px(PillH));
            x += target[i].Width + gap;
        }
    }

    private Size Measure(string s, Font f)
    {
        using var g = CreateGraphics();
        return Size.Ceiling(g.MeasureString(s, f, new PointF(0, 0), FmtLeft));
    }

    /// <summary>任务进行中:创建区整体锁定,只能暂停/取消;同一条件也决定"取消"按钮是否可用。</summary>
    private bool Locked => _target != null;

    /// <summary>锁定时未选中的创建控件减半透明度;选中项保持原样,好让人看清"现在 armed 的是哪个"。</summary>
    private Color Mute(Color c) => Locked ? Color.FromArgb(c.A / 2, c.R, c.G, c.B) : c;

    private void RegisterHits()
    {
        _hits.Clear();
        _hits.Add((_rClose, IdClose));
        _hits.Add((_rGear, IdSettings));
        _hits.Add((_rAction, IdAction));
        if (Locked) _hits.Add((_rCancel, IdCancel));
        if (Locked) return;
        _hits.Add((_rSegL, IdSegCd)); _hits.Add((_rSegR, IdSegAt));
        for (int i = 0; i < 4; i++) _hits.Add((_rTiles[i], "act" + i));
        if (_mode == "countdown")
            for (int i = 0; i < PillTexts.Length; i++) _hits.Add((_pillRects[i], "preset" + i));
        else
            for (int i = 0; i < NudgeTexts.Length; i++) _hits.Add((_nudgeRects[i], "nudge" + i));
    }

    // ══ 绘制 ═════════════════════════════════════════════
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;   // 主页压在亚克力上,ClearType 会串色
        if (_page == "settings") { DrawSettingsPage(g); return; }   // 设置页不透明,自己换成 ClearType

        if (_acrylic) g.Clear(Color.Transparent);
        else g.Clear(FluentTheme.BgFallback);
        Fill(g, new Rectangle(0, 0, _clientW, ClientSize.Height), GlassBase());

        DrawTitleBar(g);
        var statusColor = _flashMsg != null && DateTime.Now < _flashUntil ? _flashColor : FluentTheme.TextPrimary;
        DrawStr(g, StatusText(), _fStatus, statusColor, _rStatus, FmtCenter);
        DrawSegment(g);
        foreach (var f in VisibleFields()) DrawField(g, f);
        if (_mode == "countdown") DrawPills(g, PillTexts, _pillRects, "preset");
        else DrawPills(g, NudgeTexts, _nudgeRects, "nudge");
        DrawTiles(g);
        DrawAction(g);
        DrawCancel(g);
        DrawStr(g, FooterText, _fFooter, FluentTheme.TextSecondary, _rFooter, FmtTop);
        DrawChrome(g);
    }

    private void Fill(Graphics g, Rectangle r, Color c)
    {
        using var b = new SolidBrush(c);
        g.FillRectangle(b, r);
    }

    private void FillPath(Graphics g, GraphicsPath p, Color c)
    {
        using var b = new SolidBrush(c);
        g.FillPath(b, p);
    }

    private void DrawStr(Graphics g, string s, Font f, Color c, RectangleF r, StringFormat fmt)
    {
        using var b = new SolidBrush(c);
        g.DrawString(s, f, b, r, fmt);
    }

    private void DrawTitleBar(Graphics g)
    {
        DrawAppIcon(g);
        DrawStr(g, "定时关机助手", _fTitle, FluentTheme.TextTitle,
            new RectangleF(_rIcon.Right + Px(8), _rTitle.Y, Px(180), _rTitle.Height), FmtLeft);
        DrawGear(g, _rGear);
        DrawTitleButton(g, _rClose, "collapse");
    }

    /// <summary>设置入口图标:滑杆式三条横线各带圆点,自绘免 emoji。</summary>
    private void DrawGear(Graphics g, Rectangle r)
    {
        bool hover = _hoverId == IdSettings;
        if (hover) Fill(g, r, Color.FromArgb(24, 0, 0, 0));
        using var pen = new Pen(FluentTheme.TextTitle, Math.Max(1f, 1.4f * _scale));
        pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round;
        float[] knob = [0.62f, 0.38f, 0.55f];
        using var kb = new SolidBrush(FluentTheme.TextTitle);
        for (int i = 0; i < 3; i++)
        {
            float y = r.Y + r.Height * (0.3f + i * 0.2f);
            float x1 = r.X + r.Width * 0.28f, x2 = r.X + r.Width * 0.72f;
            g.DrawLine(pen, x1, y, x2, y);
            float kx = r.X + r.Width * knob[i];
            g.FillEllipse(kb, kx - Px(2.2f), y - Px(2.2f), Px(4.4f), Px(4.4f));
        }
    }

    /// <summary>标题栏右侧按钮(防误导):back=← 返回上一页;collapse=⌄ 收进托盘(不是退出)。</summary>
    private void DrawTitleButton(Graphics g, Rectangle r, string kind)
    {
        string id = kind == "back" ? IdClose : IdClose;
        bool hover = _hoverId == id;
        if (hover) Fill(g, r, Color.FromArgb(28, 0, 0, 0));
        using var pen = new Pen(hover ? FluentTheme.TextPrimary : FluentTheme.TextTitle, Math.Max(1f, 1.5f * _scale));
        pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
        var c = new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f);
        float w = Px(5), h = Px(4);
        if (kind == "back")
        {
            g.DrawLine(pen, c.X + w, c.Y - h, c.X - w, c.Y);   // ←
            g.DrawLine(pen, c.X - w, c.Y, c.X + w, c.Y + h);
        }
        else
        {
            float d = Px(4.5f);   // ×:收进托盘(用户确认保留 × 样式)
            g.DrawLine(pen, c.X - d, c.Y - d, c.X + d, c.Y + d);
            g.DrawLine(pen, c.X + d, c.Y - d, c.X - d, c.Y + d);
        }
    }

    private void DrawAppIcon(Graphics g)
    {
        if (AppImage == null) return;
        var prev = g.InterpolationMode;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(AppImage, _rIcon);
        g.InterpolationMode = prev;
    }

    private static void DrawClockGlyph(Graphics g, Rectangle r, Color c)
    {
        float w = Math.Max(1f, r.Width * 0.085f);
        using var pen = new Pen(c, w);
        var ring = RectangleF.Inflate(r, -w / 2, -w / 2);
        g.DrawEllipse(pen, ring);
        var ctr = new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f);
        g.DrawLine(pen, ctr, new PointF(ctr.X, ctr.Y - ring.Height * 0.28f));
        g.DrawLine(pen, ctr, new PointF(ctr.X + ring.Width * 0.24f, ctr.Y));
    }

    private void DrawSegment(Graphics g)
    {
        using (var path = FluentTheme.RoundedPath(_rSeg, Radius(8)))
            FillPath(g, path, FluentTheme.Card);
        DrawSegItem(g, _rSegL, "倒计时", _mode == "countdown", IdSegCd, _segTextW0, 0);
        DrawSegItem(g, _rSegR, "定时任务", _mode == "at", IdSegAt, _segTextW1, 1);
    }

    /// <summary>分段项:图标与文字作为一个整体居中,替代设计稿里的 ⏱ / 表情(后者会渲染成豆腐块)</summary>
    private void DrawSegItem(Graphics g, Rectangle r, string text, bool active, string id, int textW, int icon)
    {
        using var path = FluentTheme.RoundedPath(r, Radius(6));
        if (active) FillPath(g, path, Color.White);
        else if (_hoverId == id) FillPath(g, path, FluentTheme.Over(FluentTheme.Card, Color.FromArgb(128, 255, 255, 255)));
        var color = active ? FluentTheme.TextPrimary : Mute(FluentTheme.TextMuted);
        int iw = Px(16), gap = Px(6);
        int x = r.X + (r.Width - (iw + gap + textW)) / 2;
        var ir = new Rectangle(x, r.Y + (r.Height - iw) / 2, iw, iw);
        if (icon == 0)
        {
            using var p = SvgPath.Build(Icons.Hourglass, ir);
            FillPath(g, p, color);
        }
        else DrawClockGlyph(g, ir, color);
        DrawStr(g, text, _fSeg, color, new RectangleF(x + iw + gap, r.Y, textW + Px(6), r.Height), FmtLeft);
    }

    /// <summary>字段:数值区 + 右侧步进列(日期字段为 ▾ 列),整体半透明白底、只圆上两角、2px 底边</summary>
    private void DrawField(Graphics g, NumField f)
    {
        var r = f.Box;
        bool focused = ReferenceEquals(_focused, f);
        int ctrlW = (f.IsDate ? f.Chevron.Right : f.Down.Right) - r.X;
        using (var path = FluentTheme.TopRounded(new Rectangle(r.X, r.Y, ctrlW, r.Height - Px(2)), Radius(6)))
            FillPath(g, path, FluentTheme.Over(GlassBase(), Color.FromArgb(focused ? 204 : Locked ? 64 : 128, 255, 255, 255)));

        if (f.IsDate) DrawChevronBtn(g, f);
        else
        {
            DrawStepBtn(g, f.Up, StepHot(f, 1), 1, Mute(FluentTheme.TextMuted));
            DrawStepBtn(g, f.Down, StepHot(f, -1), -1, Mute(FluentTheme.TextMuted));
        }

        using (var pen = new Pen(Mute(FluentTheme.FieldUnderline), Math.Max(1f, _scale)))
            g.DrawLine(pen, r.Right, r.Y + 1, r.Right, r.Bottom - Px(3));
        Fill(g, new Rectangle(r.X, r.Bottom - Px(2), ctrlW, Px(2)),
            focused ? FluentTheme.Accent : Mute(FluentTheme.FieldUnderline));

        int x = r.X + (r.Width - f.TextW) / 2;
        int baselineY = r.Y + (r.Height - Px(2) - _fieldTextH) / 2;
        DrawStr(g, FieldText(f), _fField, Mute(Color.Black),
            new RectangleF(x, baselineY, f.TextW + 1, _fieldTextH), FmtLeft);

        DrawStr(g, f.Label, _fUnit, Mute(FluentTheme.TextMuted), f.LabelBox, FmtCenter);
    }

    private bool StepHot(NumField f, int dir) => ReferenceEquals(_hoverStep, f) && _hoverStepDir == dir;

    private void DrawStepBtn(Graphics g, Rectangle r, bool hot, int dir, Color ink)
    {
        if (hot) Fill(g, r, FluentTheme.Over(GlassBase(), Color.FromArgb(217, 255, 255, 255)));
        var c = new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f);
        float w = Px(3.5f), h = Px(2.2f);
        using var b = new SolidBrush(hot ? FluentTheme.Accent : ink);
        PointF[] tri = dir > 0
            ? [new(c.X, c.Y - h), new(c.X - w, c.Y + h), new(c.X + w, c.Y + h)]
            : [new(c.X, c.Y + h), new(c.X - w, c.Y - h), new(c.X + w, c.Y - h)];
        g.FillPolygon(b, tri);
    }

    private void DrawChevronBtn(Graphics g, NumField f)
    {
        var r = f.Chevron;
        if (ReferenceEquals(_hoverStep, f))
            Fill(g, r, FluentTheme.Over(GlassBase(), Color.FromArgb(217, 255, 255, 255)));
        using var pen = new Pen(FluentTheme.TextMuted, Math.Max(1f, 1.4f * _scale));
        pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
        var c = new PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f);
        float w = Px(4f), h = Px(2.5f);
        g.DrawLines(pen, [new PointF(c.X - w, c.Y - h), new PointF(c.X, c.Y + h), new PointF(c.X + w, c.Y - h)]);
    }

    private void DrawPills(Graphics g, string[] texts, Rectangle[] rects, string idPrefix)
    {
        for (int i = 0; i < texts.Length; i++)
        {
            var r = rects[i];
            bool hover = _hoverId == idPrefix + i;
            using var path = FluentTheme.RoundedPath(r, r.Height / 2);
            FillPath(g, path, hover ? FluentTheme.PillHover : Mute(FluentTheme.Pill));
            using var pen = new Pen(Mute(FluentTheme.PillBorder), Math.Max(1f, _scale));
            g.DrawPath(pen, path);
            DrawStr(g, texts[i], _fPill, Mute(FluentTheme.TextTitle), r, FmtCenter);
        }
    }

    // ── 动作磁贴──
    private static readonly string[] TileDisplay = ["锁定", "睡眠", "关机", "重启"];
    private static readonly Dictionary<string, Color> ActionColor = new()
    {
        [ActionExecutor.Lock] = Color.FromArgb(0x24, 0x83, 0x7B),          // Cyan 600
        [ActionExecutor.Sleep] = Color.FromArgb(0x20, 0x5E, 0xA6),         // Blue 600
        [ActionExecutor.Shutdown] = Color.FromArgb(0xAF, 0x30, 0x29),      // Red 600
        [ActionExecutor.Reboot] = Color.FromArgb(0xBC, 0x52, 0x15),        // Orange 600
    };

    private void DrawTiles(Graphics g)
    {
        for (int i = 0; i < 4; i++)
        {
            var r = _rTiles[i];
            string name = ActionExecutor.All[i];
            bool sel = _action == name;
            bool hover = _hoverId == "act" + i;
            using var path = FluentTheme.RoundedPath(r, Px(10));
            if (sel)
            {
                using var b = new SolidBrush(ActionColor[name]);
                g.FillPath(b, path);
                using var shadow = new SolidBrush(Color.FromArgb(40, 16, 15, 15));
                using var shadowPath = FluentTheme.RoundedPath(new Rectangle(r.X, r.Y + Px(3), r.Width, r.Height), Px(10));
                g.FillPath(shadow, shadowPath);
            }
            else
            {
                using var b = new SolidBrush(Mute(hover ? Color.FromArgb(233, 225, 211) : Color.FromArgb(255, 252, 240)));
                g.FillPath(b, path);
                using var pen = new Pen(Mute(FluentTheme.FieldUnderline), Math.Max(1f, _scale));
                g.DrawPath(pen, path);
            }
            var fg = sel ? Color.FromArgb(255, 252, 240) : Mute(hover ? FluentTheme.TextPrimary : FluentTheme.TextSecondary);
            var iconR = new Rectangle(r.X, r.Y + Px(7), r.Width, Px(16));
            DrawActionIcon(g, iconR, i, fg);
            DrawStr(g, TileDisplay[i], _fPill, fg, new RectangleF(r.X, r.Y + Px(24), r.Width, Px(18)), FmtCenter);
        }
    }

    /// <summary>动作小图标:每个几笔几何线,免 emoji 免资源。</summary>
    private void DrawActionIcon(Graphics g, Rectangle r, int idx, Color c)
    {
        using var pen = new Pen(c, Math.Max(1.4f, 1.6f * _scale));
        using var fill = new SolidBrush(c);
        pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
        float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, rad = r.Height * 0.42f;
        switch (idx)
        {
            case 0: // 锁定:锁体 + 锁梁
                float bw = rad * 1.5f, bh = rad * 1.15f;
                using (var body = FluentTheme.RoundedPath(new Rectangle((int)Math.Round(cx - bw / 2), (int)Math.Round(cy - bh * 0.1f), (int)Math.Round(bw), (int)Math.Round(bh)), Px(3)))
                    g.FillPath(fill, body);
                g.DrawArc(pen, cx - rad * 0.55f, cy - rad * 0.85f, rad * 1.1f, rad * 1.1f, 180, 180);
                break;
            case 1: // 睡眠:月牙(大圆挖偏移小圆)
                using (var moon = new System.Drawing.Drawing2D.GraphicsPath { FillMode = System.Drawing.Drawing2D.FillMode.Alternate })
                {
                    moon.AddEllipse(cx - rad, cy - rad, rad * 2, rad * 2);
                    moon.AddEllipse(cx - rad * 0.15f, cy - rad * 0.8f, rad * 1.7f, rad * 1.7f);
                    g.FillPath(fill, moon);
                }
                break;
            case 2: // 关机:圆弧 + 顶部竖线
                g.DrawArc(pen, cx - rad, cy - rad + r.Height * 0.1f, rad * 2, rad * 2, -60, 300);
                g.DrawLine(pen, cx, cy - rad + r.Height * 0.1f - Px(1), cx, cy - Px(1));
                break;
            case 3: // 重启:圆环留 70° 缺口朝右上,箭头骑在弧末端指向切线方向
                const float gapDeg = 70f, startDeg = -10f;
                g.DrawArc(pen, cx - rad, cy - rad, rad * 2, rad * 2, startDeg, 360f - gapDeg);
                float endRad = (startDeg + 360f - gapDeg) * MathF.PI / 180f;
                float ex = cx + rad * MathF.Cos(endRad), ey = cy + rad * MathF.Sin(endRad);
                float ax = -MathF.Sin(endRad), ay = MathF.Cos(endRad);   // 顺时针切线
                float rx = MathF.Cos(endRad), ry = MathF.Sin(endRad);    // 径向
                float hl = Px(6.5f), hw = Px(3.6f);
                g.FillPolygon(fill, new[]
                {
                    new PointF(ex + ax * hl, ey + ay * hl),
                    new PointF(ex + rx * hw, ey + ry * hw),
                    new PointF(ex - rx * hw, ey - ry * hw),
                });
                break;
        }
    }

    private void PickAction(string a)
    {
        if (_action == a) return;
        _action = a;
        _settings.LastAction = a;
        _settings.Save();
        Invalidate();
        Logger.Info($"切换动作: {a}");
    }

    private void DrawAction(Graphics g)
    {
        bool pressed = _hoverId == IdAction && MouseButtons == MouseButtons.Left;
        // 三态:未设定=启动;运行中=暂停;暂停中=继续
        Color back, fore = Color.White;
        string text;
        if (_paused) { back = Color.FromArgb(0x66, 0x80, 0x0B); text = "继续"; }
        else if (_target != null) { back = FluentTheme.Over(GlassBase(), Color.FromArgb(140, 255, 255, 255)); fore = FluentTheme.TextPrimary; text = "暂停"; }
        else { back = pressed ? ActionColorDown(_action) : hoverAction ? ActionColorHover(_action) : ActionColor[_action]; text = (_mode == "countdown" ? "启动倒计时 · " : "启动定时任务 · ") + _action; }

        using var path = FluentTheme.RoundedPath(_rAction, Radius(8));
        FillPath(g, path, back);
        DrawStr(g, text, _fAction, fore, _rAction, FmtCenter);
    }

    private bool hoverAction => _hoverId == IdAction;
    private static Color ActionColorHover(string a) => ControlPaint.Light(ActionColor[a]);
    private static Color ActionColorDown(string a) => ControlPaint.Dark(ActionColor[a]);

    private void DrawCancel(Graphics g)
    {
        using var path = FluentTheme.RoundedPath(_rCancel, Radius(8));
        if (!Locked)
        {
            // 未设定关机:常驻但灰暗,热区也没注册,所以点不动
            FillPath(g, path, FluentTheme.Over(GlassBase(), Color.FromArgb(18, 0, 0, 0)));
            using var idlePen = new Pen(FluentTheme.Over(GlassBase(), Color.FromArgb(43, 0, 0, 0)), Math.Max(1f, _scale));
            g.DrawPath(idlePen, path);
            DrawStr(g, CancelLabel, _fSeg, Color.FromArgb(151, 155, 161), _rCancel, FmtCenter);
            return;
        }
        if (_hoverId == IdCancel) FillPath(g, path, FluentTheme.DangerHover);
        using var pen = new Pen(FluentTheme.DangerBorder, Math.Max(1f, _scale));
        g.DrawPath(pen, path);
        DrawStr(g, CancelLabel, _fSeg, FluentTheme.Danger, _rCancel, FmtCenter);
    }

    private void DrawChrome(Graphics g)
    {
        using var hp = new Pen(FluentTheme.Hairline, Math.Max(1f, _scale));
        g.DrawRectangle(hp, 0, 0, _clientW - 1, ClientSize.Height - 1);
        Fill(g, new Rectangle(0, 0, _clientW, Math.Max(1, Px(1))), FluentTheme.TopHighlight);
    }

    // ══ 交互:鼠标 ═══════════════════════════════════════
    private string HitAt(Point p)
    {
        for (int i = _hits.Count - 1; i >= 0; i--)
            if (_hits[i].R.Contains(p)) return _hits[i].Id;
        return "";
    }

    private Rectangle RectOf(string id)
    {
        foreach (var h in _hits) if (h.Id == id) return h.R;
        return Rectangle.Empty;
    }

    private NumField? FieldAt(Point p)
    {
        if (Locked) return null;   // 任务进行中:字段既不可点也不可滚轮/键盘改
        foreach (var f in VisibleFields())
            if (f.Box.Contains(p) || f.LabelBox.Contains(p) || f.Chevron.Contains(p)
                || f.Up.Contains(p) || f.Down.Contains(p)) return f;
        return null;
    }

    /// <summary>命中的步进按钮:±1 为增减,日期字段的 ▾ 记为 0</summary>
    private (NumField? F, int Dir) StepAt(Point p)
    {
        if (Locked) return (null, 0);
        foreach (var f in VisibleFields())
        {
            if (f.IsDate) { if (f.Chevron.Contains(p)) return (f, 0); }
            else
            {
                if (f.Up.Contains(p)) return (f, 1);
                if (f.Down.Contains(p)) return (f, -1);
            }
        }
        return (null, 0);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_page == "settings")
        {
            var h = _rClose.Contains(PointToClient(Cursor.Position)) ? IdClose : "";
            if (h != _hoverId) { _hoverId = h; Invalidate(); }
            return;
        }
        var id = HitAt(e.Location);
        var (sf, sd) = StepAt(e.Location);
        if (id == _hoverId && ReferenceEquals(sf, _hoverStep) && sd == _hoverStepDir) return;
        _hoverId = id; _hoverStep = sf; _hoverStepDir = sd;
        Cursor = id.Length > 0 || sf != null ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        StopRepeat();
        if (_hoverId.Length == 0 && _hoverStep == null) return;
        _hoverId = ""; _hoverStep = null; _hoverStepDir = 0;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (_page == "settings") { HandleSettingsClick(e.Location); return; }
        if (Locked && _rCreate.Contains(e.Location))
        {
            FlashStatus("任务进行中,先取消才能改", FluentTheme.TextSecondary);
            return;
        }
        // ▾ 的开关语义:月历开着时再点 ▾ 只收起(不拦住会被下面的"点外收起"关掉后又立刻重开)
        var (sfPre, sdPre) = StepAt(e.Location);
        if (_calendar is { Visible: true } && sfPre != null && sfPre.IsDate)
        {
            CloseCalendar();
            SetFocused(sfPre);
            return;
        }
        // 弹层不抢激活权,所以"点到外面就收起"要自己判断
        if (_calendar is { Visible: true } cal && !cal.Bounds.Contains(PointToScreen(e.Location)))
            CloseCalendar();
        var (sf, sd) = StepAt(e.Location);
        if (sf != null)
        {
            SetFocused(sf);
            if (sf.IsDate) { OpenCalendar(sf); return; }
            StepField(sf, sd);
            _repeatField = sf; _repeatDir = sd;
            _stepTick.Interval = 400;
            _stepTick.Start();
            return;
        }
        var id = HitAt(e.Location);
        if (id.Length > 0)
        {
            Invalidate(RectOf(id));
            if (_actions.TryGetValue(id, out var act)) act();
            return;
        }
        var f = FieldAt(e.Location);
        if (f != null) { SetFocused(f); return; }
        if (_rTitle.Contains(e.Location)) { StartDrag(); return; }
        SetFocused(null);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        StopRepeat();
    }

    private void StopRepeat()
    {
        _stepTick.Stop();
        _repeatField = null;
        _repeatDir = 0;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var target = FieldAt(e.Location) ?? _focused;
        if (target == null) return;
        StepField(target, e.Delta > 0 ? 1 : -1);
        if (e is HandledMouseEventArgs he) he.Handled = true;
    }

    private void OpenCalendar(NumField f)
    {
        if (_calendar is { Visible: true }) { CloseCalendar(); return; }   // 再点 ▾ 收起
        var cal = new CalendarForm(_atDate, d => { _atDate = d; _atDirty = true; RebuildLayout(); Invalidate(); }, _scale);
        var wa = Screen.FromControl(this).WorkingArea;
        int x = PointToScreen(f.Box.Location).X;
        int y = PointToScreen(new Point(f.Box.X, f.Box.Bottom + Px(4))).Y;
        if (y + cal.Height > wa.Bottom)
            y = PointToScreen(new Point(f.Box.X, f.Box.Y - cal.Height - Px(4))).Y;
        cal.Location = new Point(Math.Min(x, wa.Right - cal.Width), y);
        _calendar = cal;
        // 月历是 NOACTIVATE 窗,自己不会失焦;选完日期只 Close,不 Dispose 会每次漏一个窗体+3 个字体
        cal.FormClosed += (_, _) => { if (ReferenceEquals(_calendar, cal)) _calendar = null; cal.Dispose(); };
        cal.Show(this);
    }

    private void CloseCalendar()
    {
        var cal = _calendar;
        _calendar = null;
        if (cal == null) return;
        cal.Close();
        cal.Dispose();
    }

    // ══ 交互:键盘 ═══════════════════════════════════════
    private void SetFocused(NumField? f)
    {
        if (ReferenceEquals(_focused, f)) return;
        var prev = _focused;
        _focused = f;
        InvalidateField(prev);
        InvalidateField(f);
    }

    private void InvalidateField(NumField? f)
    {
        if (f == null) return;
        Invalidate(Rectangle.Union(Rectangle.Union(f.Box, f.LabelBox), f.IsDate ? f.Chevron : f.Down));
    }

    private void StepField(NumField f, int delta)
    {
        if (f.IsDate) _atDate = _atDate.AddDays(delta);
        else f.Val = Math.Clamp(f.Val + delta, 0, f.Max);
        if (Array.IndexOf(_at, f) >= 0) _atDirty = true;
        InvalidateField(f);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Tab || keyData == (Keys.Tab | Keys.Shift))
        {
            var fields = VisibleFields();
            int i = _focused == null ? -1 : Array.IndexOf(fields, _focused);
            int n = fields.Length;
            int next = keyData == Keys.Tab ? (i + 1) % n : (i - 1 + n) % n;
            SetFocused(fields[next]);
            return true;
        }
        if (keyData == Keys.Enter)
        {
            if (_warnEditActive) { CommitWarnEdit(); return true; }   // 编辑中:Enter 提交提醒点
            if (Locked) return true;   // 运行中 Enter 不重挂任务,防止默认值静默覆盖当前任务
            StartCurrentMode();
            return true;
        }
        if (keyData == Keys.Escape)
        {
            if (_warnEditActive) { CommitWarnEdit(); return true; }   // 编辑中:Esc 先提交编辑
            if (_page == "settings") SwitchPage("main");
            if (_calendar != null) CloseCalendar(); else SetFocused(null);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // 设置页提醒点编辑:数字直接打字、退格删位
        if (_page == "settings" && _warnEditActive)
        {
            var k = e.KeyCode;
            if (k is >= Keys.D0 and <= Keys.D9) { _warnEditBuf += (char)('0' + (k - Keys.D0)); e.Handled = true; }
            else if (k is >= Keys.NumPad0 and <= Keys.NumPad9) { _warnEditBuf += (char)('0' + (k - Keys.NumPad0)); e.Handled = true; }
            else if (k == Keys.Back && _warnEditBuf.Length > 0) { _warnEditBuf = _warnEditBuf[..^1]; e.Handled = true; }
            else if (k is Keys.Enter or Keys.Escape) { CommitWarnEdit(); e.Handled = true; }
            if (e.Handled) { Invalidate(); return; }
        }
        base.OnKeyDown(e);
        if (_focused == null || Locked) return;
        int delta = e.KeyCode switch
        {
            Keys.Up or Keys.Right => 1,
            Keys.Down or Keys.Left => -1,
            Keys.PageUp => 5,
            Keys.PageDown => -5,
            _ => 0,
        };
        if (delta != 0) StepField(_focused, delta);
    }

    private void StartDrag()
    {
        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
    }

    /// <summary>面板失焦(点了别处 / 收进托盘)时收起月历:它是独立顶层窗,不跟着隐藏会一直浮在最上层。</summary>
    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (_calendar is { Visible: true }) CloseCalendar();
    }

    protected override void WndProc(ref Message m)
    {
        if ((uint)m.Msg == WM_SHOW_PANEL)
        {
            Logger.Info("WndProc: 收到 WM_SHOW_PANEL,弹面板");
            ShowPanel();
            return;
        }
        base.WndProc(ref m);
        if (m.Msg == WM_DWMCOMPOSITIONCHANGED)
        {
            _acrylic = FluentTheme.ApplyAcrylic(this);
            if (!_acrylic) FluentTheme.ClearBackdrop(this);
            Invalidate();
        }
    }

    // ══ 业务逻辑 ═════════════════════════════════════════
    /// <summary>剩余时间。必须单独处理超过一天的情况:TimeSpan.Hours 只是 0-23 的分量,
    /// 直接用会把 26 小时显示成 02 小时,严重误导关机时刻。</summary>
    private static string FormatRemain(TimeSpan r)
        => r.TotalDays >= 1
            ? $"{(int)r.TotalDays}天{r.Hours:00}:{r.Minutes:00}:{r.Seconds:00}"
            : $"{r.Hours:00}:{r.Minutes:00}:{r.Seconds:00}";

    private string StatusText()
    {
        if (_flashMsg != null && DateTime.Now < _flashUntil) return _flashMsg;
        if (_paused) return $"已暂停 · 剩余 {FormatRemain(_pausedRemain)}";
        if (_indefiniteGuard && _target == null) return "无限护航中 · 系统不会自动睡眠";
        if (_target == null) return "未设定关机";
        var remain = _target.Value - DateTime.Now;
        if (_shutdownError != null) return _shutdownError;
        if (remain <= TimeSpan.Zero) return "正在执行…";
        return $"剩余 {FormatRemain(remain)}";
    }

    /// <summary>
    /// 开机自启动会让"启动时算的默认值"一直挂着不更新(可能挂一整天),
    /// 所以每次进定点模式都按当时时间重算 +2 小时;用户手动改过就不再覆盖。
    /// </summary>
    private void RefreshAtDefault()
    {
        if (_atDirty) return;
        var soon = DateTime.Now.AddHours(2);
        _atDate = soon.Date;
        _at[1].Val = soon.Hour;
        _at[2].Val = soon.Minute;
    }

    private void SwitchMode(string mode)
    {
        if (_mode == mode || Locked) return;
        _mode = mode;
        CloseCalendar();
        SetFocused(null);
        if (mode == "at") RefreshAtDefault();
        RebuildLayout();
        Invalidate();
    }

    private void Preset(int h, int m)
    {
        _cd[0].Val = h;
        _cd[1].Val = m;
        RebuildLayout();
        Invalidate();
    }

    /// <summary>定点模式选中的完整时刻 = 日历选中的日期 + 时/分字段</summary>
    private DateTime AtValue() => _atDate.Date + new TimeSpan(_at[1].Val, _at[2].Val, 0);

    /// <summary>往后加时间:日期与时分一起写回</summary>
    private void NudgeAt(TimeSpan ts)
    {
        var v = AtValue() + ts;
        _atDate = v.Date;
        _at[1].Val = v.Hour;
        _at[2].Val = v.Minute;
        _atDirty = true;
        RebuildLayout();
        Invalidate();
    }

    private void StartCurrentMode()
    {
        if (_mode == "countdown") StartCountdown(); else StartAtTime();
    }

    // ── 任务暂停/继续────────────────────────────
    private void PauseTask()
    {
        if (_target == null) return;
        _pausedRemain = _target.Value - DateTime.Now;
        _paused = true;
        SetTrayText("定时关机助手 · 已暂停");
        RebuildLayout();
        Invalidate();
        Logger.Info("任务暂停");
    }

    private void ResumeTask()
    {
        _target = DateTime.Now + _pausedRemain;
        _paused = false;
        RebuildLayout();
        Invalidate();
        Logger.Info("任务继续");
    }

    private void StartCountdown()
    {
        var total = _cd[0].Val * 3600 + _cd[1].Val * 60;
        if (total <= 0)
        {
            MessageBox.Show("请先设置时长(时/分至少一项大于 0)", "定时关机助手",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        SetTarget(DateTime.Now.AddSeconds(total));
    }

    private void StartAtTime()
    {
        var t = AtValue();
        if (t <= DateTime.Now)
        {
            MessageBox.Show("所选时间已经是过去啦,请选一个未来的时间。", "定时关机助手",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        SetTarget(t);
    }

    private void SetTarget(DateTime t)
    {
        _target = t;
        _shutdownError = null;
        _totalSeconds = Math.Max(1, (int)(t - DateTime.Now).TotalSeconds);
        _warnFired = false;                         // 新任务重新计预警点
        RefreshGuard();                             // 任务护航(与无限护航开关取或)
        CloseCalendar();
        SetFocused(null);                           // 启动后创建区锁定,焦点得撤掉
        RebuildLayout();
        Invalidate();
        Logger.Info($"设定: mode={_mode}, action={_action}, target={t:yyyy-MM-dd HH:mm:ss}, dryRun={_dryRun}");
    }

    // ── 预警通知(Windows 系统通知,进通知中心)──

    /// <summary>剩余时间到达提醒点(仅一次)→ 弹系统通知,软件内正常计时。</summary>
    private void CheckWarn(TimeSpan remain)
    {
        if (!_settings.WarnEnabled || _warnFired) return;
        if (remain.TotalSeconds > _settings.WarnPoint * 60) return;
        _warnFired = true;
        string msg = $"还有 {_settings.WarnPoint} 分钟 · {_action}";
        Logger.Info($"预警通知: {msg}");
        try { _tray.ShowBalloonTip(0, "定时关机助手", msg, ToolTipIcon.None); }
        catch { /* 通知失败不影响计时 */ }
    }

    // ── 防睡眠护航──────────────────
    private bool _guardActive;
    private bool _indefiniteGuard;   // 无限护航:不定时长挂机,托盘一键开关,不跨重启

    /// <summary>护航有效性 = 无限护航开关 或 (有任务 且 任务护航开启)。
    /// 生效时持执行态标志锁住系统不自动睡眠;屏幕是否常亮由 KeepDisplayWhileTiming 决定。</summary>
    private void RefreshGuard()
    {
        bool on = _indefiniteGuard || (_target != null && _settings.KeepAwakeWhileTiming);
        if (_guardActive == on) return;
        _guardActive = on;
        uint flags = PowerApis.ES_CONTINUOUS;
        if (on) flags |= PowerApis.ES_SYSTEM_REQUIRED
                       | (_settings.KeepDisplayWhileTiming ? PowerApis.ES_DISPLAY_REQUIRED : 0u);
        var r = PowerApis.SetThreadExecutionState(flags);
        Logger.Info($"护航{(on ? "开启" : "释放")}: indefinite={_indefiniteGuard}, SetThreadExecutionState=0x{r:X}");
    }

    // ── 托盘图标数字/进度环────────────────────────
    private void UpdateTrayIcon(TimeSpan remain)
    {
        if (!_settings.TrayCountdownEnabled)
        {
            RestoreTrayStar();
            return;
        }
        if (_settings.TrayCountdownStyle == "Ring")
        {
            int pct = Math.Clamp((int)Math.Round(remain.TotalSeconds * 100.0 / _totalSeconds), 0, 100);
            string key = "ring" + pct;
            if (key == _lastIconKey) return;
            _lastIconKey = key;
            SetTrayIcon(MakeRingIcon(pct));
        }
        else
        {
            string text = remain.TotalHours >= 1
                ? $"{(int)remain.TotalHours}:{remain.Minutes:00}"
                : $"{remain.Minutes:00}:{remain.Seconds:00}";
            if (text == _lastIconKey) return;   // 仅当显示串变化才重绘
            _lastIconKey = text;
            SetTrayIcon(MakeDigitsIcon(text, Color.FromArgb(255, 252, 240), Color.FromArgb(16, 15, 15)));
        }
    }

    private void RestoreTrayStar()
    {
        if (_lastIconKey == "star") return;
        _lastIconKey = "star";
        SetTrayIcon(_baseIcon);
    }

    private void SetTrayIcon(Icon? icon)
    {
        var old = _tray.Icon;
        _tray.Icon = icon;
        // 旧 Icon 必须 Dispose,否则 GDI 句柄泄漏(闪烁残留的来源)
        if (old != null && !ReferenceEquals(old, _baseIcon)) old.Dispose();
    }

    private Icon MakeRingIcon(int pct)
    {
        int size = 64;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = Rectangle.Inflate(new Rectangle(0, 0, size, size), -10, -10);
            using var track = new Pen(Color.FromArgb(206, 206, 208), 8f);
            g.DrawArc(track, r, -90, 360);
            using var arc = new Pen(Color.FromArgb(0xAF, 0x30, 0x29), 8f);
            g.DrawArc(arc, r, -90, 360f * pct / 100f);
        }
        return IconFromBitmap(bmp);
    }

    private Icon MakeDigitsIcon(string text, Color back, Color fore)
    {
        int size = 64;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = FluentTheme.RoundedPath(new Rectangle(2, 2, size - 4, size - 4), 14))
                using (var bb = new SolidBrush(back)) g.FillPath(bb, path);
            using var f = new Font("Consolas", size * (text.Length > 4 ? 0.30f : 0.40f), FontStyle.Bold, GraphicsUnit.Pixel);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using var tb = new SolidBrush(fore);
            g.DrawString(text, f, tb, new RectangleF(0, 0, size, size), sf);
        }
        return IconFromBitmap(bmp);
    }

    private static Icon IconFromBitmap(Bitmap bmp)
    {
        var h = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(h);
            return (Icon)tmp.Clone();   // Clone 拥有独立副本,原 HICON 立即销毁
        }
        finally
        {
            PowerApis.DestroyIcon(h);
        }
    }

    private void OnTick()
    {
        // 状态行临时消息到点回落
        if (_flashMsg != null && DateTime.Now >= _flashUntil)
        {
            _flashMsg = null;
            Invalidate(_rStatus);
        }

        // 暂停:时间冻结,状态行保持"已暂停"
        if (_paused) return;

        if (_target == null) return;
        var remain = _target.Value - DateTime.Now;
        if (remain <= TimeSpan.Zero)
        {
            ExecuteDueAction();
            return;
        }
        CheckWarn(remain);
        Invalidate(_rStatus);
        UpdateTrayIcon(remain);
        SetTrayText($"定时关机助手 · 剩余 {FormatRemain(remain)}");

    }


    /// <summary>托盘文本统一入口:超长截断,杜绝 NotifyIcon 63 字符上限抛异常。</summary>
    private void SetTrayText(string text)
    {
        try { _tray.Text = Logger.TraySafe(text); }
        catch { /* 托盘不可用时静默 */ }
    }


    /// <summary>到点:动作四选一走 ActionExecutor 单点。破坏性动作到点即执行,无宽限。</summary>
    private void ExecuteDueAction()
    {
        // 不停 tick:出口都靠 _target 自身清空防重入,停掉会让下一次任务永远不倒数
        var r = ActionExecutor.Execute(_action, _dryRun);

        if (r.Error != null)
        {
            // 失败即停,禁止自动重试——错误常驻,等用户干预
            _target = null;
            _shutdownError = r.Error;
            RefreshGuard();   // 失败即停;无限护航不受影响
            SetTrayText("定时关机助手 · " + r.Error);
            RestoreTrayStar();
            RebuildLayout();
            Invalidate();
            if (Visible) MessageBox.Show(r.Error, "定时关机助手", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // 完成(全部动作):破坏性动作由系统接管,进程可能随之结束
        _target = null;
        RefreshGuard();
        RestoreTrayStar();
        SetTrayText(_dryRun ? "定时关机助手 · [dry-run] 已模拟" : "定时关机助手 · 已执行" + TileDisplay[Array.IndexOf(ActionExecutor.All, _action)]);
        FlashStatus(r.DryRun ? $"[dry-run] 已模拟 {_action}" : $"已执行 {_action}", _dryRun ? FluentTheme.Accent : FluentTheme.Danger);
        RebuildLayout();
        Invalidate();
    }

    /// <summary>取消任务:清目标、释放护航、托盘恢复星星。到点即执行,没有可撤销的待关机。</summary>
    private void CancelPending()
    {
        _target = null;
        _paused = false;
        RefreshGuard();
        _tick.Start();   // 保留 tick:状态行闪消息的回落靠它驱动
        SetTrayText("定时关机助手");
        RestoreTrayStar();
        Logger.Info("取消已设定的关机");
        RebuildLayout();
        Invalidate();
    }

    // ── 设置页(集成进主面板,⚙ 切换,✕ 返回)────

    private sealed class SettingRow
    {
        public string Kind = "";                 // group | toggle | chips
        public string Label = "";
        public Func<bool>? GetBool;
        public Action<bool>? SetBool;
        public Rectangle Rect;
        public Rectangle ToggleRect;
        public List<(int idx, Rectangle grp)> Chips = new();   // 提醒点编辑器:[数值框|▲▼]
    }

    private readonly List<SettingRow> _setRows = new();
    private static readonly int[] ChipSteps = [1, 3, 5, 10, 15, 20, 30, 45, 60, 90, 120];

    private void BuildSettingRows()
    {
        _setRows.Clear();
        _setRows.Add(new SettingRow { Kind = "group", Label = "系统行为" });
        _setRows.Add(new SettingRow { Kind = "toggle", Label = "计时期间保持系统唤醒", GetBool = () => _settings.KeepAwakeWhileTiming, SetBool = v => _settings.KeepAwakeWhileTiming = v });
        _setRows.Add(new SettingRow { Kind = "toggle", Label = "同时保持屏幕常亮", GetBool = () => _settings.KeepDisplayWhileTiming, SetBool = v => _settings.KeepDisplayWhileTiming = v });
        _setRows.Add(new SettingRow { Kind = "toggle", Label = "托盘显示剩余时间", GetBool = () => _settings.TrayCountdownEnabled, SetBool = v => _settings.TrayCountdownEnabled = v });
        _setRows.Add(new SettingRow { Kind = "toggle", Label = "开机自启动", GetBool = () => _settings.AutoStart,
            SetBool = v => _settings.AutoStart = AppSettings.ApplyAutoStart(v) ? v : AppSettings.GetAutoStart() });
        _setRows.Add(new SettingRow { Kind = "group", Label = "提醒与通知" });
        _setRows.Add(new SettingRow { Kind = "toggle", Label = "到点前提醒", GetBool = () => _settings.WarnEnabled, SetBool = v => _settings.WarnEnabled = v });
        _setRows.Add(new SettingRow { Kind = "chips", Label = "提前" });
    }

    /// <summary>设置页切换:重建该页布局与命中区,窗口高度随页变化。切页前先提交未完成的提醒点编辑。</summary>
    private void SwitchPage(string p)
    {
        if (_warnEditActive) CommitWarnEdit();   // 编辑未提交就切页:先落盘,避免显示与实际不一致
        _page = p;
        SetFocused(null);
        CloseCalendar();
        RebuildLayout();
        Invalidate();
        Logger.Info($"切换页面: {p}");
    }

    private void LayoutSettings()
    {
        BuildSettingRows();
        _clientW = Px(BaseW);
        int y = Px(TitleH + Pt);
        _rTitle = new Rectangle(0, 0, _clientW, Px(TitleH));
        _rIcon = new Rectangle(Px(12), Px(12), Px(16), Px(16));
        _rGear = Rectangle.Empty;
        _rClose = new Rectangle(_clientW - Px(WinBtnW), 0, Px(WinBtnW), Px(TitleH));

        foreach (var row in _setRows)
        {
            if (row.Kind == "group")
            {
                row.Rect = new Rectangle(Px(PadX), y, _clientW - Px(PadX * 2), Px(24));
                y += row.Rect.Height + Px(4);
                continue;
            }
            row.Rect = new Rectangle(Px(PadX), y, _clientW - Px(PadX * 2), Px(34));
            row.ToggleRect = new Rectangle(row.Rect.Right - Px(40), y + Px(7), Px(40), Px(20));
            if (row.Kind == "chips") LayoutSettingChips(row);
            y += row.Rect.Height + Px(4);
        }

        _rFooter = new Rectangle(0, y + Px(Gap), _clientW, Px(FooterH));
        y = _rFooter.Bottom + Px(Pb);
        ClientSize = new Size(_clientW, y);
        _segTextW0 = 0; _segTextW1 = 0;
    }

    private void LayoutSettingChips(SettingRow row)
    {
        // 靠右排:容器 64 + 间距 + "分",与开关一样贴齐行右缘,别压住左侧标签
        var grp = new Rectangle(row.Rect.Right - Px(90), row.Rect.Y + Px(3), Px(64), Px(30));
        row.Chips.Clear();
        row.Chips.Add((0, grp));
        row.Rect = new Rectangle(row.Rect.X, row.Rect.Y, row.Rect.Width, grp.Bottom + Px(2) - row.Rect.Y);
    }

    /// <summary>提醒点编辑器子区:容器 / 数值区 / ▲▼ 列。"分"字画在容器右侧。</summary>
    private (Rectangle ctrl, Rectangle val, Rectangle up, Rectangle down) WarnEditorParts(Rectangle grp)
    {
        var ctrl = new Rectangle(grp.X, grp.Y, Px(64), grp.Height);
        var val = new Rectangle(grp.X + Px(2), grp.Y + Px(2), Px(44), grp.Height - Px(4));
        int colW = Px(16), half = val.Height / 2;
        var up = new Rectangle(val.Right, val.Y, colW, half);
        var down = new Rectangle(val.Right, val.Y + half, colW, val.Height - half);
        return (ctrl, val, up, down);
    }

    private void DrawSettingsPage(Graphics g)
    {
        // 这页铺实色底(不像主页那样透出玻璃),所以小字可以走 ClearType + 网格对齐,笔画落在整像素上才不发虚
        g.Clear(FluentTheme.BgFallback);
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        DrawStr(g, "首选项", _fTitle, FluentTheme.TextTitle,
            new RectangleF(_rIcon.Right + Px(8), _rTitle.Y, Px(180), _rTitle.Height), FmtLeft);
        DrawTitleButton(g, _rClose, "back");

        foreach (var row in _setRows)
        {
            if (row.Kind == "group")
            {
                using var f = SegFont(10, true);
                DrawStr(g, row.Label.ToUpperInvariant(), f, FluentTheme.TextSecondary, row.Rect, FmtLeft);
                continue;
            }
            DrawStr(g, row.Label, _fLabel, FluentTheme.TextPrimary,
                new RectangleF(row.Rect.X, row.Rect.Y, row.Rect.Width - Px(130), row.Rect.Height), FmtLeft);
            if (row.Kind == "toggle") DrawPageToggle(g, row.ToggleRect, row.GetBool!());
            if (row.Kind == "chips")
            {
                // 主面板同款字段:一个容器包住[数值|▲▼],中间细分隔线,底部 2px 下划线
                var (ctrl, vr, up, down) = WarnEditorParts(row.Chips[0].grp);
                bool en = _settings.WarnEnabled;
                bool editing = _warnEditActive;
                string txt = editing ? _warnEditBuf : _settings.WarnPoint.ToString();
                var ink = en ? FluentTheme.TextMuted : Color.FromArgb(150, 156, 139);
                using (var path = FluentTheme.TopRounded(new Rectangle(ctrl.X, ctrl.Y, ctrl.Width, ctrl.Height - Px(2)), Radius(6)))
                    FillPath(g, path, en
                        ? FluentTheme.Over(GlassBase(), Color.FromArgb(editing ? 204 : 128, 255, 255, 255))
                        : FluentTheme.Over(GlassBase(), Color.FromArgb(26, 0, 0, 0)));
                var cp = PointToClient(Cursor.Position);
                DrawStepBtn(g, up, en && up.Contains(cp), 1, ink);
                DrawStepBtn(g, down, en && down.Contains(cp), -1, ink);
                using (var pen = new Pen(FluentTheme.FieldUnderline, Math.Max(1f, _scale)))
                    g.DrawLine(pen, vr.Right, ctrl.Y + 1, vr.Right, ctrl.Bottom - Px(3));
                Fill(g, new Rectangle(ctrl.X, ctrl.Bottom - Px(2), ctrl.Width, Px(2)),
                    editing && en ? FluentTheme.Accent : FluentTheme.FieldUnderline);
                DrawStr(g, txt, _fPill, en ? FluentTheme.TextPrimary : FluentTheme.TextSecondary, vr, FmtCenter);
                DrawStr(g, "分", _fUnit, FluentTheme.TextSecondary,
                    new RectangleF(ctrl.Right + Px(4), ctrl.Y, Px(20), ctrl.Height), FmtLeft);
            }
        }
        DrawChrome(g);
    }

    private void DrawPageToggle(Graphics g, Rectangle r, bool on)
    {
        using var path = FluentTheme.RoundedPath(r, r.Height / 2);
        FillPath(g, path, on ? FluentTheme.Accent : FluentTheme.Over(GlassBase(), Color.FromArgb(51, 0, 0, 0)));
        int k = r.Height - Px(4);
        var knob = new Rectangle(on ? r.Right - k - Px(2) : r.X + Px(2), r.Y + Px(2), k, k);
        g.FillEllipse(Brushes.White, knob);   // Brushes.White 是系统缓存画刷:每次重绘 new SolidBrush 会漏 GDI 句柄
    }

    /// <summary>设置页点击:← 返回、开关翻转、提醒点编辑(数值框打字/▲▼ 步进)。</summary>
    private void HandleSettingsClick(Point p)
    {
        if (_rClose.Contains(p)) { SwitchPage("main"); return; }
        foreach (var row in _setRows)
        {
            if (row.Kind == "toggle" && row.ToggleRect.Contains(p))
            {
                row.SetBool!(!row.GetBool!());
                _settings.Save();
                RefreshGuard();
                FlashStatus("已保存", FluentTheme.Accent);
                Invalidate();
                return;
            }
            if (row.Kind != "chips") continue;

            var (_, vr, up, down) = WarnEditorParts(row.Chips[0].grp);
            if (!_settings.WarnEnabled) return;   // 未开启:灰色不可操作
            if (vr.Contains(p)) { _warnEditActive = true; _warnEditBuf = _settings.WarnPoint.ToString(); Invalidate(); return; }
            if (up.Contains(p)) { _settings.WarnPoint = Math.Min(720, _settings.WarnPoint + 1); SaveWarnPoint(); return; }
            if (down.Contains(p)) { _settings.WarnPoint = Math.Max(1, _settings.WarnPoint - 1); SaveWarnPoint(); return; }
        }
    }

    private void SaveWarnPoint()
    {
        _settings.Save();
        LayoutSettings();
        Invalidate();
        FlashStatus("已保存", FluentTheme.Accent);
    }

    /// <summary>提交正在编辑的提醒点值(Enter/点击别处)。</summary>
    private void CommitWarnEdit()
    {
        if (!_warnEditActive) return;
        if (int.TryParse(_warnEditBuf, out var v))
            _settings.WarnPoint = Math.Clamp(v, 1, 720);
        _warnEditActive = false;
        _warnEditBuf = "";
        _settings.Save();
        LayoutSettings();
        Invalidate();
        FlashStatus("已保存", FluentTheme.Accent);
    }

    /// <summary>状态行临时消息:显示 3 秒后回落常驻信息。</summary>
    public void FlashStatus(string msg, Color color)
    {
        _flashMsg = msg;
        _flashColor = color;
        _flashUntil = DateTime.Now.AddSeconds(3);
        Invalidate(_rStatus);
    }

    private void ShowPanel()
    {
        // 面板从托盘里躺了几个小时后重新打开,定点默认值同样该跟上现在的时间
        if (_mode == "at" && _target == null) { RefreshAtDefault(); RebuildLayout(); }
        Show();
        WindowState = FormWindowState.Normal;
        // 前台权限往往还在 shell 或调用方手里:先压到最上,等 300ms 再交还,
        // 否则刚被叫醒的面板会被重新抢到前台的窗口盖住
        TopMost = true;
        _topHold.Start();
        Activate();
    }

    private void ReallyExit()
    {
        if (_target != null)
        {
            if (MessageBox.Show("有待执行的关机任务,退出将同时取消它。确定退出?",
                "定时关机助手", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        }
        _indefiniteGuard = false;
        RefreshGuard();   // 退出前显式清执行态(进程退出本身也会恢复,双保险)
        _reallyExit = true;
        _tray.Visible = false;
        _tick.Stop();
        _stepTick.Stop();
        Context.ExitThread();
    }
}
