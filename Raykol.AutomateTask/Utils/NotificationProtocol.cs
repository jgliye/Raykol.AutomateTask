using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Raykol.AutomateTask.Utils;

/// <summary>
/// 服务和托盘共用的本机只读通知通道约定
/// </summary>
public static class NotificationProtocol
{
    /// <summary>
    /// 限制单条通知大小，避免客户端无限读取
    /// </summary>
    public const int MaxMessageBytes = 65536;

    /// <summary>
    /// 有界读取单行 JSON 帧，连接中断时拒绝不完整消息
    /// </summary>
    public static async Task<T?> Read<T>(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[MaxMessageBytes];
        var length = 0;

        while (length < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(length), ct);

            if (count == 0)
                break;

            length += count;

            if (buffer[length - 1] == (byte)'\n')
                return SerializeHelper.Deserialize<T>(buffer.AsSpan(0, length));
        }

        throw new InvalidDataException("通知格式无效或消息过长");
    }

    /// <summary>
    /// 发送紧凑单行 JSON，避免全局缩进破坏帧边界
    /// </summary>
    public static async Task Write<T>(Stream stream, T value, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(SerializeHelper.Serialize(value, JsonSerializerOptions.Web) + "\n");

        if (bytes.Length >= MaxMessageBytes)
            throw new InvalidDataException("通知消息过长");

        await stream.WriteAsync(bytes, ct);
    }

    /// <summary>
    /// 按应用目录隔离开发与正式实例，路径末尾分隔符不影响标识
    /// </summary>
    public static string PipeName(string root)
    {
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)));
        return "Raykol.AutomateTask.Notifications." + hash[..24];
    }
}
