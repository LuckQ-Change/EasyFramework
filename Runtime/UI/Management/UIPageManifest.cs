using System;
using System.Collections.Generic;
using UnityEngine;

namespace EasyFramework.UI
{
    /// <summary>一个可导航页面的唯一登记，记录资源定位、创建工厂与返回行为。</summary>
    [Serializable]
    public sealed class UIPageEntry
    {
        [SerializeField] private string _id;
        [Tooltip("用于在编辑器中定位 Prefab 和验证资源地址。")]
        [SerializeField] private string _prefabGuid;
        [SerializeField] private string _prefabAddress;
        [Tooltip("由 View 脚本 GUID 生成，随 View 脚本移动保持不变。")]
        [SerializeField] private string _factoryKey;
        [SerializeField] private UIPageKind _kind;
        [SerializeField] private bool _isCloseOnBack = true;
        [SerializeField] private bool _isCloseOnBackground;

        /// <summary>调用方用来定位页面的稳定 ID。</summary>
        public string Id => _id?.Trim();
        /// <summary>构建时校验资源地址实际指向的 Prefab GUID。</summary>
        public string PrefabGuid => NormalizeGuid(_prefabGuid);
        /// <summary>加载页面实例的资源地址，支持包别名。</summary>
        public string PrefabAddress => _prefabAddress?.Trim();
        /// <summary>对应生成代码中的显式页面工厂键。</summary>
        public string FactoryKey => NormalizeGuid(_factoryKey);
        /// <summary>页面使用主界面栈或模态弹窗栈。</summary>
        public UIPageKind Kind => _kind;
        /// <summary>页面位于顶部时，返回动作是否关闭页面。</summary>
        public bool CanCloseOnBack => _isCloseOnBack;
        /// <summary>弹窗是否响应点击公共遮罩关闭。</summary>
        public bool CanCloseOnBackground => _isCloseOnBackground;

        /// <summary>创建供页面清单登记的入口。</summary>
        /// <param name="id">稳定页面 ID。</param>
        /// <param name="prefabGuid">Prefab 资产 GUID。</param>
        /// <param name="prefabAddress">资源系统使用的 Prefab 地址。</param>
        /// <param name="factoryKey">生成工厂使用的 View 脚本 GUID。</param>
        /// <param name="kind">主界面或弹窗的导航类型。</param>
        /// <param name="isCloseOnBack">是否允许返回关闭。</param>
        /// <param name="isCloseOnBackground">弹窗是否允许点击遮罩关闭。</param>
        public UIPageEntry(string id, string prefabGuid, string prefabAddress, string factoryKey, UIPageKind kind,
            bool isCloseOnBack = true, bool isCloseOnBackground = false)
        {
            _id = id?.Trim();
            _prefabGuid = NormalizeGuid(prefabGuid);
            _prefabAddress = prefabAddress?.Trim();
            _factoryKey = NormalizeGuid(factoryKey);
            _kind = kind;
            _isCloseOnBack = isCloseOnBack;
            _isCloseOnBackground = isCloseOnBackground;
        }

        internal static string NormalizeGuid(string value) => value?.Trim().ToLowerInvariant();

        internal static string AddressKey(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return null;
            int separator = address.IndexOf(':');
            string alias = separator < 0 ? AssetModule.MainPackage : address.Substring(0, separator).Trim();
            string location = (separator < 0 ? address : address.Substring(separator + 1)).Trim();
            if (string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(location) || location.IndexOf(':') >= 0)
                return null;
            return $"{alias}:{location}";
        }
    }

    /// <summary>启动配置引用的页面清单，供编辑器、运行时和构建共同使用。</summary>
    [CreateAssetMenu(fileName = "UIPageManifest", menuName = "EasyFramework/UI/Page Manifest")]
    public sealed class UIPageManifest : ScriptableObject
    {
        /// <summary>记录同一份清单中的稳定键，避免开页时扫描 Prefab 或类型。</summary>
        private sealed class PageIndex
        {
            internal readonly Dictionary<string, UIPageEntry> Ids = new(StringComparer.Ordinal);
            internal readonly Dictionary<string, UIPageEntry> Prefabs = new(StringComparer.Ordinal);
            internal readonly Dictionary<string, UIPageEntry> Factories = new(StringComparer.Ordinal);
            private readonly Dictionary<string, UIPageEntry> _addresses = new(StringComparer.Ordinal);

            internal PageIndex(IReadOnlyList<UIPageEntry> pages)
            {
                for (int i = 0; i < pages.Count; i++)
                    Add(pages[i], i + 1);
            }

            private void Add(UIPageEntry page, int ordinal)
            {
                if (page == null || string.IsNullOrWhiteSpace(page.Id))
                    throw new InvalidOperationException($"页面清单第 {ordinal} 项缺少 ID。");
                if (!Guid.TryParseExact(page.PrefabGuid, "N", out _) ||
                    !Guid.TryParseExact(page.FactoryKey, "N", out _))
                    throw new InvalidOperationException($"页面「{page.Id}」的 Prefab 或工厂 GUID 无效。");
                if (!UIBindingRoot.IsPageKind(page.Kind) || (page.Kind == UIPageKind.Screen && page.CanCloseOnBackground))
                    throw new InvalidOperationException($"页面「{page.Id}」的导航配置无效。");

                string addressKey = UIPageEntry.AddressKey(page.PrefabAddress);
                if (addressKey == null)
                    throw new InvalidOperationException($"页面「{page.Id}」需要有效的 Prefab 地址。");
                if (Ids.TryGetValue(page.Id, out UIPageEntry existing))
                    throw Conflict($"页面 ID「{page.Id}」重复", existing, page);
                if (Prefabs.TryGetValue(page.PrefabGuid, out existing))
                    throw Conflict("Prefab 重复登记", existing, page);
                if (Factories.TryGetValue(page.FactoryKey, out existing))
                    throw Conflict("View 工厂重复登记", existing, page);
                if (_addresses.TryGetValue(addressKey, out existing))
                    throw Conflict($"资源地址「{addressKey}」重复", existing, page);

                Ids.Add(page.Id, page);
                Prefabs.Add(page.PrefabGuid, page);
                Factories.Add(page.FactoryKey, page);
                _addresses.Add(addressKey, page);
            }

            private static InvalidOperationException Conflict(string reason, UIPageEntry existing, UIPageEntry incoming) =>
                new($"{reason}。已有页面「{existing.Id}」，冲突项「{incoming.Id}」。");
        }

        [SerializeField] private List<UIPageEntry> _pages = new();
        [NonSerialized] private PageIndex _index;

        /// <summary>此启动配置可导航的完整页面记录。</summary>
        public IReadOnlyList<UIPageEntry> Pages => _pages != null ? _pages : Array.Empty<UIPageEntry>();

        #region 公共方法

        /// <summary>校验页面标识、Prefab、工厂、资源地址及导航声明。</summary>
        public void Validate() => _index = new PageIndex(Pages);

        #endregion

        #region 私有逻辑

        internal UIPageEntry FindById(string id) => Find(GetIndex().Ids, id?.Trim());
        internal UIPageEntry FindByPrefabGuid(string guid) => Find(GetIndex().Prefabs, UIPageEntry.NormalizeGuid(guid));
        internal UIPageEntry FindByFactoryKey(string key) => Find(GetIndex().Factories, UIPageEntry.NormalizeGuid(key));

        internal void Register(UIPageEntry entry)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            // 候选清单通过校验后再提交，避免登记失败影响已有页面。
            var next = new List<UIPageEntry>(Pages) { entry };
            var index = new PageIndex(next);
            _pages = next;
            _index = index;
        }

        private PageIndex GetIndex() => _index ??= new PageIndex(Pages);

        private static UIPageEntry Find(Dictionary<string, UIPageEntry> entries, string key) =>
            key != null && entries.TryGetValue(key, out UIPageEntry entry) ? entry : null;

        #endregion

        #region 生命周期

        private void OnValidate() => _index = null;

        #endregion
    }
}
