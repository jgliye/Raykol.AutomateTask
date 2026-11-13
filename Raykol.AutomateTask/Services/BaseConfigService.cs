using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.Logging;
using Raykol.AutomateTask.Enums;
using Raykol.AutomateTask.Models;
using Raykol.AutomateTask.Utils;

namespace Raykol.AutomateTask.Services
{
    /// <summary>
    /// 基础配置存储服务实现
    /// </summary>
    public class BaseConfigService<T>(ILogger<T> logger, IFreeSql orm)
        : IBaseConfigService<T> where T : class
    {
        private readonly ILogger<T> logger = logger;
        private readonly IFreeSql orm = orm;
        private static readonly JsonSerializerOptions jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            IncludeFields = true,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        /// <summary>
        /// 数据库访问入口
        /// </summary>
        private IFreeSql Orm => orm;

        /// <summary>
        /// 以配置类型名称作为默认键
        /// </summary>
        public string Key { get; } = typeof(T).Name;

        /// <summary>
        /// 同步读取指定键，损坏内容交由调用边界处理
        /// </summary>
        public T? GetByKey(string key = "", BaseConfigType type = BaseConfigType.Default)
        {
            var searchKey = string.IsNullOrEmpty(key) ? Key : key;
            var entry = Orm.Select<BaseConfigEntry>().Where(item => item.Key == searchKey && item.Type == type).First();
            return entry == null ? null : Deserialize(entry);
        }

        /// <summary>
        /// 串行保存当前类型对象，返回前已提交数据库
        /// </summary>
        public void Save(T content, BaseConfigType type = BaseConfigType.Default)
            => SaveByKey(Key, content, type);

        /// <summary>
        /// 串行保存指定键的完整对象
        /// </summary>
        public void SaveByKey(string key, T content, BaseConfigType type = BaseConfigType.Default)
        {
            ArgumentNullException.ThrowIfNull(content);
            BaseConfigLock.Semaphore.Wait();

            try
            {
                var entry = Orm.Select<BaseConfigEntry>().Where(item => item.Key == key && item.Type == type).First()
                    ?? new BaseConfigEntry { Key = key, Type = type };
                entry.Content = SerializeHelper.Serialize(content, jsonOptions);
                entry.UpdateTime = DateTime.Now;
                Orm.InsertOrUpdate<BaseConfigEntry>().SetSource(entry).ExecuteAffrows();
            }
            finally
            {
                BaseConfigLock.Semaphore.Release();
            }
        }

        /// <summary>
        /// 按键删除配置，与保存操作共用写入锁
        /// </summary>
        public bool DeleteByKey(string key, BaseConfigType type = BaseConfigType.Default)
        {
            BaseConfigLock.Semaphore.Wait();

            try
            {
                return Orm.Delete<BaseConfigEntry>().Where(item => item.Key == key && item.Type == type).ExecuteAffrows() > 0;
            }
            finally
            {
                BaseConfigLock.Semaphore.Release();
            }
        }

        #region 获取数据
        /// <summary>
        /// 按 Id 获取配置内容
        /// </summary>
        public async Task<T?> GetByIdAsync(long id)
        {
            var entity = await Orm.Select<BaseConfigEntry>().Where(m => m.Id == id).FirstAsync();
            if (entity == null)
                return null;
            return Deserialize(entity);
        }

        /// <summary>
        /// 按指定键获取配置内容
        /// </summary>
        public async Task<T?> GetByKeyAsync(string key = "", BaseConfigType type = BaseConfigType.Default)
        {
            var searchKey = string.IsNullOrEmpty(key) ? Key : key;
            var entity = await Orm.Select<BaseConfigEntry>().Where(m => m.Key == searchKey && m.Type == type).FirstAsync();
            if (entity == null)
                return null;
            return Deserialize(entity);
        }

        /// <summary>
        /// 按名称获取配置内容
        /// </summary>
        public async Task<T?> GetByNameAsync(string name, BaseConfigType type)
        {
            var entity = await Orm.Select<BaseConfigEntry>().Where(m => m.Type == type && m.Name == name).FirstAsync();
            if (entity == null)
                return null;
            return Deserialize(entity);
        }

        /// <summary>
        /// 按类型查询所有配置条目
        /// </summary>
        public async Task<List<BaseConfigEntry>> GetListByTypeAsync(BaseConfigType type)
        {
            return await Orm.Select<BaseConfigEntry>().Where(e => e.Type == type).OrderByDescending(e => e.CreateTime).ToListAsync();
        }

        #endregion

        #region 保存数据
        /// <summary>
        /// 保存当前类型的配置
        /// </summary>
        public Task SaveAsync(T content, BaseConfigType type = BaseConfigType.Default, bool isLog = false)
        {
            return SaveByKeyAsync(Key, content, type, isLog);
        }

        /// <summary>
        /// 按键保存配置
        /// </summary>
        public async Task SaveByKeyAsync(string key, T content, BaseConfigType type = BaseConfigType.Default, bool isLog = false)
        {
            ArgumentNullException.ThrowIfNull(content);
            await BaseConfigLock.Semaphore.WaitAsync();
            try
            {
                var json = SerializeHelper.Serialize(content, jsonOptions);
                if (isLog)
                    logger.LogDebug("保存配置：{Key} - {Type}", key, type);

                var entry = await Orm.Select<BaseConfigEntry>().Where(m => m.Key == key && m.Type == type).FirstAsync();
                entry ??= new BaseConfigEntry();

                entry.Key = key;
                entry.Type = type;
                entry.Content = json;
                entry.UpdateTime = DateTime.Now;
                await Orm.InsertOrUpdate<BaseConfigEntry>().SetSource(entry).ExecuteAffrowsAsync();
            }
            finally
            {
                BaseConfigLock.Semaphore.Release();
            }
        }

        /// <summary>
        /// 按名称保存配置
        /// </summary>
        public async Task SaveByNameAsync(string name, T content, BaseConfigType type = BaseConfigType.Default)
        {
            ArgumentNullException.ThrowIfNull(content);
            await BaseConfigLock.Semaphore.WaitAsync();
            try
            {
                var json = SerializeHelper.Serialize(content, jsonOptions);

                var entry = await Orm.Select<BaseConfigEntry>().Where(m => m.Type == type && m.Name == name).FirstAsync();
                entry ??= new BaseConfigEntry();

                entry.Key = null;
                entry.Name = name;
                entry.Type = type;
                entry.Content = json;
                entry.UpdateTime = DateTime.Now;
                await Orm.InsertOrUpdate<BaseConfigEntry>().SetSource(entry).ExecuteAffrowsAsync();
            }
            finally
            {
                BaseConfigLock.Semaphore.Release();
            }
        }
        #endregion

        #region 序列化处理
        /// <summary>
        /// 反序列化配置内容
        /// </summary>
        private T? Deserialize(BaseConfigEntry entity)
        {
            if (string.IsNullOrWhiteSpace(entity.Content))
            {
                throw new InvalidDataException("配置内容为空");
            }

            try
            {
                return SerializeHelper.Deserialize<T>(entity.Content, jsonOptions)
                    ?? throw new InvalidDataException("配置内容为空");
            }
            catch (JsonException)
            {
                logger.LogWarning("配置内容 JSON 解析失败：{Key} - {Type} - {Id}", entity.Key, entity.Type, entity.Id);
                throw;
            }
            catch (NotSupportedException)
            {
                logger.LogWarning("配置内容反序列化失败：{Key} - {Type} - {Id}", entity.Key, entity.Type, entity.Id);
                throw;
            }
        }
        #endregion

        #region 其他
        /// <summary>
        /// 检查同类型下配置名称是否已存在
        /// </summary>
        public async Task<bool> ExistsByNameAsync(string name, BaseConfigType type)
        {
            return await Orm.Select<BaseConfigEntry>().Where(e => e.Type == type && e.Name == name).AnyAsync();
        }

        /// <summary>
        /// 按 Id 删除配置
        /// </summary>
        public async Task<bool> DeleteByIdAsync(long id)
        {
            await BaseConfigLock.Semaphore.WaitAsync();

            try
            {
                return await Orm.Delete<BaseConfigEntry>().Where(entry => entry.Id == id).ExecuteAffrowsAsync() > 0;
            }
            finally
            {
                BaseConfigLock.Semaphore.Release();
            }
        }

        /// <summary>
        /// 按 Id 重命名配置
        /// </summary>
        public async Task RenameAsync(long id, string name)
        {
            await BaseConfigLock.Semaphore.WaitAsync();
            try
            {
                var entry = await Orm.Select<BaseConfigEntry>().Where(e => e.Id == id).FirstAsync();
                if (entry == null)
                    return;

                if (entry.Name == name)
                    return;

                var exists = await Orm.Select<BaseConfigEntry>()
                                      .Where(e => e.Id != id && e.Type == entry.Type && e.Name == name)
                                      .AnyAsync();
                if (exists)
                    throw new InvalidOperationException("已存在同名配置");

                entry.Name = name;
                entry.UpdateTime = DateTime.Now;
                await Orm.Update<BaseConfigEntry>().SetSource(entry).ExecuteAffrowsAsync();
            }
            finally
            {
                BaseConfigLock.Semaphore.Release();
            }
        }
        #endregion

    }

    /// <summary>
    /// 配置写入共用锁
    /// </summary>
    internal static class BaseConfigLock
    {
        /// <summary>
        /// 不同配置类型共用写入锁
        /// </summary>
        public static readonly SemaphoreSlim Semaphore = new(1, 1);
    }
}
