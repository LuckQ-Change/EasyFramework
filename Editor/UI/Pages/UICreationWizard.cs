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
    /// <summary>生成 UI Prefab 和绑定脚本，并登记可导航的页面。</summary>
    internal sealed class UICreationWizard : EditorWindow
    {
        private string _pageName = "";
        private string _scriptFolder;
        private string _error;
        private UIPageKind _kind;
        private UIPageManifest _manifest;
        private FrameworkStartupConfig _startup;
        private AssetPackageConfig _package;
        private string _pageId = string.Empty;

        #region 生命周期

        [MenuItem("Tools/EasyFramework/UI/Create Display", false, 1)]
        private static void Open()
        {
            if (Selection.activeObject is FrameworkStartupConfig startup) OpenPage(startup);
            else GetWindow<UICreationWizard>(true, "创建 UI");
        }

        private void OnEnable()
        {
            _scriptFolder = UIAuthoringSettings.instance.ScriptFolder;
            _manifest = Selection.activeObject as UIPageManifest ?? UIAuthoringSettings.instance.PageManifest;
        }

        private void OnGUI()
        {
            _pageName = EditorGUILayout.TextField("名称", _pageName);
            _kind = (UIPageKind)EditorGUILayout.EnumPopup("类型", _kind);
            _scriptFolder = EditorGUILayout.TextField("业务脚本目录", _scriptFolder);
            bool isPage = UIBindingRoot.IsPageKind(_kind);
            if (isPage) DrawPageSettings();
            if (!string.IsNullOrEmpty(_error)) EditorGUILayout.HelpBox(_error, MessageType.Error);
            if (GUILayout.Button("项目预设")) SettingsService.OpenProjectSettings(UIAuthoringSettings.SettingsPath);
            if (!GUILayout.Button("创建")) return;
            try
            {
                AssetPackageConfig package = isPage ? ResolvePackage() : null;
                string path = Create(_pageName.Trim(), _kind, _scriptFolder, _manifest, _pageId, package);
                EditorApplication.delayCall += () => PrefabStageUtility.OpenPrefab(path);
                Close();
            }
            catch (Exception exception) { _error = exception.Message; }
        }

        #endregion

        #region 公共方法

        internal static void OpenPage(FrameworkStartupConfig startup)
        {
            if (startup == null) throw new ArgumentNullException(nameof(startup));
            var window = GetWindow<UICreationWizard>(true, "创建 UI");
            window._kind = UIPageKind.Screen;
            window._startup = startup;
            window._manifest = startup.PageManifest;
            window._pageId = string.Empty;
            window._error = null;
            AssetPackageConfig[] packages = startup.AssetPackages.Where(package => package != null).ToArray();
            string preferredAlias = UIAuthoringSettings.instance.Package?.Trim();
            window._package = packages.Length == 1
                ? packages[0] : packages.FirstOrDefault(package => package.Alias?.Trim() == preferredAlias);
            window.Focus();
        }

        internal static void OpenItemTemplate() => GetWindow<UICreationWizard>(true, "创建 UI")._kind = UIPageKind.Item;

        internal static string Create(string name, UIPageKind kind, string businessFolder = null,
            UIPageManifest manifest = null, string pageId = null, AssetPackageConfig package = null)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("退出 Play Mode 后再创建 UI。");
            UIAuthoringSettings settings = UIAuthoringSettings.instance;
            string prefabFolder = UIGeneratedScriptFiles.AssetFolder(settings.PrefabFolder);
            string scriptFolder = UIGeneratedScriptFiles.AssetFolder(businessFolder ?? settings.ScriptFolder);
            if (!UIReferenceCodeGenerator.IsName(name) ||
                !settings.Namespace.Split('.').All(UIReferenceCodeGenerator.IsIdentifier))
                throw new InvalidOperationException("界面名称应以大写字母开头，命名空间必须是有效的 C# 标识符。");

            bool isPage = UIBindingRoot.IsPageKind(kind);
            string viewName = settings.Namespace + "." + name;
            string address = null;
            var directPackage = package as ReferencedAssetPackageConfig;
            if (isPage)
            {
                if (manifest == null) manifest = settings.PageManifest;
                if (package != null && string.IsNullOrEmpty(AssetDatabase.GetAssetPath(package)))
                    throw new InvalidOperationException("请选择已保存的资源包配置。");
                pageId = string.IsNullOrWhiteSpace(pageId) ? viewName : pageId.Trim();
                address = LocationFor(name, package);
                UIPageManifestAuthoring.ValidateNewPage(manifest, pageId, address, directPackage);
            }
            string prefab = $"{prefabFolder}/{name}.prefab";
            string script = $"{scriptFolder}/{name}.cs";
            RequireUnusedPath(prefab);
            RequireUnusedPath(script);

            GameObject root = null;
            var created = new List<string>(3);
            bool isRegistering = false;
            try
            {
                UIGeneratedScriptFiles.EnsureFolder(prefabFolder);
                UIGeneratedScriptFiles.EnsureFolder(scriptFolder);
                WriteNewScript(script, UIScriptGenerator.Create(name, settings.Namespace, kind), created);
                AssetDatabase.ImportAsset(script, ImportAssetOptions.ForceSynchronousImport);
                string guid = AssetDatabase.AssetPathToGUID(script);
                if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException("界面脚本未成功导入。");
                string generated = UIGeneratedScriptFiles.PathFor(script, guid, prepare: true);
                WriteNewScript(generated, UIReferenceCodeGenerator.Source(viewName, kind,
                    isPage ? settings.Namespace + "." + name + "Args" : null,
                    kind == UIPageKind.Popup ? settings.Namespace + "." + name + "Result" : null,
                    Array.Empty<UIControlReference>(), isPage ? guid : null), created);
                AssetDatabase.ImportAsset(generated);

                root = new GameObject(name, typeof(RectTransform), kind == UIPageKind.Node ? typeof(UINode) : typeof(UIDisplay));
                var rect = (RectTransform)root.transform;
                if (isPage)
                {
                    rect.anchorMin = Vector2.zero;
                    rect.anchorMax = Vector2.one;
                    rect.offsetMin = rect.offsetMax = Vector2.zero;
                    root.AddComponent<UITransition>();
                }
                if (root.TryGetComponent(out UINode node)) node.SetViewScript(viewName, guid);
                else root.GetComponent<UIDisplay>().Configure(kind, viewName, guid);
                RequireUnusedPath(prefab);
                created.Add(prefab);
                GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(root, prefab);
                if (prefabAsset == null) throw new InvalidOperationException($"Prefab 保存失败：{prefab}。");
                if (isPage)
                {
                    isRegistering = true;
                    UIPageManifestAuthoring.Register(manifest,
                        new UIPageEntry(pageId, AssetDatabase.AssetPathToGUID(prefab), address, guid, kind),
                        directPackage, prefabAsset);
                }
                return prefab;
            }
            catch (AggregateException exception) when (isRegistering)
            {
                // 登记资产恢复失败时保留生成结果，避免残留登记引用不存在的 Prefab。
                throw new InvalidOperationException($"页面登记未能完整回滚，已保留 {prefab} 和生成脚本。{exception.Message}", exception);
            }
            catch
            {
                RemoveCreatedAssets(created);
                throw;
            }
            finally { if (root != null) Object.DestroyImmediate(root); }
        }

        #endregion

        #region 私有逻辑

        private void DrawPageSettings()
        {
            UIPageManifest manifest = (UIPageManifest)EditorGUILayout.ObjectField("页面清单", _manifest,
                typeof(UIPageManifest), false);
            if (_manifest != manifest && _startup != null && _startup.PageManifest != manifest)
            {
                _startup = null;
                _package = null;
            }
            _manifest = manifest;
            _pageId = EditorGUILayout.DelayedTextField(new GUIContent("页面 ID", "留空时使用初始 View 全名。"), _pageId);
            DrawPackage();
            if (_manifest == null)
                EditorGUILayout.HelpBox("请选择用于登记新页面的清单。", MessageType.Info);
            else if (_startup != null && _package == null)
                EditorGUILayout.HelpBox("请选择此启动配置中的资源包。", MessageType.Info);
            else if (_package is IYooAssetPackageConfig)
                EditorGUILayout.HelpBox("YooAsset 收集器需要将此 Prefab 收集为页面登记中的包内地址。", MessageType.Info);
        }

        private void DrawPackage()
        {
            if (_startup == null)
            {
                _package = (AssetPackageConfig)EditorGUILayout.ObjectField(new GUIContent("资源包",
                    "选择直接引用包时自动登记 Prefab。留空时使用项目预设的别名。"),
                    _package, typeof(AssetPackageConfig), false);
                return;
            }

            AssetPackageConfig[] packages = _startup.AssetPackages.Where(package => package != null).ToArray();
            var labels = new string[packages.Length + 1];
            labels[0] = "选择启动配置中的资源包";
            for (int i = 0; i < packages.Length; i++)
                labels[i + 1] = $"{packages[i].Alias} · {packages[i].name}";
            int index = EditorGUILayout.Popup("资源包", Array.IndexOf(packages, _package) + 1, labels);
            _package = index > 0 ? packages[index - 1] : null;
        }

        private AssetPackageConfig ResolvePackage()
        {
            if (_startup == null) return _package;
            if (_manifest != _startup.PageManifest)
                throw new InvalidOperationException("启动配置的页面清单已变化，请重新打开页面向导。");
            if (_package == null || !_startup.AssetPackages.Contains(_package))
                throw new InvalidOperationException("请选择此启动配置中已登记的资源包。");
            string alias = AssetModule.NormalizeAlias(_package.Alias);
            if (_startup.AssetPackages.Count(package => package != null && package.Alias?.Trim() == alias) != 1)
                throw new InvalidOperationException($"启动配置中的资源包别名「{alias}」重复。");
            return _package;
        }

        private static string LocationFor(string name, AssetPackageConfig package)
        {
            var settings = UIAuthoringSettings.instance;
            string alias = AssetModule.NormalizeAlias(package != null ? package.Alias : settings.Package);
            string prefix = settings.AddressPrefix?.Trim().Trim('/') ?? string.Empty;
            if (prefix.Contains(":")) throw new InvalidOperationException("UI 包内路径前缀不能包含冒号。");
            return alias + ":" + (prefix.Length == 0 ? name : prefix + "/" + name);
        }

        private static void RequireUnusedPath(string path)
        {
            if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets)) ||
                File.Exists(path) || Directory.Exists(path) || File.Exists(path + ".meta") || Directory.Exists(path + ".meta"))
                throw new InvalidOperationException($"资产路径已被占用：{path}。");
        }

        private static void WriteNewScript(string path, string source, List<string> created)
        {
            RequireUnusedPath(path);
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            created.Add(path);
            using var writer = new StreamWriter(stream);
            writer.Write(source);
        }

        private static void RemoveCreatedAssets(List<string> created)
        {
            for (int i = created.Count - 1; i >= 0; i--)
            {
                string path = created[i];
                try
                {
                    if (!File.Exists(path) && !File.Exists(path + ".meta")) continue;
                    if (AssetDatabase.DeleteAsset(path)) continue;
                    File.Delete(path);
                    File.Delete(path + ".meta");
                }
                catch (Exception exception) { Debug.LogError($"无法清理生成失败的资产 {path}。{exception.Message}"); }
            }
        }

        #endregion
    }
}
