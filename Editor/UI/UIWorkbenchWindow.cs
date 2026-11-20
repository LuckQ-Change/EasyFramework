using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using EasyFramework.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace EasyFramework.Editor.UI
{
    /// <summary>把选中页面的配置、引用、状态和校验汇集到一个可停靠入口。</summary>
    internal sealed class UIWorkbenchWindow : EditorWindow
    {
        private enum Page { Display, References, States, Adaptation, Animation, Validation }
        private static readonly string[] PageNames = { "页面", "引用", "状态", "适配", "动画", "校验" };

        private Page _page;
        private Vector2 _scroll;
        private UIControlRename _rename;
        private UIBindingRoot _renameDisplay;
        private UIStateController _controller;
        private List<UIValidationIssue> _issues;
        private UnityEditor.Editor _inspector;
        private Object _inspectedObject;
        private string _referenceMessage;

        [MenuItem("Tools/EasyFramework/UI/UI Editor", false, 0)]
        private static void Open()
        {
            var window = GetWindow<UIWorkbenchWindow>("UI Editor");
            window.minSize = new Vector2(560f, 420f);
            window.Show();
        }

        private void OnEnable() => Undo.undoRedoPerformed += Repaint;

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= Repaint;
            ClearInspector();
        }

        private void OnSelectionChange()
        {
            _controller = null;
            _issues = null;
            ClearInspector();
            Repaint();
        }

        private void OnHierarchyChange() { _issues = null; Repaint(); }
        private void OnProjectChange() { _issues = null; Repaint(); }

        private void OnGUI()
        {
            GameObject selected = Selection.activeGameObject;
            UIBindingRoot display = FindDisplay(selected);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("UI 工作台", new GUIStyle(EditorStyles.boldLabel) { fontSize = 17 });
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("创建页面 / Node / Item", GUILayout.Height(26f)))
                EditorApplication.ExecuteMenuItem("Tools/EasyFramework/UI/Create Display");
            using (new EditorGUI.DisabledScope(display == null))
                if (GUILayout.Button("打开 Prefab", GUILayout.Height(26f))) OpenPrefab(display);
            if (GUILayout.Button("组件转换", GUILayout.Height(26f)))
                EditorApplication.ExecuteMenuItem("Tools/EasyFramework/UI/Component Converter");
            EditorGUILayout.EndHorizontal();

            int nextPage = GUILayout.Toolbar((int)_page, PageNames);
            if (nextPage != (int)_page)
            {
                _page = (Page)nextPage;
                _issues = null;
                ClearInspector();
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.ObjectField("选中节点", selected, typeof(GameObject), true);
                EditorGUILayout.ObjectField("所属界面 / Node", display, typeof(UIBindingRoot), true);
            }
            if (display == null)
            {
                EditorGUILayout.HelpBox("在 Project、Hierarchy 或 Prefab Mode 中选择页面、Node 或其子节点。",
                    MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            switch (_page)
            {
                case Page.Display: DrawInspector(display); break;
                case Page.References: DrawReferences(display, selected); break;
                case Page.States: DrawStates(display, selected); break;
                case Page.Adaptation: DrawAdaptation(display, selected); break;
                case Page.Animation: DrawAnimation(display, selected); break;
                case Page.Validation: DrawValidation(display); break;
            }
            EditorGUILayout.EndScrollView();
        }

        private static UIBindingRoot FindDisplay(GameObject selected)
        {
            if (selected != null) return selected.GetComponentInParent<UIBindingRoot>(true);
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            return stage == null ? null : stage.prefabContentsRoot.GetComponent<UIBindingRoot>();
        }

        private void DrawReferences(UIBindingRoot display, GameObject selected)
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            bool editable = stage != null && stage.prefabContentsRoot == display.gameObject;
            if (_renameDisplay != display)
            {
                _rename = null;
                _renameDisplay = display;
                _referenceMessage = null;
            }

            EditorGUILayout.HelpBox("在 Prefab 中按「关键字_属性名」命名节点，例如 brn_CloseButton。保存后，View 可直接用 CloseButton。", MessageType.Info);
            try
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool canExpose = editable && selected != null && selected != display.gameObject && FindDisplay(selected) == display;
                    using (new EditorGUI.DisabledScope(!canExpose))
                        if (GUILayout.Button("为选中节点选择关键字"))
                            ShowKeywordMenu(selected, stage);
                    if (GUILayout.Button("编辑关键字映射"))
                        SettingsService.OpenProjectSettings(UIAuthoringSettings.SettingsPath);
                }

                var found = UIReferenceCodeGenerator.Scan(display).ToList();
                foreach (UIControlReference slot in display.Controls)
                {
                    UIControlReference current = found.FirstOrDefault(control => UIReferenceCodeGenerator.SameNode(control, slot));
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.ObjectField(slot?.Name ?? "失效引用", slot?.Target, typeof(Component), true);
                        using (new EditorGUI.DisabledScope(!editable))
                        {
                            if (current != null && current.Target != slot.Target)
                                EditorGUILayout.LabelField("保存后更新组件", GUILayout.Width(105f));
                            else if (current != null && current.Name != slot.Name &&
                                GUILayout.Button($"预览 → {current.Name}", GUILayout.MinWidth(140f)))
                                _rename = UIControlRename.Preview(display, slot, current.Name);
                            if (current == null && GUILayout.Button("移除") &&
                                EditorUtility.DisplayDialog("移除控件引用", $"移除 {slot?.Name}？请一并删除业务中的使用。", "移除", "取消"))
                            {
                                var remaining = display.Controls.Where(control => !ReferenceEquals(control, slot)).ToArray();
                                var code = UIReferenceCodeGenerator.Source(display, remaining);
                                UIReferenceCodeGenerator.Write(UIReferenceCodeGenerator.GeneratedPath(display), code);
                                display.SetControls(remaining);
                                EditorUtility.SetDirty(display);
                                EditorSceneManager.MarkSceneDirty(stage.scene);
                                _issues = null;
                            }
                        }
                    }
                }

                if (_rename != null)
                {
                    EditorGUILayout.LabelField(_rename.Description, EditorStyles.boldLabel);
                    var usages = _rename.Usages.ToArray();
                    if (usages.Length == 0) EditorGUILayout.LabelField("没有业务使用点。");
                    foreach (var usage in usages)
                        if (GUILayout.Button($"{usage.Path}:{usage.Line}", EditorStyles.miniButton))
                            AssetDatabase.OpenAsset(AssetDatabase.LoadAssetAtPath<MonoScript>(usage.Path), usage.Line);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("取消预览")) _rename = null;
                        else if (GUILayout.Button("确认改名并保存"))
                        {
                            _rename.Apply();
                            _rename = null;
                            _issues = null;
                            _referenceMessage = "Prefab 和引用已同步。";
                        }
                    }
                }

                foreach (var control in found.Where(control => display.Controls.All(slot => !UIReferenceCodeGenerator.SameNode(slot, control))))
                    EditorGUILayout.ObjectField($"待保存 · {control.Name}", control.Target, typeof(Component), true);
            }
            catch (Exception exception) { _referenceMessage = exception.GetBaseException().Message; }
            if (!string.IsNullOrEmpty(_referenceMessage))
                EditorGUILayout.HelpBox(_referenceMessage, MessageType.Info);
        }

        private static void ShowKeywordMenu(GameObject selected, PrefabStage stage)
        {
            var groups = UIAuthoringSettings.instance.BindingKeywords;
            string name = selected.name;
            int separator = name.IndexOf('_');
            if (separator >= 0 && groups.Contains(name.Substring(0, separator)))
                name = name.Substring(separator + 1);
            name = Regex.Replace(name, "[^A-Za-z0-9_]", "");
            if (!UIReferenceCodeGenerator.IsIdentifier(name) || !char.IsLetter(name[0]))
                throw new InvalidOperationException("请给节点起一个以英文字母开头的控件属性名。");

            var menu = new GenericMenu();
            foreach (var group in groups)
            {
                if (group.Skip(1).Any()) continue;
                var rule = group.First();
                Type type = rule.ResolveType();
                if (type == null || !selected.GetComponents(type).Any(component => component != null)) continue;
                string nodeName = rule.Keyword + "_" + name;
                menu.AddItem(new GUIContent($"{rule.Keyword} · {type.Name}"), selected.name == nodeName, () =>
                {
                    if (selected == null || PrefabStageUtility.GetCurrentPrefabStage() != stage) return;
                    Undo.RecordObject(selected, "设置 UI 控件关键字");
                    selected.name = nodeName;
                    EditorSceneManager.MarkSceneDirty(stage.scene);
                });
            }
            if (menu.GetItemCount() == 0)
                menu.AddDisabledItem(new GUIContent("没有匹配此节点组件的关键字"));
            menu.ShowAsContext();
        }

        private void DrawStates(UIBindingRoot display, GameObject selected)
        {
            UIStateController[] controllers = display.GetComponentsInChildren<UIStateController>(true);
            UIStateController selectedController = selected == null
                ? null : selected.GetComponentInParent<UIStateController>(true);
            if (_controller == null || _controller.GetComponentInParent<UIBindingRoot>(true) != display)
                _controller = selectedController != null ? selectedController :
                    controllers.Length > 0 ? controllers[0] : null;

            EditorGUILayout.LabelField($"状态控制器：{controllers.Length}", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            foreach (UIStateController controller in controllers)
                if (GUILayout.Toggle(controller == _controller, controller.name, EditorStyles.miniButton))
                    _controller = controller;
            EditorGUILayout.EndHorizontal();

            if (_controller == null && !AssetDatabase.Contains(display.gameObject) &&
                GUILayout.Button("在页面根节点添加 UIStateController"))
                _controller = Undo.AddComponent<UIStateController>(display.gameObject);

            if (_controller == null)
            {
                EditorGUILayout.HelpBox("打开 Prefab 后，可在页面根节点添加 UIStateController，再切换和记录状态。",
                    MessageType.Info);
                return;
            }
            bool canAddElement = selected != null && !AssetDatabase.Contains(selected) &&
                selected.GetComponentInParent<UIStateController>(true) == _controller &&
                selected.GetComponent<UIElement>() == null;
            using (new EditorGUI.DisabledScope(!canAddElement))
                if (GUILayout.Button("在选中节点添加 UIElement"))
                    Undo.AddComponent<UIElement>(selected);

            DrawInspector(_controller);
            if (GUILayout.Button("打开状态矩阵")) UIStateWorkbench.Open(_controller);
        }

        private void DrawValidation(UIBindingRoot display)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("页面校验", EditorStyles.boldLabel);
            if (GUILayout.Button("重新校验", GUILayout.Width(100f))) _issues = UIValidator.FindIssues(display, validateAddress: true);
            EditorGUILayout.EndHorizontal();
            if (_issues == null) _issues = UIValidator.FindIssues(display, validateAddress: true);
            if (_issues.Count == 0)
            {
                EditorGUILayout.HelpBox("当前页面校验通过。", MessageType.Info);
                return;
            }
            foreach (UIValidationIssue issue in _issues)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.HelpBox(issue.Message, issue.Blocking ? MessageType.Error : MessageType.Warning);
                using (new EditorGUI.DisabledScope(issue.Context == null))
                    if (GUILayout.Button("定位", GUILayout.Width(52f), GUILayout.MinHeight(38f)))
                    {
                        Selection.activeObject = issue.Context;
                        EditorGUIUtility.PingObject(issue.Context);
                    }
                EditorGUILayout.EndHorizontal();
            }
        }

        private static void DrawAnimation(UIBindingRoot display, GameObject selected)
        {
            UITimeline timeline = selected == null ? null : selected.GetComponent<UITimeline>();
            if (timeline == null) timeline = display.GetComponentInChildren<UITimeline>(true);
            bool canAdd = selected != null && !AssetDatabase.Contains(selected) &&
                selected.GetComponentInParent<UIBindingRoot>(true) == display &&
                selected.GetComponent<UITimeline>() == null;
            using (new EditorGUI.DisabledScope(!canAdd))
                if (GUILayout.Button("在选中节点添加 UITimeline"))
                    timeline = Undo.AddComponent<UITimeline>(selected);
            if (timeline == null)
                EditorGUILayout.HelpBox("添加 UITimeline 后可创建命名动画、片段和关键帧。", MessageType.Info);
            else
            {
                EditorGUILayout.ObjectField("时间轴", timeline, typeof(UITimeline), true);
                if (GUILayout.Button("打开时间轴工作台")) UITimelineWorkbench.Open(timeline);
            }
        }

        private static void DrawAdaptation(UIBindingRoot display, GameObject selected)
        {
            if (GUILayout.Button("打开分辨率与安全区预览")) UIResolutionPreview.Open(display);
            var list = selected == null ? null : selected.GetComponentInParent<UIVirtualList>(true);
            if (list != null && list.GetComponentInParent<UIBindingRoot>(true) == display &&
                GUILayout.Button("预览选中节点的虚拟列表")) UIVirtualListPreview.Open(list);
            if (GUILayout.Button("创建可复用 Item 模板")) UICreationWizard.OpenItemTemplate();
            RectTransform rect = selected == null ? null : selected.GetComponent<RectTransform>();
            bool editable = rect != null && !AssetDatabase.Contains(selected) &&
                selected.GetComponentInParent<UIBindingRoot>(true) == display;
            EditorGUILayout.LabelField("选中节点 Anchor 预设", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(!editable))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("全屏拉伸")) UIRectPresets.Stretch(rect);
                if (GUILayout.Button("居中")) UIRectPresets.Center(rect);
                if (GUILayout.Button("左上")) UIRectPresets.TopLeft(rect);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.LabelField("Layout 预设", EditorStyles.boldLabel);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("纵向")) UIRectPresets.AddVerticalLayout(selected);
                if (GUILayout.Button("横向")) UIRectPresets.AddHorizontalLayout(selected);
                if (GUILayout.Button("网格")) UIRectPresets.AddGridLayout(selected);
                EditorGUILayout.EndHorizontal();
                if (selected != null && selected.GetComponent<UISafeArea>() == null &&
                    GUILayout.Button("添加安全区组件")) Undo.AddComponent<UISafeArea>(selected);
            }
            if (!editable)
                EditorGUILayout.HelpBox("在 Prefab Mode 中选择页面内的 RectTransform 后可应用预设。", MessageType.Info);
        }

        private void DrawInspector(Object target)
        {
            if (_inspectedObject != target || _inspector == null)
            {
                ClearInspector();
                _inspectedObject = target;
                _inspector = UnityEditor.Editor.CreateEditor(target);
            }
            _inspector?.OnInspectorGUI();
        }

        private void ClearInspector()
        {
            if (_inspector != null) DestroyImmediate(_inspector);
            _inspector = null;
            _inspectedObject = null;
        }

        private static void OpenPrefab(UIBindingRoot display)
        {
            if (display == null) return;
            string path = AssetDatabase.GetAssetPath(display.gameObject);
            if (string.IsNullOrEmpty(path))
                path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(display.gameObject);
            if (string.IsNullOrEmpty(path))
                path = PrefabStageUtility.GetCurrentPrefabStage()?.assetPath;
            if (string.IsNullOrEmpty(path))
                EditorUtility.DisplayDialog("UI", "当前 Display 尚未保存为 Prefab。", "确定");
            else
                PrefabStageUtility.OpenPrefab(path);
        }
    }
}
