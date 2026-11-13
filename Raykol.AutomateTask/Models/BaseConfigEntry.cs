using System;
using FreeSql.DataAnnotations;
using Raykol.AutomateTask.Enums;

namespace Raykol.AutomateTask.Models
{
    /// <summary>
    /// 基础配置项（数据库实体，支持多配置管理）
    /// </summary>
    [Index("uk_config_key_type", "Key,Type", true)]
    public sealed class BaseConfigEntry
    {
        /// <summary>
        /// 数据唯一ID（主键）
        /// </summary>
        [Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>
        /// 配置键
        /// </summary>
        [Column(Position = 2)]
        public string? Key { get; set; }

        /// <summary>
        /// 配置类型
        /// </summary>
        [Column(Position = 3)]
        public BaseConfigType Type { get; set; } = BaseConfigType.Default;

        /// <summary>
        /// 配置名称
        /// </summary>
        [Column(Position = 4)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 配置分类
        /// </summary>
        [Column(Position = 5)]
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// JSON 内容
        /// </summary>
        [Column(MapType = typeof(string), StringLength = -1, Position = 6)]
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// 创建时间
        /// </summary>
        [Column(Position = 7)]
        public DateTime CreateTime { get; set; } = DateTime.Now;

        /// <summary>
        /// 修改时间
        /// </summary>
        [Column(Position = 8)]
        public DateTime UpdateTime { get; set; } = DateTime.Now;
    }
}
