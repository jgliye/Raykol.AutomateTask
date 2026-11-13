using System.Text.Json.Serialization;

namespace Raykol.AutomateTask.Models;

/// <summary>
/// 每天的持久化执行状态，保留首次计划快照避免配置变更影响核对
/// </summary>
public sealed record ExecutionState(
    [property: JsonRequired] DateOnly Date,
    [property: JsonRequired] WorkHoursConfig Plan,
    [property: JsonRequired] int Attempts,
    [property: JsonRequired] DateTimeOffset StartedAt,
    [property: JsonRequired] DateTimeOffset? RetryAt,
    [property: JsonRequired] bool PostAttempted,
    [property: JsonRequired] bool Complete,
    [property: JsonRequired] string Result);
