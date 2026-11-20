using System;
using System.IO;
using System.Linq;
using EasyFramework.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyFramework.Editor.UI
{
    internal sealed class UICreationWizard : EditorWindow
    {
        private string _pageName = "";
        private string _scriptFolder;
        private string _error;
        private UIPageKind _kind;

        [MenuItem("Tools/EasyFramework/UI/Create Display", false, 1)]
        private static void Open() => GetWindow<UICreationWizard>(true, "创建 UI");

        internal static void OpenItemTemplate() => GetWindow<UICreationWizard>(true, "创建 UI")._kind = UIPageKind.Item;

        private void OnEnable() => _scriptFolder = UIAuthoringSettings.instance.ScriptFolder;

        private void OnGUI()
        {
            _pageName = EditorGUILayout.TextField("名称", _pageName);
            _kind = (UIPageKind)EditorGUILayout.EnumPopup("类型", _kind);
            _scriptFolder = EditorGUILayout.TextField("业务脚本目录", _scriptFolder);
            if (!string.IsNullOrEmpty(_error)) EditorGUILayout.HelpBox(_error, MessageType.Error);
            if (GUILayout.Button("项目预设")) SettingsService.OpenProjectSettings(UIAuthoringSettings.SettingsPath);
            if (!GUILayout.Button("创建")) return;
            try
            {
                string path = Create(_pageName.Trim(), _kind, _scriptFolder);
                EditorApplication.delayCall += () => PrefabStageUtility.OpenPrefab(path);
                Close();
            }
            catch (Exception exception) { _error = exception.Message; }
        }

        internal static string Create(string name, UIPageKind kind, string businessFolder = null)
        {
            UIAuthoringSettings settings = UIAuthoringSettings.instance;
            string prefabFolder = UIGeneratedScriptFiles.AssetFolder(settings.PrefabFolder);
            string scriptFolder = UIGeneratedScriptFiles.AssetFolder(businessFolder ?? settings.ScriptFolder);
            if (!UIReferenceCodeGenerator.IsName(name) ||
                !settings.Namespace.Split('.').All(UIReferenceCodeGenerator.IsIdentifier))
                throw new InvalidOperationException("界面名称应以大写字母开头，命名空间必须是有效的 C# 标识符。");

            bool isPage = UIBindingRoot.IsPageKind(kind);
            string address = isPage ? LocationFor(name) : string.Empty;
            string prefab = $"{prefabFolder}/{name}.prefab";
            string script = $"{scriptFolder}/{name}.cs";
            if (File.Exists(prefab) || File.Exists(script))
                throw new InvalidOperationException("同名 Prefab 或脚本已存在。");

            GameObject root = null;
            string generated = null;
            bool wroteGenerated = false;
            try
            {
                UIGeneratedScriptFiles.EnsureFolder(prefabFolder);
                UIGeneratedScriptFiles.EnsureFolder(scriptFolder);
                File.WriteAllText(script, UIScriptGenerator.Create(name, settings.Namespace, kind));
                AssetDatabase.ImportAsset(script, ImportAssetOptions.ForceSynchronousImport);
                string guid = AssetDatabase.AssetPathToGUID(script);
                if (string.IsNullOrEmpty(guid)) throw new InvalidOperationException("界面脚本未成功导入。");
                generated = UIGeneratedScriptFiles.PathFor(script, guid, prepare: true);
                if (File.Exists(generated)) throw new InvalidOperationException($"生成脚本已存在：{generated}。");
                wroteGenerated = true;
                File.WriteAllText(generated, UIReferenceCodeGenerator.Source(settings.Namespace + "." + name, kind,
                    address, isPage ? settings.Namespace + "." + name + "Args" : null,
                    kind == UIPageKind.Popup ? settings.Namespace + "." + name + "Result" : null, Array.Empty<UIControlReference>()));
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
                string viewName = settings.Namespace + "." + name;
                if (root.TryGetComponent(out UINode node)) node.SetViewScript(viewName, guid);
                else root.GetComponent<UIDisplay>().Configure(kind, viewName, guid, address);
                if (PrefabUtility.SaveAsPrefabAsset(root, prefab) == null)
                    throw new InvalidOperationException("Prefab 保存失败。");
                return prefab;
            }
            catch
            {
                foreach (string path in new[] { prefab, script, wroteGenerated ? generated : null })
                    if (File.Exists(path) && !AssetDatabase.DeleteAsset(path)) File.Delete(path);
                throw;
            }
            finally { if (root != null) Object.DestroyImmediate(root); }
        }

        internal static string LocationFor(string name)
        {
            var settings = UIAuthoringSettings.instance;
            string package = settings.Package?.Trim();
            string prefix = settings.AddressPrefix?.Trim().Trim('/') ?? string.Empty;
            if (string.IsNullOrEmpty(package) || package.Contains(":") || prefix.Contains(":"))
                throw new InvalidOperationException("请先配置可用的 UI 资源包别名和包内路径。");
            return package + ":" + (prefix.Length == 0 ? name : prefix + "/" + name);
        }

    }
}
