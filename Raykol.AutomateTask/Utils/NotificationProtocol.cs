using System.Security.Cryptography;
using System.Text;

namespace Raykol.AutomateTask.Utils;

/// <summary>
/// 服务和托盘共用的本机只读通知通道约定
/// </summary>
public static class NotificationProtocol
{
    /// <summary>
    /// 限制单条通知大小，避免客户端无限读取
    /// </summary>
    public const int MaxMessageBytes = 1024;

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
