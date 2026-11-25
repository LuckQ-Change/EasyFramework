using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EasyFramework.UI;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyFramework.Editor.UI
{
    internal sealed class UIValidationIssue
    {
        internal string Message { get; }
        internal Object Context { get; }
        internal bool Blocking { get; }

        internal UIValidationIssue(string message, Object context, bool blocking = true)
        {
            Message = message;
            Context = context;
            Blocking = blocking;
        }
    }

    [InitializeOnLoad]
    internal sealed class UIValidator : IPreprocessBuildWithReport
    {
        static UIValidator() => PrefabStage.prefabSaving += BeforeSave;
        public int callbackOrder => 0;

        internal static bool ValidateAndReport(UIBindingRoot display)
        {
            var issues = FindIssues(display, validateAddress: true);
            EditorUtility.DisplayDialog("UI 校验", issues.Count == 0 ? "校验通过。" :
                string.Join("\n", issues.Select(issue => issue.Message)), "确定");
            return !issues.Any(issue => issue.Blocking);
        }

        internal static List<UIValidationIssue> FindIssues(UIBindingRoot display, bool validateAddress = false)
        {
            var issues = new List<UIValidationIssue>();
            if (display == null)
            {
                issues.Add(new UIValidationIssue("缺少 UIDisplay 或 UINode。", null));
                return issues;
            }
            Type type = UIFactory.ResolveType(display.ViewTypeName);
            if (type == null)
                issues.Add(new UIValidationIssue("View 尚未编译或类型已丢失。", display));
            else
            {
                string path = AssetDatabase.GUIDToAssetPath(display.ViewScriptGuid);
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script == null || script.GetClass() != type || type.IsAbstract || type.GetConstructor(Type.EmptyTypes) == null)
                    issues.Add(new UIValidationIssue("View 脚本与 Prefab 类型不一致，或缺少公开无参构造函数。", display));
                if (display.IsPage &&
                    (string.IsNullOrWhiteSpace(display.PrefabLocation) || UIFactory.Address(type) != display.PrefabLocation))
                    issues.Add(new UIValidationIssue("生成的页面地址与 Prefab 不一致。", display));
            }

            try
            {
                var found = UIReferenceCodeGenerator.Scan(display).ToList();
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (UIControlReference control in found)
                {
                    if (!names.Add(control.Name))
                        issues.Add(new UIValidationIssue($"控件属性 {control.Name} 重复。", control.Target));
                    UIControlReference slot = display.Controls.FirstOrDefault(value => UIReferenceCodeGenerator.SameNode(value, control));
                    if (slot == null)
                        issues.Add(new UIValidationIssue($"{control.Name} 尚未生成，请保存 Prefab。", control.Target));
                    else if (slot.Target != control.Target)
                        issues.Add(new UIValidationIssue($"{control.Name} 的组件映射已变化，请保存 Prefab。", control.Target));
                    else if (slot.Name != control.Name)
                        issues.Add(new UIValidationIssue($"{slot.Name} 已改名为 {control.Name}，可在引用页预览同步。", control.Target, false));
                }

                foreach (UIControlReference slot in display.Controls)
                    if (slot == null || slot.Target == null || found.All(control => !UIReferenceCodeGenerator.SameNode(slot, control)))
                        issues.Add(new UIValidationIssue($"引用 {slot?.Name} 已失效，检查节点是否使用了已配置的关键字。", display));

                if (type != null)
                {
                    string generated = UIReferenceCodeGenerator.GeneratedPath(display);
                    if (!File.Exists(generated) || File.ReadAllText(generated) !=
                        UIReferenceCodeGenerator.Source(display, display.Controls))
                        issues.Add(new UIValidationIssue("生成的引用尚未同步，请保存 Prefab。", display));
                }
            }
            catch (Exception exception) { issues.Add(new UIValidationIssue(exception.Message, display)); }

            foreach (Canvas canvas in display.GetComponentsInChildren<Canvas>(true))
            {
                if (canvas.gameObject == display.gameObject && display.IsPage)
                    issues.Add(new UIValidationIssue("页面根节点由框架 Canvas 管理，请把内容 Canvas 放到子节点。", canvas));
                else if (Mathf.Approximately(canvas.transform.localScale.x, 0f) ||
                         Mathf.Approximately(canvas.transform.localScale.y, 0f))
                    issues.Add(new UIValidationIssue("Canvas 缩放为 0。", canvas, false));
            }
            foreach (UITimeline timeline in display.GetComponentsInChildren<UITimeline>(true))
            {
                if (timeline.GetComponentInParent<UIBindingRoot>(true) != display) continue;
                try { timeline.ValidateConfiguration(); }
                catch (InvalidOperationException exception) { issues.Add(new UIValidationIssue(exception.Message, timeline)); }
            }
            if (display.TryGetComponent(out UITransition transition))
            {
                try { transition.ValidateTimelines(); }
                catch (InvalidOperationException exception) { issues.Add(new UIValidationIssue(exception.Message, transition)); }
            }
            foreach (UIVirtualList list in display.GetComponentsInChildren<UIVirtualList>(true))
            {
                if (list.GetComponentInParent<UIBindingRoot>(true) != display) continue;
                try { list.ValidateConfiguration(); }
                catch (InvalidOperationException exception) { issues.Add(new UIValidationIssue(exception.Message, list)); }
            }
            if (validateAddress && display is UIDisplay page && page.IsPage)
            {
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                string path = stage != null && stage.prefabContentsRoot == display.gameObject
                    ? stage.assetPath : AssetDatabase.GetAssetPath(display.gameObject);
                try
                {
                    string problem = AddressIssue(page, path, BuildPackages(), new(StringComparer.Ordinal));
                    if (problem != null) issues.Add(new UIValidationIssue(problem, display));
                }
                catch (Exception exception) { issues.Add(new UIValidationIssue(exception.GetBaseException().Message, display)); }
            }
            return issues;
        }

        private static void BeforeSave(GameObject root)
        {
            if (root == null) return;
            foreach (UIBindingRoot display in root.GetComponentsInChildren<UIBindingRoot>(true).Reverse())
            {
                GameObject instance = PrefabUtility.GetNearestPrefabInstanceRoot(display.gameObject);
                if (display.gameObject != root && instance != null && instance != root) continue;
                try { UIReferenceCodeGenerator.Sync(display); }
                catch (Exception exception) { Debug.LogWarning($"[UI] {display.name}：{exception.Message}", display); }
            }
        }

        public void OnPreprocessBuild(BuildReport report) => CheckBuild();

        internal static void CheckBuild()
        {
            var errors = new List<string>();
            var types = new HashSet<Type>();
            var addresses = new HashSet<string>(StringComparer.Ordinal);
            var packages = BuildPackages();
            var collected = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                foreach (UIBindingRoot scope in prefab.GetComponentsInChildren<UIBindingRoot>(true))
                    foreach (var issue in FindIssues(scope).Where(issue => issue.Blocking))
                        errors.Add($"{path} · {scope.name} · {issue.Message}");
                if (!prefab.TryGetComponent(out UIDisplay display) || !display.IsPage) continue;
                Type type = UIFactory.ResolveType(display.ViewTypeName);
                if (type != null && !types.Add(type)) errors.Add($"{path} · {type.Name} 关联了多个页面。");
                string address = display.PrefabLocation;
                if (string.IsNullOrWhiteSpace(address)) continue;
                var (alias, location) = SplitAddress(address);
                if (!addresses.Add($"{alias}:{location}")) errors.Add($"{path} · 资源地址 {address} 重复。");
                try
                {
                    string problem = AddressIssue(display, path, packages, collected);
                    if (problem != null) errors.Add($"{path} · {problem}");
                }
                catch (Exception exception)
                {
                    errors.Add($"{path} · 无法验证 {address}：{exception.GetBaseException().Message}");
                }
            }
            if (errors.Count != 0) throw new BuildFailedException("[UI] 构建前校验失败。\n" + string.Join("\n", errors));
        }

        private static string AddressIssue(UIDisplay display, string path, Dictionary<string, AssetPackageConfig> packages,
            Dictionary<string, Dictionary<string, string>> collected)
        {
            string address = display.PrefabLocation;
            if (string.IsNullOrWhiteSpace(address) || string.IsNullOrEmpty(path)) return null;
            var (alias, location) = SplitAddress(address);
            if (!packages.TryGetValue(alias, out var package)) return $"Startup Config 缺少资源包 {alias}。";
            return IsCollected(package, location, path, collected) ? null : $"{address} 未收集为此 Prefab。";
        }

        private static Dictionary<string, AssetPackageConfig> BuildPackages()
        {
            string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray();
            var paths = new HashSet<string>(AssetDatabase.GetDependencies(scenes, true), StringComparer.Ordinal);
            const string configFileName = FrameworkStartupConfig.ResourceName + ".asset";
            foreach (string guid in AssetDatabase.FindAssets("t:FrameworkStartupConfig"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.Equals(Path.GetFileName(path), configFileName, StringComparison.Ordinal)) paths.Add(path);
            }
            var result = new Dictionary<string, AssetPackageConfig>(StringComparer.Ordinal);
            foreach (string path in paths.Where(path => path.EndsWith(".asset", StringComparison.Ordinal)))
            {
                var startup = AssetDatabase.LoadAssetAtPath<FrameworkStartupConfig>(path);
                if (startup == null) continue;
                foreach (AssetPackageConfig package in startup.AssetPackages)
                {
                    if (package == null || string.IsNullOrWhiteSpace(package.Alias)) continue;
                    string alias = package.Alias.Trim();
                    if (result.TryGetValue(alias, out var previous) && previous != package)
                        throw new BuildFailedException($"[UI] 构建入口中的资源包别名 {alias} 存在不同配置。");
                    result[alias] = package;
                }
            }
            return result;
        }

        private static (string alias, string address) SplitAddress(string address)
        {
            int separator = address.IndexOf(':');
            return separator < 0 ? (AssetModule.MainPackage, address.Trim()) :
                (address.Substring(0, separator).Trim(), address.Substring(separator + 1).Trim());
        }

        private static bool IsCollected(AssetPackageConfig package, string address, string prefabPath,
            Dictionary<string, Dictionary<string, string>> collected)
        {
            if (package is ReferencedAssetPackageConfig references)
            {
                var entries = references.Entries.Where(entry => entry != null && entry.Address?.Trim() == address).ToArray();
                if (entries.Length > 1) throw new InvalidOperationException($"资源包 {package.Alias} 的地址 {address} 重复。");
                return entries.Length == 1 && entries[0].Asset != null && AssetDatabase.GetAssetPath(entries[0].Asset) == prefabPath;
            }
            if (package is not IYooAssetPackageConfig yooAsset)
                throw new InvalidOperationException($"资源包 {package.Alias} 暂无编辑期收集校验。");

            string packageName = yooAsset.PackageName?.Trim();
            if (string.IsNullOrEmpty(packageName))
                throw new InvalidOperationException($"资源包 {package.Alias} 缺少 YooAsset 包名。");
            if (!collected.TryGetValue(packageName, out var assets))
            {
                Type collector = Type.GetType("YooAsset.Editor.BundleCollectorSettingData, YooAsset.Editor", false)
                    ?? throw new InvalidOperationException($"资源包 {package.Alias} 的编辑期校验需要安装 YooAsset。");
                if (!(bool)collector.GetMethod("HasSettingAsset").Invoke(null, null))
                    throw new InvalidOperationException("YooAsset 缺少资源收集器配置。");
                object setting = collector.GetProperty("Setting").GetValue(null);
                object result = setting.GetType().GetMethod("BeginCollect")
                    .Invoke(setting, new object[] { packageName, false, false, true });
                assets = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (object entry in (IEnumerable)result.GetType().GetProperty("CollectAssets").GetValue(result))
                {
                    Type entryType = entry.GetType();
                    if (entryType.GetProperty("CollectorType").GetValue(entry).ToString() != "MainAssetCollector") continue;
                    string location = (string)entryType.GetProperty("Address").GetValue(entry);
                    if (string.IsNullOrEmpty(location)) continue;
                    object info = entryType.GetProperty("AssetInfo").GetValue(entry);
                    string path = (string)info.GetType().GetField("AssetPath").GetValue(info);
                    assets.Add(location, path);
                }
                collected.Add(packageName, assets);
            }
            return assets.TryGetValue(address, out string pathForAddress) && pathForAddress == prefabPath;
        }
    }
}
