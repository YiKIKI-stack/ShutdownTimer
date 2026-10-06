using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ShutdownTimer;

/// <summary>
/// 动作执行单点入口:四个动作一个函数,集中处理
/// --dry-run 分支、失败语义、日志。
/// 所有到点路径只准经过这里,不允许旁路调用。
/// 动作集合对齐 Windows 电源菜单:锁定 / 睡眠 / 关机 / 重启。
/// </summary>
internal static class ActionExecutor
{
    public const string Lock = "锁定";
    public const string Sleep = "睡眠";
    public const string Shutdown = "关机";
    public const string Reboot = "重启";

    public static readonly string[] All = [Lock, Sleep, Shutdown, Reboot];
    public static readonly string[] Destructive = [Shutdown, Reboot];   // 到点即执行,无宽限窗口

    public static bool IsDestructive(string action) => Array.IndexOf(Destructive, action) >= 0;

    /// <summary>执行结果:完成或失败。</summary>
    public sealed class Result
    {
        public bool DryRun;
        public bool Done;              // 已完成(全部动作,或 dry-run)
        public string? Error;          // 失败时的错误串(含错误码)
    }

    /// <param name="action">四选一动作名</param>
    /// <param name="dryRun">true = 只记日志不执行</param>
    public static Result Execute(string action, bool dryRun)
    {
        var r = new Result { DryRun = dryRun };
        Logger.Info($"动作执行: {action}{(dryRun ? " [dry-run]" : "")}");

        if (dryRun)
        {
            r.Done = true;
            return r;
        }

        switch (action)
        {
            case Shutdown:
            case Reboot:
                PowerApis.EnableShutdownPrivilege();
                // 到点直接执行,不留等待窗口。
                // 走 ExitWindowsEx(开始菜单同一条路):常驻应用会先收到 WM_QUERYENDSESSION
                // 自己收尾,Wallpaper 那类程序不再被判定"异常退出";FORCEIFHUNG 兜住卡死的应用,
                // 不让它把定时关机挡下来。
                uint flags = (action == Reboot ? PowerApis.EWX_REBOOT : PowerApis.EWX_POWEROFF)
                             | PowerApis.EWX_EXIT_WINDOWS | PowerApis.EWX_FORCEIFHUNG;
                if (PowerApis.ExitWindowsEx(flags, 0))
                {
                    r.Done = true;
                    Logger.Info($"{action} 已发起(ExitWindowsEx 0x{flags:X})");
                }
                else
                {
                    r.Error = $"{action}命令失败(错误码 {Marshal.GetLastWin32Error()})";
                    Logger.Error(r.Error);
                }
                break;

            case Lock:
                if (PowerApis.LockWorkStation()) r.Done = true;
                else { r.Error = $"锁定失败(错误码 {Marshal.GetLastWin32Error()})"; Logger.Error(r.Error); }
                break;

            case Sleep:
                // 注:本机 S0ix 待机会断网,用户知情保留(对齐 Windows 电源菜单语义)
                // SetSuspendState 在不支持电源管理的机器上是抛异常而非返回 false
                // 不接住的话 _target 停在过点上,OnTick 会每 500ms 重放一次
                try
                {
                    if (Application.SetSuspendState(PowerState.Suspend, false, false)) r.Done = true;
                    else r.Error = "睡眠失败(系统拒绝)";
                }
                catch (Exception ex) { r.Error = $"睡眠失败: {ex.Message}"; }
                if (r.Error != null) Logger.Error(r.Error);
                break;
        }
        return r;
    }
}
