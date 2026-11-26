using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using EasyFramework.UI;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyFramework.Editor.UI
{
    /// <summary>工作台和构建入口共用的一条 UI 校验结果。</summary>
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

    /// <summary>按构建入口的页面清单校验注册项及其 Prefab。</summary>
    [InitializeOnLoad]
    internal static class UIValidator
    {
        #region 生命周期

        static UIValidator() => PrefabStage.prefabSaving += BeforeSave;

        #endregion

        #region 公共方法

        internal static bool ValidateAndReport(UIBindingRoot display)
        {
            var issues = FindIssues(display, validateAddress: true);
            EditorUtility.DisplayDialog("UI 校验", issues.Count == 0 ? "校验通过。" :
                string.Join("\n", issues.Select(issue => issue.Message)), "确定");
            return !issues.Any(issue => issue.Blocking);
        }

        internal static List<UIValidationIssue> FindIssues(UIBindingRoot display, bool validateAddress = false)
        {
            UIPageManifest manifest = null;
            UIPageEntry entry = null;
            if (display != null && display.IsPage)
                manifest = UIPageManifestAuthoring.FindManifest(display, out entry);

            List<UIValidationIssue> issues = FindBindingIssues(display, entry);
            if (validateAddress && display is UIDisplay page && page.IsPage)
                FindAddressIssues(page, manifest, entry, issues);
            return issues;
        }

        /// <summary>校验本次构建包含的启动配置所使用的页面清单。</summary>
        /// <param name="startups">本次构建的场景依赖所引用的启动配置。</param>
        internal static void CheckBuild(IReadOnlyList<FrameworkStartupConfig> startups)
        {
            var errors = new List<string>();
            var collected = new EditorPackageAddresses();
            foreach (FrameworkStartupConfig startup in startups)
                if (startup.PageManifest != null)
                    CheckManifest(startup, collected, errors);

            if (errors.Count > 0) throw new BuildFailedException("[UI] 构建前校验失败。\n" + string.Join("\n", errors));
        }

        #endregion

        #region 私有逻辑

        private static List<UIValidationIssue> FindBindingIssues(UIBindingRoot display, UIPageEntry entry)
        {
            var issues = new List<UIValidationIssue>();
            if (display == null)
            {
                issues.Add(new UIValidationIssue("缺少 UIDisplay 或 UINode。", null));
                return issues;
            }

            if (entry != null)
            {
                if (display.Kind != entry.Kind)
                    issues.Add(new UIValidationIssue($"页面 {entry.Id} 的 Prefab 用途与清单不一致。", display));
                if (!string.Equals(UIPageEntry.NormalizeGuid(display.ViewScriptGuid), entry.FactoryKey, StringComparison.Ordinal))
                    issues.Add(new UIValidationIssue($"页面 {entry.Id} 的 View 脚本与清单工厂绑定不一致。", display));
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
                if (display.IsPage)
                {
                    bool hasRegistration = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
                        .Any(method => method.Name == "RegisterPageFactory" && method.ReturnType == typeof(void) &&
                            !method.ContainsGenericParameters && method.GetParameters().Length == 0);
                    if (!hasRegistration)
                        issues.Add(new UIValidationIssue("页面缺少工厂注册方法，请同步生成代码并完成编译。", display));
                }
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
                        UIReferenceCodeGenerator.Source(display, display.Controls, entry))
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
                try { transition.ValidateConfiguration(); }
                catch (InvalidOperationException exception) { issues.Add(new UIValidationIssue(exception.Message, transition)); }
            }
            foreach (UIVirtualList list in display.GetComponentsInChildren<UIVirtualList>(true))
            {
                if (list.GetComponentInParent<UIBindingRoot>(true) != display) continue;
                try { list.ValidateConfiguration(); }
                catch (InvalidOperationException exception) { issues.Add(new UIValidationIssue(exception.Message, list)); }
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

        private static void FindAddressIssues(UIDisplay display, UIPageManifest manifest, UIPageEntry entry,
            List<UIValidationIssue> issues)
        {
            if (entry == null)
            {
                issues.Add(new UIValidationIssue("此 Prefab 尚未登记到页面清单，可作为草稿独立编辑。", display, false));
                return;
            }

            string path = AssetDatabase.GUIDToAssetPath(entry.PrefabGuid);
            var collected = new EditorPackageAddresses();
            bool hasStartup = false;
            foreach (FrameworkStartupConfig startup in FindEnabledSceneStartupConfigs())
            {
                if (startup.PageManifest == null) continue;
                try
                {
                    UIPageEntry activeEntry = startup.PageManifest.FindByPrefabGuid(entry.PrefabGuid);
                    if (activeEntry == null) continue;
                    hasStartup = true;
                    string problem = AddressIssue(activeEntry.PrefabAddress, path, BuildPackages(startup), collected);
                    if (problem != null) issues.Add(new UIValidationIssue($"{startup.name} · {problem}", display));
                }
                catch (Exception exception)
                {
                    issues.Add(new UIValidationIssue($"{startup.name} · {exception.GetBaseException().Message}", display));
                }
            }
            if (!hasStartup)
                issues.Add(new UIValidationIssue($"页面已登记到 {manifest.name}，接入构建入口后会校验资源包收集结果。", display, false));
        }

        private static void CheckManifest(FrameworkStartupConfig startup,
            EditorPackageAddresses collected, List<string> errors)
        {
            UIPageManifest manifest = startup.PageManifest;
            string scope = $"{AssetDatabase.GetAssetPath(startup)} · {manifest.name}";
            Dictionary<string, AssetPackageConfig> packages;
            try
            {
                manifest.Validate();
                packages = BuildPackages(startup);
            }
            catch (Exception exception)
            {
                errors.Add($"{scope} · {exception.GetBaseException().Message}");
                return;
            }

            foreach (UIPageEntry entry in manifest.Pages)
            {
                string pageScope = $"{scope} · 页面 {entry.Id}";
                string path = AssetDatabase.GUIDToAssetPath(entry.PrefabGuid);
                GameObject prefab = path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                    ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
                if (prefab == null || !prefab.TryGetComponent(out UIDisplay display) || !display.IsPage)
                {
                    errors.Add($"{pageScope} · Prefab GUID {entry.PrefabGuid} 未指向有效的页面 Prefab。");
                    continue;
                }
                CheckPrefab(prefab, entry, $"{pageScope} · {path}", errors);
                CheckAddress(entry.PrefabAddress, path, pageScope, packages, collected, errors);
            }
        }

        private static void CheckPrefab(GameObject prefab, UIPageEntry entry, string context, List<string> errors)
        {
            foreach (UIBindingRoot binding in prefab.GetComponentsInChildren<UIBindingRoot>(true))
            {
                UIPageEntry page = binding.gameObject == prefab ? entry : null;
                foreach (UIValidationIssue issue in FindBindingIssues(binding, page))
                    if (issue.Blocking)
                        errors.Add($"{context} · {binding.name} · {issue.Message}");
            }
        }

        private static void CheckAddress(string address, string path, string context,
            Dictionary<string, AssetPackageConfig> packages,
            EditorPackageAddresses collected, List<string> errors)
        {
            try
            {
                string problem = AddressIssue(address, path, packages, collected);
                if (problem != null) errors.Add($"{context} · {problem}");
            }
            catch (Exception exception)
            {
                errors.Add($"{context} · 无法验证 {address}：{exception.GetBaseException().Message}");
            }
        }

        private static string AddressIssue(string address, string path, Dictionary<string, AssetPackageConfig> packages,
            EditorPackageAddresses collected)
        {
            if (string.IsNullOrWhiteSpace(address) || string.IsNullOrEmpty(path)) return null;
            var (alias, location) = SplitAddress(address);
            if (string.IsNullOrEmpty(alias) || string.IsNullOrEmpty(location) || location.Contains(":"))
                return $"资源地址 {address} 的格式无效。";
            if (!packages.TryGetValue(alias, out var package)) return $"Startup Config 缺少资源包 {alias}。";
            return IsCollected(package, location, path, collected) ? null : $"{address} 未收集为此 Prefab。";
        }

        private static FrameworkStartupConfig[] FindEnabledSceneStartupConfigs()
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path).ToArray();
            return FrameworkPlayerBuildValidation.FindStartupConfigs(scenes);
        }

        private static Dictionary<string, AssetPackageConfig> BuildPackages(FrameworkStartupConfig startup)
        {
            var packages = new Dictionary<string, AssetPackageConfig>(StringComparer.Ordinal);
            foreach (AssetPackageConfig package in startup.AssetPackages)
            {
                if (package == null || string.IsNullOrWhiteSpace(package.Alias)) continue;
                string alias = package.Alias.Trim();
                if (!packages.TryAdd(alias, package))
                    throw new InvalidOperationException($"启动配置 {startup.name} 的资源包别名 {alias} 重复。");
            }
            return packages;
        }

        private static (string alias, string address) SplitAddress(string address)
        {
            int separator = address.IndexOf(':');
            return separator < 0 ? (AssetModule.MainPackage, address.Trim()) :
                (address.Substring(0, separator).Trim(), address.Substring(separator + 1).Trim());
        }

        private static bool IsCollected(AssetPackageConfig package, string address, string prefabPath,
            EditorPackageAddresses collected)
        {
            if (!collected.Collect(package).TryGetValue(address, out string path) || path != prefabPath) return false;
            if (package is not ReferencedAssetPackageConfig references) return true;

            // 同一工程路径可能含多个子资产，直接引用包必须把页面指向 Prefab 根资产。
            return references.Entries.Any(entry => entry.Address.Trim() == address &&
                entry.Asset is GameObject prefab && AssetDatabase.IsMainAsset(prefab));
        }

        #endregion
    }
}
