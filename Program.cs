using System.Threading;
using ShutdownTimer;

ApplicationConfiguration.Initialize();

// ── 全局异常兜底:无人值守工具,异常只写日志,绝不弹框 ──
// 顺序:先初始化日志,立刻挂钩子,再去做任何可能抛异常的事(设置加载里有注册表和文件 IO)。
Logger.Init(true);

Application.ThreadException += (_, e) => Logger.Error("UI 线程异常: " + e.Exception);
Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
{
    // "+" 的优先级高于 "??",写在一个表达式里会让后两个兜底永远走不到
    var detail = (e.ExceptionObject as Exception)?.ToString() ?? e.ExceptionObject?.ToString() ?? "未知对象";
    Logger.Error("未处理异常: " + detail);
};

var bootSettings = AppSettings.Load();
var bootDryRun = Environment.GetCommandLineArgs().Contains("--dry-run");   // 四动作只记日志不执行

// 只留一个实例:再双击 exe 不再开第二个托盘图标,而是叫已经躲起来的那个弹面板
using var single = new Mutex(true, @"Local\ShutdownTimer.SingleInstance", out var isFirst);
if (!isFirst)
{
    PanelForm.WakeExistingInstance();
    return;
}

Logger.Info($"启动: dryRun={bootDryRun}, autostart参数={Environment.GetCommandLineArgs().Contains("/autostart")}");
var panel = new PanelForm(Environment.GetCommandLineArgs().Contains("/autostart"), bootSettings, bootDryRun);
Application.Run(panel.Context);
