using Microsoft.Win32;

namespace ShutdownTimer;

/// <summary>
/// v4 设置中枢:ini 持久化(exe 同目录 settings.ini,单机便携语义固定)。
/// 设置项固定存 exe 同目录;自启只由设置页的开关管理,其余保存不碰注册表。
/// 旧版注册表 StartMinimized 在首次加载时迁入 ini 并清掉旧键。
/// </summary>
public sealed class AppSettings
{
    public bool TrayCountdownEnabled = true;
    public string TrayCountdownStyle = "Digits";   // Digits | Ring
    public bool KeepAwakeWhileTiming = true;
    public bool KeepDisplayWhileTiming = false;
    public bool WarnEnabled = false;               // 弹窗纪律:未经用户开启不得自动弹窗
    public int WarnPoint = 5;                      // 到点前提醒(分钟,仅第一个提醒;延后固定 10)
    public bool StartMinimized = true;
    public string LastAction = "关机";              // 动作记忆,新建时默认选上次用的
    public bool AutoStart;                          // 真值始终来自注册表 Run 项(开机自启开关)

    private static string IniPath => Path.Combine(AppContext.BaseDirectory, "settings.ini");
    private const string RUN_KEY = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RUN_NAME = "ShutdownTimer";
    private const string LEGACY_APP_KEY = @"Software\ShutdownTimer";   // 旧版 StartMinimized 的存放处

    // ── 加载 ─────────────────────────────────────────────

    public static AppSettings Load()
    {
        var s = new AppSettings();
        var map = ReadIni(IniPath);

        if (map.TryGetValue("TrayCountdownEnabled", out var v)) s.TrayCountdownEnabled = ParseBool(v);
        if (map.TryGetValue("TrayCountdownStyle", out v) && v is "Digits" or "Ring") s.TrayCountdownStyle = v;
        if (map.TryGetValue("KeepAwakeWhileTiming", out v)) s.KeepAwakeWhileTiming = ParseBool(v);
        if (map.TryGetValue("KeepDisplayWhileTiming", out v)) s.KeepDisplayWhileTiming = ParseBool(v);
        if (map.TryGetValue("WarnEnabled", out v)) s.WarnEnabled = ParseBool(v);
        if (map.TryGetValue("WarnPoint", out v) && int.TryParse(v, out var wp)) s.WarnPoint = Math.Clamp(wp, 1, 720);
        else if (map.TryGetValue("WarnPoints", out v))   // 旧键迁移:取第一个提醒点
        {
            var first = ParsePoints(v).FirstOrDefault();
            if (first > 0) s.WarnPoint = first;
        }
        if (map.TryGetValue("LastAction", out v) && ActionExecutor.All.Contains(v)) s.LastAction = v;

        s.AutoStart = GetAutoStart();

        // v3 迁移:ini 里没有 StartMinimized 就读旧注册表值,写进 ini 后清掉旧键
        if (!map.TryGetValue("StartMinimized", out v))
        {
            using var k = Registry.CurrentUser.OpenSubKey(LEGACY_APP_KEY);
            s.StartMinimized = (k?.GetValue("StartMinimized") as int?) != 0;
        }
        else s.StartMinimized = ParseBool(v);

        return s;
    }

    // ── 保存 ─────────────────────────────────────────────

    public void Save()
    {
        WriteIni(IniPath, new Dictionary<string, string>
        {
            ["TrayCountdownEnabled"] = TrayCountdownEnabled ? "1" : "0",
            ["TrayCountdownStyle"] = TrayCountdownStyle,
            ["KeepAwakeWhileTiming"] = KeepAwakeWhileTiming ? "1" : "0",
            ["KeepDisplayWhileTiming"] = KeepDisplayWhileTiming ? "1" : "0",
            ["WarnEnabled"] = WarnEnabled ? "1" : "0",
            ["WarnPoint"] = WarnPoint.ToString(),
            ["StartMinimized"] = StartMinimized ? "1" : "0",
            ["LastAction"] = LastAction,
        });

        // 自启只在开关被拨动时写(见 ApplyAutoStart)。放在这里会让"任何一次保存"
        // 都把 Run 项改成当前运行的 exe 路径——从 dist 测一次就把开机自启带走了。

        // 迁移完成后旧注册表键不再使用
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(LEGACY_APP_KEY, writable: true);
            k?.DeleteValue("StartMinimized", false);
        }
        catch { /* 无旧键或无权限都无所谓 */ }
    }

    // ── 开机自启(注册表 Run 项,指向当前 exe)────────────

    public static bool GetAutoStart()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RUN_KEY);
        return k?.GetValue(RUN_NAME) != null;
    }

    /// <summary>开机自启开关:只在用户拨动该项时调用,写入当前 exe 路径。</summary>
    public static bool ApplyAutoStart(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RUN_KEY);
            if (k == null) return false;
            if (on) k.SetValue(RUN_NAME, $"\"{Application.ExecutablePath}\" /autostart");
            else k.DeleteValue(RUN_NAME, false);
            Logger.Info($"自启{(on ? "写入" : "移除")}: {Application.ExecutablePath}");
            return true;
        }
        catch (Exception ex) { Logger.Error($"自启注册表操作失败: {ex.Message}"); return false; }
    }

    // ── ini 读写(手写 key=value,无任何序列化依赖)──────

    // 便携 ini 是给用户手改的,所以只认明确的真值;
    // 旧写法 v != "0" 会把 WarnEnabled=false / 空值 / 拼错一律读成"开"。
    private static bool ParseBool(string v)
        => v is "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<string, string> ReadIni(string path)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!File.Exists(path)) return map;
            foreach (var line in File.ReadAllLines(path))
            {
                var t = line.Trim();
                if (t.Length == 0 || t.StartsWith('#') || t.StartsWith('[')) continue;
                int eq = t.IndexOf('=');
                if (eq <= 0) continue;
                map[t[..eq].Trim()] = t[(eq + 1)..].Trim();
            }
        }
        catch { /* 读失败按默认值走 */ }
        return map;
    }

    private static void WriteIni(string path, Dictionary<string, string> map)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // 先写临时文件再原子替换:进程被杀/断电不会留下半个 ini。
            // 残缺文件会被 ReadIni 按"键缺失"处理,静默回到默认值——而默认动作恰是最危险的"关机"。
            var tmp = path + ".tmp";
            File.WriteAllLines(tmp, map.Select(kv => $"{kv.Key}={kv.Value}"));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }
        catch { /* 写失败静默:设置工具不允许因 IO 抛异常打断运行 */ }
    }

    private static List<int> ParsePoints(string v)
    {
        var list = v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => int.TryParse(t, out var n) ? Math.Clamp(n, 1, 720) : 0)
            .Where(n => n > 0).Distinct().OrderBy(n => n).ToList();
        return list;
    }
}
