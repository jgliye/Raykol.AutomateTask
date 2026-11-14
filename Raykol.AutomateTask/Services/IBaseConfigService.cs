using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Raykol.AutomateTask.Enums;
using Raykol.AutomateTask.Models;

namespace Raykol.AutomateTask.Services
{
    /// <summary>
    /// 基础配置存储服务（支持多配置管理）
    /// </summary>
    public interface IBaseConfigService<T> where T : class
    {
        /// <summary>
        /// 当前键
        /// </summary>
        string Key { get; }

        /// <summary>
        /// 同步读取指定键，供现有调度和启动流程使用
        /// </summary>
        T? GetByKey(string key = "", BaseConfigType type = BaseConfigType.Default);

        /// <summary>
        /// 按分类读取完整配置列表
        /// </summary>
        List<T> GetList(BaseConfigType type = BaseConfigType.Default);

        /// <summary>
        /// 在同一次写入锁内读取最新配置并更新，防止部分字段相互覆盖
        /// </summary>
        T UpdateByKey(string key, Func<T, T> update, BaseConfigType type = BaseConfigType.Default);

        /// <summary>
        /// 同步保存当前类型配置，返回前完成持久化
        /// </summary>
        void Save(T content, BaseConfigType type = BaseConfigType.Default);

        /// <summary>
        /// 同步保存指定键的完整配置
        /// </summary>
        void SaveByKey(string key, T content, BaseConfigType type = BaseConfigType.Default);

        /// <summary>
        /// 删除指定键的配置
        /// </summary>
        bool DeleteByKey(string key, BaseConfigType type = BaseConfigType.Default);

        /// <summary>
        /// 按 Id 获取配置内容
        /// </summary>
        Task<T?> GetByIdAsync(long id);

        /// <summary>
        /// 按指定键获取配置内容
        /// </summary>
        Task<T?> GetByKeyAsync(string key = "", BaseConfigType type = BaseConfigType.Default);

        /// <summary>
        /// 按名称获取配置内容
        /// </summary>
        Task<T?> GetByNameAsync(string name, BaseConfigType type);

        /// <summary>
        /// 按类型获取所有配置条目
        /// </summary>
        Task<List<BaseConfigEntry>> GetListByTypeAsync(BaseConfigType type);

        /// <summary>
        /// 保存配置
        /// </summary>
        Task SaveAsync(T content, BaseConfigType type = BaseConfigType.Default, bool isLog = false);

        /// <summary>
        /// 按键保存配置
        /// </summary>
        Task SaveByKeyAsync(string key, T content, BaseConfigType type = BaseConfigType.Default, bool isLog = false);

        /// <summary>
        /// 按名称保存配置
        /// </summary>
        Task SaveByNameAsync(string name, T content, BaseConfigType type = BaseConfigType.Default);

        /// <summary>
        /// 检查同类型下配置名称是否已存在
        /// </summary>
        Task<bool> ExistsByNameAsync(string name, BaseConfigType type);

        /// <summary>
        /// 按 Id 删除配置
        /// </summary>
        Task<bool> DeleteByIdAsync(long id);

        /// <summary>
        /// 按 Id 重命名配置
        /// </summary>
        Task RenameAsync(long id, string name);

    }
}
