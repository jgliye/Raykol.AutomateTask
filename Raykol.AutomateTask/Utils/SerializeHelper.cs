using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace Raykol.AutomateTask.Utils;

/// <summary>
/// 统一 JSON 转换和原子文件读写，空值及损坏数据由调用边界校验
/// </summary>
public static class SerializeHelper
{
    private static readonly JsonSerializerOptions defaultOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        IncludeFields = true
    };

    /// <summary>
    /// 固定共享格式，兼容旧文件字段大小写且防止调用方修改全局选项
    /// </summary>
    static SerializeHelper() => defaultOptions.MakeReadOnly(populateMissingResolver: true);

    /// <summary>
    /// 判断是否为有效的 JSON 对象或数组
    /// </summary>
    public static bool IsValidJson(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return false;

        try
        {
            using var document = JsonDocument.Parse(content);
            return document.RootElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// 序列化对象，允许通知协议使用 JSON null
    /// </summary>
    public static string Serialize<T>(T value, JsonSerializerOptions? options = null) =>
        JsonSerializer.Serialize(value, options ?? defaultOptions);

    /// <summary>
    /// 从字符串还原对象，保留 JSON 必填字段校验和原始解析异常
    /// </summary>
    public static T? Deserialize<T>(string json, JsonSerializerOptions? options = null) =>
        JsonSerializer.Deserialize<T>(json, options ?? defaultOptions);

    /// <summary>
    /// 从有界 UTF-8 数据还原对象，避免通知读取额外复制为字符串
    /// </summary>
    public static T? Deserialize<T>(ReadOnlySpan<byte> json, JsonSerializerOptions? options = null) =>
        JsonSerializer.Deserialize<T>(json, options ?? defaultOptions);

    /// <summary>
    /// 读取必需文件，空值或损坏时失败，不恢复尚未提交的临时文件
    /// </summary>
    public static T Load<T>(string filePath) => Deserialize<T>(File.ReadAllBytes(filePath))
        ?? throw new InvalidDataException("JSON 文件内容为空");

    /// <summary>
    /// 同目录唯一临时文件写穿并刷新到磁盘后原子替换
    /// </summary>
    public static void Save<T>(T value, string filePath)
    {
        filePath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var temp = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(file, value, defaultOptions);
                file.Flush(true);
            }

            File.Move(temp, filePath, true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    /// <summary>
    /// 异步写入唯一临时文件，落盘完成后原子替换
    /// </summary>
    public static async Task SaveAsync<T>(T value, string filePath)
    {
        filePath = Path.GetFullPath(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var temp = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(file, value, defaultOptions);
                await file.FlushAsync();
                file.Flush(true);
            }

            File.Move(temp, filePath, true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    /// <summary>
    /// 异步加载文件，缺失返回默认值，损坏内容直接抛出异常
    /// </summary>
    public static async Task<T?> LoadAsync<T>(string filePath) => File.Exists(filePath)
        ? Deserialize<T>(await File.ReadAllTextAsync(filePath)) : default;

    /// <summary>
    /// 从流异步加载内容，保留调用方对流生命周期的控制
    /// </summary>
    public static async Task<T?> LoadAsync<T>(Stream stream)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        return Deserialize<T>(await reader.ReadToEndAsync());
    }
}
