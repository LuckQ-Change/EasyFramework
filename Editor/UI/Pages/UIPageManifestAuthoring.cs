using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EasyFramework.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyFramework.Editor.UI
{
    /// <summary>登记新页面及其直接资源引用，并按 Prefab GUID 查找页面清单。</summary>
    [InitializeOnLoad]
    internal static partial class UIPageManifestAuthoring
    {
        private static UIPageManifest[] _manifests;

        static UIPageManifestAuthoring() => EditorApplication.projectChanged += Invalidate;

        #region 公共方法

        internal static UIPageManifest FindManifest(UIBindingRoot display, out UIPageEntry entry)
        {
            entry = null;
            if (display == null || !display.IsPage) return null;
            string path = GetPrefabPath(display.gameObject);
            if (string.IsNullOrEmpty(path)) return null;
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid)) return null;

            UIPageManifest preferred = UIAuthoringSettings.instance.PageManifest;
            entry = FindEntry(preferred, guid);
            if (entry != null) return preferred;

            _manifests ??= AssetDatabase.FindAssets("t:UIPageManifest")
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(value => value, StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<UIPageManifest>)
                .Where(value => value != null).ToArray();
            foreach (UIPageManifest manifest in _manifests)
            {
                if (manifest == preferred) continue;
                entry = FindEntry(manifest, guid);
                if (entry != null) return manifest;
            }
            return null;
        }

        internal static void ValidateNewPage(UIPageManifest manifest, string pageId, string address,
            ReferencedAssetPackageConfig package)
        {
            ValidateRegistration(manifest, pageId, address, package);
        }

        internal static void Register(UIPageManifest manifest, UIPageEntry entry,
            ReferencedAssetPackageConfig package, GameObject prefab)
        {
            if (entry == null) throw new ArgumentNullException(nameof(entry));
            string location = ValidateRegistration(manifest, entry.Id, entry.PrefabAddress, package);
            bool hasPackage = package != null;
            if (hasPackage) ValidatePrefab(entry, prefab);

            string previousManifest = EditorJsonUtility.ToJson(manifest);
            string previousPackage = hasPackage ? EditorJsonUtility.ToJson(package) : null;
            manifest.Register(entry);
            try
            {
                // 两份登记均写入内存后再持久化，确保清单指向的包内地址存在。
                if (hasPackage)
                {
                    AppendPackageEntry(package, location, prefab);
                    Save(package);
                }
                Save(manifest);
            }
            catch (Exception exception)
            {
                var rollbackErrors = new List<Exception>();
                Restore(manifest, previousManifest, rollbackErrors);
                if (hasPackage) Restore(package, previousPackage, rollbackErrors);
                if (rollbackErrors.Count > 0)
                {
                    rollbackErrors.Insert(0, exception);
                    throw new AggregateException("页面登记失败且恢复未完成，请核对页面清单和直接引用包。", rollbackErrors);
                }
                throw;
            }
            finally { Invalidate(); }
        }

        #endregion

        #region 私有逻辑

        private static string ValidateRegistration(UIPageManifest manifest, string pageId, string address,
            ReferencedAssetPackageConfig package)
        {
            RequireWritableAsset(manifest, "页面清单");
            string id = pageId?.Trim();
            if (string.IsNullOrEmpty(id)) throw new InvalidOperationException("请填写页面 ID。");
            string addressKey = UIPageEntry.AddressKey(address);
            if (addressKey == null) throw new InvalidOperationException($"页面地址「{address}」格式无效。");

            manifest.Validate();
            if (manifest.FindById(id) != null)
                throw new InvalidOperationException($"页面 ID「{id}」已在「{manifest.name}」登记。");
            UIPageEntry existing = manifest.Pages.FirstOrDefault(page => UIPageEntry.AddressKey(page.PrefabAddress) == addressKey);
            if (existing != null)
                throw new InvalidOperationException($"资源地址「{addressKey}」已由页面「{existing.Id}」使用。");

            if (package == null) return null;
            RequireWritableAsset(package, "直接引用包");
            int separator = addressKey.IndexOf(':');
            string alias = addressKey.Substring(0, separator);
            string location = addressKey.Substring(separator + 1);
            if (!string.Equals(package.Alias?.Trim(), alias, StringComparison.Ordinal))
                throw new InvalidOperationException($"地址「{addressKey}」与直接引用包「{package.name}」的别名「{package.Alias}」不一致。");

            IReadOnlyList<ReferencedAssetPackageConfig.Entry> entries = package.Entries
                ?? throw new InvalidOperationException($"直接引用包「{package.name}」的条目列表无效。");
            var addresses = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                ReferencedAssetPackageConfig.Entry asset = entries[i];
                if (asset?.Asset == null || string.IsNullOrWhiteSpace(asset.Address))
                    throw new InvalidOperationException($"直接引用包「{package.name}」第 {i + 1} 条缺少包内地址或资源引用。");
                string key = asset.Address.Trim();
                if (!addresses.Add(key))
                    throw new InvalidOperationException($"直接引用包「{package.name}」的包内地址「{key}」重复。");
            }
            if (addresses.Contains(location))
                throw new InvalidOperationException($"直接引用包「{package.name}」已占用包内地址「{location}」。");
            return location;
        }

        private static void RequireWritableAsset(Object asset, string label)
        {
            string path = asset != null ? AssetDatabase.GetAssetPath(asset) : null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                throw new InvalidOperationException($"请先保存{label}资产。");
            if (!AssetDatabase.IsOpenForEdit(path) || new FileInfo(path).IsReadOnly)
                throw new InvalidOperationException($"{label}当前不可写：{path}。");
        }

        private static void ValidatePrefab(UIPageEntry entry, GameObject prefab)
        {
            string path = prefab != null ? AssetDatabase.GetAssetPath(prefab) : null;
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
                !AssetDatabase.IsMainAsset(prefab) || AssetDatabase.AssetPathToGUID(path) != entry.PrefabGuid)
                throw new InvalidOperationException($"页面「{entry.Id}」的直接资源引用需要指向清单登记的 Prefab 资产。");
        }

        private static void AppendPackageEntry(ReferencedAssetPackageConfig package, string location, GameObject prefab)
        {
            using var data = new SerializedObject(package);
            SerializedProperty entries = data.FindProperty("_entries");
            int index = entries.arraySize;
            entries.arraySize++;
            SerializedProperty entry = entries.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative(nameof(ReferencedAssetPackageConfig.Entry.Address)).stringValue = location;
            entry.FindPropertyRelative(nameof(ReferencedAssetPackageConfig.Entry.Asset)).objectReferenceValue = prefab;
            if (!data.ApplyModifiedPropertiesWithoutUndo())
                throw new InvalidOperationException($"无法写入直接引用包「{package.name}」的地址「{location}」。");
        }

        private static void Save(Object asset)
        {
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
            if (EditorUtility.IsDirty(asset))
                throw new IOException($"无法保存资产：{AssetDatabase.GetAssetPath(asset)}。");
        }

        private static void Restore(Object asset, string snapshot, List<Exception> errors)
        {
            try
            {
                if (EditorJsonUtility.ToJson(asset) == snapshot) return;
                EditorJsonUtility.FromJsonOverwrite(snapshot, asset);
                if (asset is UIPageManifest manifest) manifest.Validate();
                Save(asset);
            }
            catch (Exception exception)
            {
                errors.Add(new InvalidOperationException($"无法恢复资产：{AssetDatabase.GetAssetPath(asset)}。", exception));
            }
        }

        private static void Invalidate() => _manifests = null;

        private static UIPageEntry FindEntry(UIPageManifest manifest, string guid) => manifest == null ? null :
            manifest.Pages.FirstOrDefault(entry => entry?.PrefabGuid == guid);

        private static string GetPrefabPath(GameObject root)
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot == root) return stage.assetPath;
            if (EditorUtility.IsPersistent(root) && root.transform.parent == null) return AssetDatabase.GetAssetPath(root);
            return PrefabUtility.GetNearestPrefabInstanceRoot(root) == root
                ? PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root)
                : null;
        }

        #endregion
    }
}
