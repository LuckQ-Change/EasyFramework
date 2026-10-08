using System.Linq;
using EasyFramework.UI;
using UnityEditor;
using UnityEngine;

namespace EasyFramework.Editor.UI
{
    internal sealed class UIStateWorkbench : EditorWindow
    {
        private UIStateController _controller;
        private Vector2 _scroll;

        [MenuItem("Tools/EasyFramework/UI/State Workbench", false, 20)]
        private static void OpenMenu()
        {
            var window = GetWindow<UIStateWorkbench>("UI States");
            window._controller = Selection.activeGameObject == null
                ? null
                : Selection.activeGameObject.GetComponentInParent<UIStateController>();
        }

        public static void Open(UIStateController controller)
        {
            var window = GetWindow<UIStateWorkbench>("UI States");
            window._controller = controller;
            window.Repaint();
        }

        private void OnGUI()
        {
            _controller = (UIStateController)EditorGUILayout.ObjectField(
                "Controller", _controller, typeof(UIStateController), true);
            if (_controller == null)
            {
                EditorGUILayout.HelpBox(
                    "选择一个 State Controller。点状态名会立刻应用到子节点；改完外观后记录到当前状态。",
                    MessageType.Info);
                return;
            }

            _controller.SyncChildVariants();
            DrawStateBar();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("将当前外观记录到当前状态"))
                RecordCurrent();
            if (GUILayout.Button("同步子节点变体"))
            {
                Undo.RecordObjects(_controller.GetComponentsInChildren<UIElement>(true), "Sync UI variants");
                _controller.SyncChildVariants();
                EditorUtility.SetDirty(_controller);
            }
            EditorGUILayout.EndHorizontal();

            UIElement[] elements = _controller.GetComponentsInChildren<UIElement>(true)
                .Where(item => item.BelongsTo(_controller)).ToArray();
            if (elements.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "还没有 UIElement。在需要随状态变化的节点上添加该组件，不必先替换 Image/Button。",
                    MessageType.Info);
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("节点", GUILayout.Width(220f));
            foreach (string state in _controller.States) GUILayout.Label(state, GUILayout.Width(90f));
            EditorGUILayout.EndHorizontal();
            foreach (UIElement element in elements)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(GetPath(_controller.transform, element.transform), EditorStyles.label, GUILayout.Width(220f)))
                    Selection.activeObject = element;
                foreach (string state in _controller.States)
                {
                    UIStateVariant variant = element.FindVariant(state);
                    string value = variant == null || variant.Properties == UIStateProperty.None
                        ? "默认"
                        : ShortProperties(variant.Properties);
                    if (GUILayout.Button(value, EditorStyles.miniLabel, GUILayout.Width(90f)))
                    {
                        Selection.activeObject = element;
                        ApplyState(state);
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("Validate Display"))
                UIValidator.ValidateAndReport(_controller.GetComponentInParent<UIDisplay>());
        }

        private void DrawStateBar()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("状态", GUILayout.Width(40f));
            foreach (string state in _controller.States)
            {
                bool selected = _controller.SelectedState == state;
                if (GUILayout.Toggle(selected, state, EditorStyles.miniButton) && !selected)
                    ApplyState(state);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(
                $"当前状态：{_controller.SelectedState}。点状态名立即应用到子节点，再改外观并记录。",
                MessageType.None);
        }

        private void ApplyState(string state)
        {
            Undo.RecordObjects(_controller.GetComponentsInChildren<Component>(true), "Apply UI state");
            _controller.SetState(state);
            SceneView.RepaintAll();
        }

        private void RecordCurrent()
        {
            Undo.RecordObjects(_controller.GetComponentsInChildren<UIElement>(true), "Capture UI state");
            _controller.CaptureCurrentToSelectedState();
            EditorUtility.SetDirty(_controller);
            foreach (UIElement element in _controller.GetComponentsInChildren<UIElement>(true))
                EditorUtility.SetDirty(element);
        }

        private static string ShortProperties(UIStateProperty properties)
        {
            if (properties == UIStateProperty.None) return "默认";
            return properties.ToString().Replace("Interactable", "Interact").Replace("SpriteIndex", "Index");
        }

        private static string GetPath(Transform root, Transform target)
        {
            string path = target.name;
            while (target.parent != null && target.parent != root)
            {
                target = target.parent;
                path = target.name + "/" + path;
            }
            return path;
        }
    }
}
