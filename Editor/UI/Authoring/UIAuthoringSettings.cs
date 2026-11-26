using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using EasyFramework.UI;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.Editor.UI
{
    /// <summary>保存 UI 创建、绑定和生成脚本的项目预设。</summary>
    [FilePath("ProjectSettings/EasyFrameworkUI.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class UIAuthoringSettings : ScriptableSingleton<UIAuthoringSettings>
    {
        internal const string SettingsPath = "Project/EasyFramework/UI";

        [Serializable]
        internal sealed class BindingKeyword
        {
            public string Keyword;
            public string ComponentType;

            internal Type ResolveType()
            {
                if (string.IsNullOrEmpty(ComponentType)) return null;
                Type type = Type.GetType(ComponentType, throwOnError: false);
                return IsBindingComponent(type) ? type : null;
            }

            internal void SetType(Type type) => ComponentType = $"{type.FullName}, {type.Assembly.GetName().Name}";
        }

        public string PrefabFolder = "Assets/GameRes/UI";
        public string ScriptFolder = "Assets/GameScripts/UI";
        public string Namespace = "Game.UI";
        public string Package = AssetModule.MainPackage;
        public string AddressPrefix = "UI";
        [SerializeField] private UIPageManifest _pageManifest;
        [SerializeField] private string _generatedFolder;
        private string _generatedFolderDraft;
        private string _generatedFolderError;
        internal string GeneratedFolder => string.IsNullOrEmpty(_generatedFolder)
            ? UIGeneratedScriptFiles.DefaultFolder : _generatedFolder;
        internal UIPageManifest PageManifest => _pageManifest;

        [SerializeField] private List<BindingKeyword> _bindingKeywords = new()
        {
            CreateKeyword("brn", typeof(Button)),
            CreateKeyword("txt", typeof(Text)),
            CreateKeyword("img", typeof(Image)),
            CreateKeyword("rect", typeof(RectTransform)),
            CreateKeyword("state", typeof(UIStateController)),
            CreateKeyword("timeline", typeof(UITimeline)),
            CreateKeyword("list", typeof(UIVirtualList)),
            CreateKeyword("node", typeof(UINode)),
        };

        internal ILookup<string, BindingKeyword> BindingKeywords =>
            _bindingKeywords.Where(rule => rule != null && IsKeyword(rule.Keyword))
                .ToLookup(rule => rule.Keyword, StringComparer.OrdinalIgnoreCase);

        internal static bool IsKeyword(string value) =>
            !string.IsNullOrEmpty(value) && Regex.IsMatch(value, "\\A[A-Za-z][A-Za-z0-9]*\\z");

        private static bool IsBindingComponent(Type type) =>
            type != null && typeof(Component).IsAssignableFrom(type) && type.IsVisible &&
            !type.IsAbstract && !type.IsGenericType &&
            !type.Assembly.GetName().Name.StartsWith("UnityEditor", StringComparison.Ordinal);

        private static BindingKeyword CreateKeyword(string keyword, Type type)
        {
            var rule = new BindingKeyword { Keyword = keyword };
            rule.SetType(type);
            return rule;
        }

        [SettingsProvider]
        private static SettingsProvider CreateProvider()
        {
            var provider = new SettingsProvider(SettingsPath, SettingsScope.Project) { label = "EasyFramework UI" };
            provider.guiHandler = _ => instance.DrawSettings(provider);
            return provider;
        }

        private void DrawSettings(SettingsProvider provider)
        {
            using var options = new SerializedObject(this);
            EditorGUILayout.PropertyField(options.FindProperty(nameof(PrefabFolder)), new GUIContent("资源收集目录"));
            EditorGUILayout.PropertyField(options.FindProperty(nameof(ScriptFolder)), new GUIContent("业务脚本默认目录"));
            EditorGUILayout.PropertyField(options.FindProperty(nameof(Namespace)), new GUIContent("命名空间"));
            EditorGUILayout.PropertyField(options.FindProperty(nameof(Package)), new GUIContent("资源包别名"));
            EditorGUILayout.PropertyField(options.FindProperty(nameof(AddressPrefix)), new GUIContent("包内地址前缀"));
            EditorGUILayout.PropertyField(options.FindProperty("_pageManifest"), new GUIContent("页面生成默认清单"));
            if (options.ApplyModifiedProperties()) Save(true);

            string editableFolder = _generatedFolderDraft ?? GeneratedFolder;
            string requestedFolder = EditorGUILayout.DelayedTextField("生成脚本目录", editableFolder);
            // 将目录移动和提示状态的更新放在本轮 IMGUI 绘制结束之后。
            if (requestedFolder != editableFolder)
                EditorApplication.delayCall += () => ApplyGeneratedFolder(requestedFolder, provider);
            if (!string.IsNullOrEmpty(_generatedFolderError))
                EditorGUILayout.HelpBox(_generatedFolderError, MessageType.Error);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("控件关键字映射", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("节点名使用「关键字_属性名」，例如 brn_CloseButton 或 node_PlayerCard。可搜索 Unity 组件或已编译的自定义组件。", MessageType.Info);

            var groups = BindingKeywords;
            bool changed = false;
            for (int i = 0; i < _bindingKeywords.Count; i++)
            {
                BindingKeyword rule = _bindingKeywords[i];
                Type type = rule.ResolveType();
                using (new EditorGUILayout.HorizontalScope())
                {
                    string keyword = EditorGUILayout.DelayedTextField(rule.Keyword ?? "", GUILayout.Width(100f)).Trim();
                    if (keyword != rule.Keyword)
                    {
                        rule.Keyword = keyword;
                        changed = true;
                    }
                    Rect rect = EditorGUILayout.GetControlRect();
                    var label = new GUIContent(type?.Name ?? "选择组件", rule.ComponentType);
                    if (EditorGUI.DropdownButton(rect, label, FocusType.Keyboard))
                    {
                        new ComponentDropdown(selected =>
                        {
                            if (!_bindingKeywords.Contains(rule)) return;
                            rule.SetType(selected);
                            Save(true);
                        }).Show(rect);
                    }
                    if (GUILayout.Button("移除", GUILayout.Width(54f)))
                    {
                        _bindingKeywords.RemoveAt(i);
                        changed = true;
                        break;
                    }
                }

                if (string.IsNullOrEmpty(rule.Keyword))
                    EditorGUILayout.HelpBox("填写关键字并选择组件类型。", MessageType.Info);
                else if (!IsKeyword(rule.Keyword))
                    EditorGUILayout.HelpBox("关键字须以英文字母开头，只能包含英文字母和数字。", MessageType.Error);
                else if (groups[rule.Keyword].Skip(1).Any())
                    EditorGUILayout.HelpBox($"关键字 {rule.Keyword} 重复。", MessageType.Error);
                else if (type == null)
                    EditorGUILayout.HelpBox(string.IsNullOrEmpty(rule.ComponentType) ? "请选择组件。" :
                        $"组件 {rule.ComponentType} 不可用，请重新选择。", MessageType.Warning);
            }

            if (GUILayout.Button("添加映射"))
            {
                _bindingKeywords.Add(new BindingKeyword { Keyword = string.Empty });
                changed = true;
            }

            if (changed) Save(true);
        }

        private void ApplyGeneratedFolder(string folder, SettingsProvider provider)
        {
            try
            {
                if (folder != GeneratedFolder) SetGeneratedFolder(folder);
                _generatedFolderDraft = null;
                _generatedFolderError = null;
            }
            catch (Exception exception) when (exception.GetBaseException() is not ExitGUIException)
            {
                _generatedFolderDraft = folder;
                Exception cause = exception.GetBaseException();
                string detail = string.IsNullOrWhiteSpace(cause.Message)
                    ? $"未返回详细错误（{cause.GetType().Name}）。"
                    : cause.Message.Trim();
                _generatedFolderError = $"无法设置生成脚本目录。{detail}\n当前使用「{GeneratedFolder}」。";
            }
            finally { provider.Repaint(); }
        }

        internal void SetGeneratedFolder(string folder)
        {
            _generatedFolder = UIGeneratedScriptFiles.ChangeFolder(GeneratedFolder, folder);
            Save(true);
        }

        private sealed class ComponentDropdown : AdvancedDropdown
        {
            private sealed class ComponentItem : AdvancedDropdownItem
            {
                internal readonly Type Type;

                internal ComponentItem(Type type) : base(type.FullName) => Type = type;
            }

            private readonly Action<Type> _selected;

            internal ComponentDropdown(Action<Type> selected) : base(new AdvancedDropdownState())
            {
                _selected = selected;
                minimumSize = new Vector2(420f, 350f);
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                var root = new AdvancedDropdownItem("组件类型");
                foreach (Type type in TypeCache.GetTypesDerivedFrom<Component>()
                             .Where(IsBindingComponent).OrderBy(type => type.FullName, StringComparer.Ordinal))
                    root.AddChild(new ComponentItem(type));
                return root;
            }

            protected override void ItemSelected(AdvancedDropdownItem item) => _selected(((ComponentItem)item).Type);
        }
    }
}
