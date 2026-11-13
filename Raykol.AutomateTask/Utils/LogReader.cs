namespace Raykol.AutomateTask.Utils;

/// <summary>
/// 为管理页读取 NLog 文件，不参与写入或轮转
/// </summary>
public sealed class LogReader(string root)
{
    /// <summary>
    /// 从最近两个文件读取末尾 200 行，允许 NLog 同时写入和清理
    /// </summary>
    public string Tail()
    {
        var directory = new DirectoryInfo(Path.Combine(root, "Logs"));

        if (!directory.Exists)
            return "暂无日志";

        var files = directory.EnumerateFiles("*.log")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.Ordinal)
            .Take(2)
            .Reverse();

        return string.Join(Environment.NewLine, files.SelectMany(file => ReadLines(file.FullName)).TakeLast(200));
    }

    /// <summary>
    /// 共享读取日志，文件恰好被保留策略清理时跳过
    /// </summary>
    private static IEnumerable<string> ReadLines(string path)
    {
        FileStream stream;

        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (FileNotFoundException)
        {
            yield break;
        }

        using var reader = new StreamReader(stream);

        while (reader.ReadLine() is { } line)
            yield return line;
    }
}
