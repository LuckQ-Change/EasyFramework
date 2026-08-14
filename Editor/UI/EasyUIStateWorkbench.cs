using System;
using System.Linq;
using EasyFramework.UI;
using UnityEditor;
using UnityEngine;

namespace EasyFramework.Editor.UI
{
    internal sealed class EasyUIStateWorkbench : EditorWindow
    {
        private EasyUIStateController _controller;
        private Vector2 _scroll;

        [MenuItem("Tools/EasyFramework/UI/State Workbench", false, 20)]
        private static void OpenMenu()
        {
            var window = GetWindow<EasyUIStateWorkbench>("Easy UI States");
            window._controller = Selection.activeGameObject == null
                ? null
                : Selection.activeGameObject.GetComponentInParent<EasyUIStateController>();
        }

        public static void Open(EasyUIStateController controller)
        {
            var window = GetWindow<EasyUIStateWorkbench>("Easy UI States");
            window._controller = controller;
            window.Repaint();
        }

        private void OnGUI()
        {
            _controller = (EasyUIStateController)EditorGUILayout.ObjectField(
                "Controller", _controller, typeof(EasyUIStateController), true);
            if (_controller == null)
            {
                EditorGUILayout.HelpBox("选择一个 State Controller，以“状态 × 节点”方式集中预览和定位配置。", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            foreach (string state in _controller.States)
            {
                bool selected = _controller.SelectedState == state;
                if (GUILayout.Toggle(selected, state, EditorStyles.miniButton) && !selected)
                {
                    Undo.RecordObjects(
                        _controller.GetComponentsInChildren<Component>(true),
                        "Preview Easy UI state");
                    _controller.SetState(state);
                    SceneView.RepaintAll();
                }
            }
            EditorGUILayout.EndHorizontal();

            EasyUIElement[] elements = _controller.GetComponentsInChildren<EasyUIElement>(true)
                .Where(item => item.Controller == _controller).ToArray();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Display Node", GUILayout.Width(220f));
            foreach (string state in _controller.States) GUILayout.Label(state, GUILayout.Width(90f));
            EditorGUILayout.EndHorizontal();
            foreach (EasyUIElement element in elements)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button(GetPath(_controller.transform, element.transform), EditorStyles.label, GUILayout.Width(220f)))
                    Selection.activeObject = element;
                foreach (string state in _controller.States)
                {
                    UIStateVariant variant = element.Variants.FirstOrDefault(item => item != null && item.State == state);
                    string value = variant == null ? "—" : ShortProperties(variant.Properties);
                    GUILayout.Label(value, GUILayout.Width(90f));
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("Validate Display"))
                EasyUIValidator.ValidateAndReport(_controller.GetComponentInParent<EasyUIDisplay>());
        }

        private static string ShortProperties(UIStateProperty properties)
        {
            if (properties == UIStateProperty.None) return "Empty";
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
