using System;
using System.Collections.Generic;
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
        private UIReference _reference;
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
            _reference = null;
            _controller = null;
            _issues = null;
            ClearInspector();
            Repaint();
        }

        private void OnHierarchyChange() => Repaint();
        private void OnProjectChange() => Repaint();

        private void OnGUI()
        {
            GameObject selected = Selection.activeGameObject;
            UIDisplay display = FindDisplay(selected);

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("UI 工作台", new GUIStyle(EditorStyles.boldLabel) { fontSize = 17 });
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("创建页面 / Item", GUILayout.Height(26f)))
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
                EditorGUILayout.ObjectField("所属 Display", display, typeof(UIDisplay), true);
            }
            if (display == null)
            {
                EditorGUILayout.HelpBox("在 Project、Hierarchy 或 Prefab Mode 中选择一个 UIDisplay 页面或其子节点。",
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

        private static UIDisplay FindDisplay(GameObject selected)
        {
            if (selected != null) return selected.GetComponentInParent<UIDisplay>(true);
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            return stage == null ? null : stage.prefabContentsRoot.GetComponent<UIDisplay>();
        }

        private void DrawReferences(UIDisplay display, GameObject selected)
        {
            UIReference[] markers = display.GetComponentsInChildren<UIReference>(true);
            UIReference selectedMarker = selected == null ? null : selected.GetComponent<UIReference>();
            if (_reference == null || _reference.GetComponentInParent<UIDisplay>(true) != display)
                _reference = selectedMarker != null ? selectedMarker :
                    markers.Length > 0 ? markers[0] : null;

            EditorGUILayout.LabelField($"引用节点：{markers.Length}", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            foreach (UIReference marker in markers)
                if (GUILayout.Toggle(marker == _reference, marker.name, EditorStyles.miniButton))
                    _reference = marker;
            EditorGUILayout.EndHorizontal();

            bool canAdd = selected != null &&
                selected.GetComponentInParent<UIDisplay>(true) == display &&
                selected.GetComponent<UIReference>() == null && !AssetDatabase.Contains(selected);
            using (new EditorGUI.DisabledScope(!canAdd))
                if (GUILayout.Button("在选中节点添加 UIReference"))
                    _reference = Undo.AddComponent<UIReference>(selected);

            bool canExpose = selected != null && !AssetDatabase.Contains(selected) &&
                selected.GetComponentInParent<UIDisplay>(true) == display;
            using (new EditorGUI.DisabledScope(!canExpose))
                if (GUILayout.Button("一键暴露选中节点组件"))
                {
                    try
                    {
                        _reference = UIReferenceEditorActions.Expose(display, selected);
                        _referenceMessage = "已添加组件引用。";
                        ClearInspector();
                    }
                    catch (Exception exception) { _referenceMessage = exception.Message; }
                }
            if (GUILayout.Button("生成引用 Key 常量"))
            {
                try
                {
                    string path = UIReferenceCodeGenerator.Generate(display, out List<string> stale);
                    _referenceMessage = stale.Count == 0
                        ? $"已生成 {path}"
                        : $"已生成 {path}，但旧 Key 仍有 {stale.Count} 处业务使用；请查看 Console。";
                    foreach (string usage in stale) Debug.LogWarning("[UI] 旧引用 Key 使用点：" + usage, display);
                }
                catch (Exception exception) { _referenceMessage = exception.Message; }
            }
            if (!string.IsNullOrEmpty(_referenceMessage))
                EditorGUILayout.HelpBox(_referenceMessage, MessageType.Info);

            if (_reference == null)
                EditorGUILayout.HelpBox("在 Prefab 中选择节点并添加 UIReference，再拖入组件。", MessageType.Info);
            else
                DrawInspector(_reference);
        }

        private void DrawStates(UIDisplay display, GameObject selected)
        {
            UIStateController[] controllers = display.GetComponentsInChildren<UIStateController>(true);
            UIStateController selectedController = selected == null
                ? null : selected.GetComponentInParent<UIStateController>(true);
            if (_controller == null || _controller.GetComponentInParent<UIDisplay>(true) != display)
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

        private void DrawValidation(UIDisplay display)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("页面校验", EditorStyles.boldLabel);
            if (GUILayout.Button("重新校验", GUILayout.Width(100f))) _issues = UIValidator.FindIssues(display);
            EditorGUILayout.EndHorizontal();
            if (_issues == null) _issues = UIValidator.FindIssues(display);
            if (_issues.Count == 0)
            {
                EditorGUILayout.HelpBox("当前页面校验通过。", MessageType.Info);
                return;
            }
            foreach (UIValidationIssue issue in _issues)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.HelpBox(issue.Message, MessageType.Warning);
                using (new EditorGUI.DisabledScope(issue.Context == null))
                    if (GUILayout.Button("定位", GUILayout.Width(52f), GUILayout.MinHeight(38f)))
                    {
                        Selection.activeObject = issue.Context;
                        EditorGUIUtility.PingObject(issue.Context);
                    }
                EditorGUILayout.EndHorizontal();
            }
        }

        private static void DrawAnimation(UIDisplay display, GameObject selected)
        {
            UITimeline timeline = selected == null ? null : selected.GetComponent<UITimeline>();
            if (timeline == null) timeline = display.GetComponentInChildren<UITimeline>(true);
            bool canAdd = selected != null && !AssetDatabase.Contains(selected) &&
                selected.GetComponentInParent<UIDisplay>(true) == display &&
                selected.GetComponent<UITimeline>() == null;
            using (new EditorGUI.DisabledScope(!canAdd))
                if (GUILayout.Button("在选中节点添加 UITimeline"))
                    timeline = Undo.AddComponent<UITimeline>(selected);
            if (timeline == null)
                EditorGUILayout.HelpBox("添加 UITimeline 后可配置颜色、位置、缩放或透明度片段。", MessageType.Info);
            else
            {
                EditorGUILayout.ObjectField("时间轴", timeline, typeof(UITimeline), true);
                if (GUILayout.Button("打开可拖动时间轴")) UITimelineWorkbench.Open(timeline);
            }
        }

        private static void DrawAdaptation(UIDisplay display, GameObject selected)
        {
            if (GUILayout.Button("打开分辨率与安全区预览")) UIResolutionPreview.Open(display);
            if (GUILayout.Button("创建可复用 Item 模板")) UICreationWizard.OpenItemTemplate();
            RectTransform rect = selected == null ? null : selected.GetComponent<RectTransform>();
            bool editable = rect != null && !AssetDatabase.Contains(selected) &&
                selected.GetComponentInParent<UIDisplay>(true) == display;
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

        private static void OpenPrefab(UIDisplay display)
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
