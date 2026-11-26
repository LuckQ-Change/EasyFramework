using UnityEngine;

namespace EasyFramework
{
    /// <summary>为组合启动链提供目标运行时使用的程序集与 AOT 元数据加载器。</summary>
    public abstract class HotUpdateRuntimeProvider : ScriptableObject
    {
        /// <summary>检查适配器资产与元数据声明，支持编辑期校验。</summary>
        /// <param name="hasAotMetadata">本次发行是否要求补充 AOT 元数据。</param>
        public virtual void ValidateConfiguration(bool hasAotMetadata) { }

        /// <summary>开始更新前检查当前运行环境是否具备启动所需能力。</summary>
        /// <param name="hasAotMetadata">本次启动是否需要实际补充 AOT 元数据。</param>
        public virtual void ValidateRuntime(bool hasAotMetadata) { }

        /// <summary>为一次启动尝试创建所需的运行时加载器。</summary>
        /// <returns>用于补充元数据和装载程序集的加载器。</returns>
        public abstract IHotfixLoader CreateLoader();
    }
}
