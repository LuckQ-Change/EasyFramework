using System;
using System.Collections.Generic;
using System.Linq;
using EasyFramework.UI;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyFramework.Editor.UI
{
    internal static partial class UIPageManifestAuthoring
    {
        /// <summary>记录页面地址和相关资源包的待确认修改。</summary>
        internal sealed class PageChange
        {
            internal UIPageManifest Manifest { get; }
            internal int PageIndex { get; }
            internal string PageId { get; }
            internal string PrefabGuid { get; }
            internal string Address { get; }
            internal string Summary { get; }
            internal PackageEdit[] Packages { get; }
            internal GameObject Prefab { get; }

            internal PageChange(UIPageManifest manifest, int pageIndex, UIPageEntry page, string address,
                string summary, PackageEdit[] packages, GameObject prefab)
            {
                Manifest = manifest;
                PageIndex = pageIndex;
                PageId = page.Id;
                PrefabGuid = page.PrefabGuid;
                Address = address;
                Summary = summary;
                Packages = packages;
                Prefab = prefab;
            }
        }

        /// <summary>记录一个直接引用包中需要移除或添加的条目。</summary>
        internal readonly struct PackageEdit
        {
            internal ReferencedAssetPackageConfig Package { get; }
            internal int OldIndex { get; }
            internal string NewLocation { get; }

            internal PackageEdit(ReferencedAssetPackageConfig package, int oldIndex, string newLocation)
            {
                Package = package;
                OldIndex = oldIndex;
                NewLocation = newLocation;
            }
        }

        #region 公共方法

        internal static PageChange PreviewPageChange(UIPageManifest manifest, int pageIndex, string address)
        {
            RequireWritableAsset(manifest, "页面清单");
            if (pageIndex < 0 || pageIndex >= manifest.Pages.Count || manifest.Pages[pageIndex] == null)
                throw new InvalidOperationException("页面登记已变化，请重新选择。");

            UIPageEntry page = manifest.Pages[pageIndex];
            string oldKey = UIPageEntry.AddressKey(page.PrefabAddress);
            string newKey = address != null ? RequireNewAddress(manifest, pageIndex, address, oldKey) : null;
            var details = new List<string> { newKey == null
                ? $"移除页面「{page.Id}」的登记（{page.PrefabAddress}）。"
                : $"将页面「{page.Id}」的地址由「{page.PrefabAddress}」改为「{newKey}」。" };
            var startupConfigs = LoadProjectAssets<FrameworkStartupConfig>();
            FrameworkStartupConfig[] owners = startupConfigs.Where(config => config.PageManifest == manifest).ToArray();
            var oldPackages = new HashSet<ReferencedAssetPackageConfig>();
            var newPackages = new HashSet<ReferencedAssetPackageConfig>();
            var externalPackages = new HashSet<AssetPackageConfig>();

            foreach (FrameworkStartupConfig config in owners)
            {
                AddPackage(config, oldKey, false, oldPackages, externalPackages);
                AddPackage(config, newKey, true, newPackages, externalPackages);
            }

            if (owners.Length == 0)
            {
                CheckStandalonePage(page, oldKey);
                details.Add("当前清单尚未关联 Startup Config，应用范围为页面清单。");
            }

            foreach (AssetPackageConfig package in externalPackages.OrderBy(package => AssetDatabase.GetAssetPath(package), StringComparer.Ordinal))
                details.Add($"外部资源包「{package.name}」（{package.Alias}）由其收集器维护，请核对收集配置。");

            string prefabPath = AssetDatabase.GUIDToAssetPath(page.PrefabGuid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (newPackages.Count > 0) ValidatePrefab(page, prefab);
            string oldLocation = LocationOf(oldKey);
            string newLocation = LocationOf(newKey);
            bool isStandaloneAddress = oldPackages.Count > 0 && IsStandaloneAddress(startupConfigs, manifest, oldKey);
            var edits = new List<PackageEdit>();
            foreach (ReferencedAssetPackageConfig package in oldPackages.Union(newPackages)
                         .OrderBy(package => AssetDatabase.GetAssetPath(package), StringComparer.Ordinal))
            {
                int removeIndex = -1;
                string appendLocation = null;
                string packagePath = AssetDatabase.GetAssetPath(package);
                if (oldPackages.Contains(package))
                {
                    int index = FindEntryIndex(package, oldLocation);
                    if (index >= 0)
                    {
                        if (!IsPagePrefab(package.Entries[index].Asset, page.PrefabGuid))
                            details.Add($"{packagePath} 保留「{oldLocation}」，该条目指向其他资源。");
                        else if (isStandaloneAddress || HasResidentConsumer(startupConfigs, package, oldKey) ||
                            FindOtherPages(startupConfigs, manifest, pageIndex, package, oldKey).Any())
                            details.Add($"{packagePath} 保留共用地址「{oldLocation}」。");
                        else
                        {
                            removeIndex = index;
                            details.Add($"{packagePath} 移除「{oldLocation}」。");
                        }
                    }
                }
                if (newPackages.Contains(package))
                {
                    UIPageEntry conflict = FindOtherPages(startupConfigs, manifest, pageIndex, package, newKey)
                        .FirstOrDefault(other => other.PrefabGuid != page.PrefabGuid);
                    if (conflict != null)
                        throw new InvalidOperationException($"直接引用包「{package.name}」的地址「{newLocation}」已由页面「{conflict.Id}」用于其他 Prefab。");
                    int index = FindEntryIndex(package, newLocation);
                    if (index < 0)
                    {
                        appendLocation = newLocation;
                        details.Add($"{packagePath} 登记「{newLocation}」。");
                    }
                    else if (IsPagePrefab(package.Entries[index].Asset, page.PrefabGuid))
                        details.Add($"{packagePath} 复用「{newLocation}」。");
                    else
                        throw new InvalidOperationException($"直接引用包「{package.name}」的地址「{newLocation}」已指向其他资源。");
                }
                if (removeIndex < 0 && appendLocation == null) continue;
                RequireWritableAsset(package, "直接引用包");
                edits.Add(new PackageEdit(package, removeIndex, appendLocation));
            }

            return new PageChange(manifest, pageIndex, page, newKey, string.Join("\n", details), edits.ToArray(), prefab);
        }

        internal static void ApplyPageChange(PageChange preview)
        {
            PageChange change = PreviewPageChange(preview.Manifest, preview.PageIndex, preview.Address);
            if (change.PageId != preview.PageId || change.PrefabGuid != preview.PrefabGuid ||
                change.Summary != preview.Summary)
                throw new InvalidOperationException("页面或资源包登记已变化，请重新预览。");

            string action = change.Address == null ? "移除页面登记" : "修改页面地址";
            Object[] assets = new Object[] { change.Manifest }.Concat(change.Packages.Select(edit => (Object)edit.Package)).ToArray();
            string[] snapshots = assets.Select(asset => EditorJsonUtility.ToJson(asset)).ToArray();
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(action);
            Undo.RegisterCompleteObjectUndo(assets, action);
            try
            {
                RewriteManifest(change);
                foreach (PackageEdit edit in change.Packages) RewritePackage(edit, change.Prefab);
                foreach (Object asset in assets) Save(asset);
                Undo.CollapseUndoOperations(undoGroup);
            }
            catch (Exception exception)
            {
                var rollbackErrors = new List<Exception>();
                for (int i = 0; i < assets.Length; i++) Restore(assets[i], snapshots[i], rollbackErrors);
                try { Undo.RevertAllDownToGroup(undoGroup); }
                catch (Exception undoError) { rollbackErrors.Add(undoError); }
                if (rollbackErrors.Count > 0)
                {
                    rollbackErrors.Insert(0, exception);
                    throw new AggregateException("页面修改失败且恢复未完成，请核对页面清单和直接引用包。", rollbackErrors);
                }
                throw;
            }
            finally { Invalidate(); }
        }

        #endregion

        #region 私有逻辑

        private static string RequireNewAddress(UIPageManifest manifest, int pageIndex, string address, string oldKey)
        {
            string key = UIPageEntry.AddressKey(address);
            if (key == null) throw new InvalidOperationException($"页面地址「{address}」格式无效。");
            if (key == oldKey) throw new InvalidOperationException("页面地址未发生变化。");
            for (int i = 0; i < manifest.Pages.Count; i++)
            {
                UIPageEntry other = manifest.Pages[i];
                if (i != pageIndex && other != null && UIPageEntry.AddressKey(other.PrefabAddress) == key)
                    throw new InvalidOperationException($"资源地址「{key}」已由页面「{other.Id}」使用。");
            }
            return key;
        }

        private static void AddPackage(FrameworkStartupConfig owner, string key, bool isRequired,
            HashSet<ReferencedAssetPackageConfig> references, HashSet<AssetPackageConfig> external)
        {
            if (key == null) return;
            string alias = key.Substring(0, key.IndexOf(':'));
            AssetPackageConfig[] matches = owner.AssetPackages.Where(package => package != null && package.Alias?.Trim() == alias).ToArray();
            if (matches.Length > 1)
                throw new InvalidOperationException($"Startup Config「{owner.name}」重复登记资源包别名「{alias}」。");
            if (matches.Length == 0)
            {
                if (isRequired) throw new InvalidOperationException($"Startup Config「{owner.name}」缺少资源包「{alias}」。");
                return;
            }
            if (matches[0] is ReferencedAssetPackageConfig local) references.Add(local);
            else external.Add(matches[0]);
        }

        private static IEnumerable<UIPageEntry> FindOtherPages(FrameworkStartupConfig[] configs, UIPageManifest manifest,
            int pageIndex, ReferencedAssetPackageConfig package, string key)
        {
            foreach (UIPageManifest other in configs.Where(config => config.AssetPackages.Contains(package))
                         .Select(config => config.PageManifest).Where(value => value != null).Distinct())
            {
                for (int i = 0; i < other.Pages.Count; i++)
                    if ((other != manifest || i != pageIndex) &&
                        UIPageEntry.AddressKey(other.Pages[i]?.PrefabAddress) == key)
                        yield return other.Pages[i];
            }
        }

        private static bool HasResidentConsumer(FrameworkStartupConfig[] configs, ReferencedAssetPackageConfig package, string key) =>
            configs.Any(config => config.AssetPackages.Contains(package) &&
                config.ResidentAssets.Any(address => UIPageEntry.AddressKey(address) == key));

        private static bool IsStandaloneAddress(FrameworkStartupConfig[] configs, UIPageManifest manifest, string key)
        {
            // 独立清单没有包绑定时，保留可能由它使用的同地址条目。
            var bound = new HashSet<UIPageManifest>(configs.Select(config => config.PageManifest));
            return LoadProjectAssets<UIPageManifest>().Any(other => other != manifest && !bound.Contains(other) &&
                other.Pages.Any(page => UIPageEntry.AddressKey(page?.PrefabAddress) == key));
        }

        private static void CheckStandalonePage(UIPageEntry page, string key)
        {
            if (key == null) return;
            string alias = key.Substring(0, key.IndexOf(':'));
            string location = LocationOf(key);
            bool hasReference = LoadProjectAssets<ReferencedAssetPackageConfig>().Any(package =>
                package.Alias?.Trim() == alias && package.Entries != null && package.Entries.Any(entry =>
                    entry?.Address?.Trim() == location && IsPagePrefab(entry.Asset, page.PrefabGuid)));
            if (hasReference)
                throw new InvalidOperationException("该清单的页面已进入直接引用包。请先通过 Startup Config 关联清单和资源包，再修改或移除登记。");
        }

        private static int FindEntryIndex(ReferencedAssetPackageConfig package, string location)
        {
            IReadOnlyList<ReferencedAssetPackageConfig.Entry> entries = package.Entries
                ?? throw new InvalidOperationException($"直接引用包「{package.name}」的条目列表无效。");
            int index = -1;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i]?.Address?.Trim() != location) continue;
                if (index >= 0) throw new InvalidOperationException($"直接引用包「{package.name}」中地址「{location}」重复。");
                index = i;
            }
            return index;
        }

        private static string LocationOf(string key) => key == null ? null : key.Substring(key.IndexOf(':') + 1);

        private static bool IsPagePrefab(Object asset, string guid) => asset is GameObject prefab && prefab != null &&
            AssetDatabase.IsMainAsset(prefab) && AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab)) == guid;

        private static T[] LoadProjectAssets<T>() where T : Object => AssetDatabase.FindAssets($"t:{typeof(T).Name}")
            .Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path, StringComparer.Ordinal)
            .SelectMany(AssetDatabase.LoadAllAssetsAtPath).OfType<T>().ToArray();

        private static void RewriteManifest(PageChange change)
        {
            using var data = new SerializedObject(change.Manifest);
            SerializedProperty pages = data.FindProperty("_pages");
            if (change.Address == null) pages.DeleteArrayElementAtIndex(change.PageIndex);
            else pages.GetArrayElementAtIndex(change.PageIndex).FindPropertyRelative("_prefabAddress").stringValue = change.Address;
            data.ApplyModifiedPropertiesWithoutUndo();
            change.Manifest.Validate();
        }

        private static void RewritePackage(PackageEdit edit, GameObject prefab)
        {
            if (edit.OldIndex < 0)
            {
                AppendPackageEntry(edit.Package, edit.NewLocation, prefab);
                return;
            }
            using var data = new SerializedObject(edit.Package);
            SerializedProperty entries = data.FindProperty("_entries");
            if (edit.NewLocation == null) entries.DeleteArrayElementAtIndex(edit.OldIndex);
            else entries.GetArrayElementAtIndex(edit.OldIndex)
                .FindPropertyRelative(nameof(ReferencedAssetPackageConfig.Entry.Address)).stringValue = edit.NewLocation;
            if (!data.ApplyModifiedPropertiesWithoutUndo())
                throw new InvalidOperationException($"无法更新直接引用包「{edit.Package.name}」。");
        }

        #endregion
    }
}
