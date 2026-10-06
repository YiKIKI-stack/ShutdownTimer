namespace ShutdownTimer;

/// <summary>
/// 统一日志:只写文件、永不弹框、永不抛异常——日志自身失败时静默吞掉。
/// 路径跟随便携设置;单文件超 1MB 轮转为 .old。
/// </summary>
public static class Logger
{
    private static readonly object Gate = new();
    private static string _path = "";

    public static void Init(bool portable)
    {
        try
        {
            _path = portable
                ? Path.Combine(AppContext.BaseDirectory, "logs", "st.log")
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ShutdownTimer", "log.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        }
        catch { _path = ""; }
    }

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg) => Write("ERROR", msg);

    private static void Write(string level, string msg)
    {
        if (_path.Length == 0) return;
        try
        {
            lock (Gate)
            {
                var fi = new FileInfo(_path);
                if (fi.Exists && fi.Length > 1_000_000)
                {
                    var old = _path + ".old";
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(_path, old);
                }
                File.AppendAllText(_path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg}{Environment.NewLine}");
            }
        }
        catch { /* 取证通路自己的失败不反噬主程序 */ }
    }

    /// <summary>托盘文本统一入口的截断规则:NotifyIcon.Text 超 63 字符直接抛异常。</summary>
    public static string TraySafe(string text)
    {
        if (text.Length <= 60) return text;
        return text[..57] + "…";
    }
}
