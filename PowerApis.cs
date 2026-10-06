using System.Runtime.InteropServices;

namespace ShutdownTimer;

/// <summary>
/// 全部新 P/Invoke 集中处:护航执行态、锁屏、关显示器、
/// 关机/重启(含特权)。睡眠走 Application.SetSuspendState,不在此处。
/// </summary>
internal static class PowerApis
{
    // ── 防睡眠护航────────────────────────────────
    public const uint ES_CONTINUOUS = 0x80000000;
    public const uint ES_SYSTEM_REQUIRED = 0x00000001;
    public const uint ES_DISPLAY_REQUIRED = 0x00000002;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint SetThreadExecutionState(uint esFlags);

    // ── 关机 / 重启 ───────────────────────────────────────
    // 用 ExitWindowsEx 而不是 InitiateSystemShutdown:后者是 LSA/远程语义,不给本会话
    // 应用发 WM_QUERYENDSESSION,Wallpaper 这类常驻程序会被直接终止并判定上次异常退出。
    // ExitWindowsEx 正是开始菜单"关机/重启"走的那条路。
    public const uint EWX_REBOOT = 0x00000002;
    public const uint EWX_POWEROFF = 0x00000008;
    public const uint EWX_FORCEIFHUNG = 0x00000010;   // 只强制卡死的应用,响应正常的仍走通知流程
    public const uint EWX_EXIT_WINDOWS = 0x00000020;  // 关不掉就回到正常状态(应用可阻止),不蓝屏不硬切

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool ExitWindowsEx(uint uFlags, uint dwReason);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr TokenHandle, bool DisableAllPrivileges, ref TOKEN_PRIVILEGES NewState, uint BufferLength, IntPtr PreviousState, IntPtr ReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    // LUID 必须是 {DWORD,LONG} 且紧跟 PrivilegeCount:原先用 long 塞进去,
    // 默认对齐会让它落在偏移 8,原生侧读到的是填充字节,特权根本没被启用。
    [StructLayout(LayoutKind.Sequential)]
    private struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x0002;
    private const int ERROR_NOT_ALL_ASSIGNED = 1300;

    /// <summary>启用 SeShutdownPrivilege;ExitWindowsEx / 关机 API 要求它处于启用态。返回是否真的启用了。</summary>
    public static bool EnableShutdownPrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token))
        {
            Logger.Error($"OpenProcessToken 失败(错误码 {Marshal.GetLastWin32Error()})");
            return false;
        }
        try
        {
            if (!LookupPrivilegeValue(null, "SeShutdownPrivilege", out var luid))
            {
                Logger.Error($"LookupPrivilegeValue 失败(错误码 {Marshal.GetLastWin32Error()})");
                return false;
            }
            var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1, Luid = luid, Attributes = SE_PRIVILEGE_ENABLED };
            // BufferLength 原来传 0 → API 直接失败,整条特权启用一直是空操作
            if (!AdjustTokenPrivileges(token, false, ref tp, (uint)Marshal.SizeOf<TOKEN_PRIVILEGES>(), IntPtr.Zero, IntPtr.Zero))
            {
                Logger.Error($"AdjustTokenPrivileges 失败(错误码 {Marshal.GetLastWin32Error()})");
                return false;
            }
            // 该 API 即使特权没被分配给本令牌也返回 true,真实结果要看 GetLastError
            int err = Marshal.GetLastWin32Error();
            if (err != 0)
            {
                Logger.Error($"特权未完全启用(错误码 {err}{(err == ERROR_NOT_ALL_ASSIGNED ? " = 本令牌没有 SeShutdownPrivilege" : "")})");
                return false;
            }
            return true;
        }
        finally { CloseHandle(token); }
    }

    // ── 锁屏 ─────────────────────────────────────────────
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool LockWorkStation();

    // ── 锁屏检测(锁屏时不该弹预警)────────────────
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseDesktop(IntPtr hDesktop);

    private const uint DESKTOP_READ = 0x0002;

    /// <summary>打开不了输入桌面 = 锁定/无交互会话。</summary>
    public static bool IsWorkstationLocked()
    {
        var h = OpenInputDesktop(0, false, DESKTOP_READ);
        if (h == IntPtr.Zero) return true;
        CloseDesktop(h);
        return false;
    }

    // ── 托盘图标句柄管理──────────────────────────
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    // ── 关显示器(SC_MONITORPOWER,尽力而为)────────────
    public const int HWND_BROADCAST = 0xffff;
    public const int WM_SYSCOMMAND = 0x0112;
    public const int SC_MONITORPOWER = 0xF170;
    public const int MONITOR_OFF = 2;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    public static void TurnOffMonitors()
    {
        SendMessageTimeout((IntPtr)HWND_BROADCAST, WM_SYSCOMMAND, (IntPtr)SC_MONITORPOWER, (IntPtr)MONITOR_OFF, 2, 1000, out _);
    }
}
