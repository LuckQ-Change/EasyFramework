using System.Collections.Generic;
using EasyFramework.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace EasyFramework.Editor.UI
{
    [InitializeOnLoad]
    internal static class UIStatePreview
    {
        private static bool _capturing;

        static UIStatePreview()
        {
            Undo.postprocessModifications -= CaptureEditedProperties;
            Undo.postprocessModifications += CaptureEditedProperties;
            Undo.undoRedoPerformed -= OnUndoRedo;
            Undo.undoRedoPerformed += OnUndoRedo;
            PrefabStage.prefabSaving -= OnPrefabSaving;
            PrefabStage.prefabSaving += OnPrefabSaving;
            EditorSceneManager.sceneSaving -= OnSceneSaving;
            EditorSceneManager.sceneSaving += OnSceneSaving;
            AssemblyReloadEvents.beforeAssemblyReload -= RestoreLoaded;
            AssemblyReloadEvents.beforeAssemblyReload += RestoreLoaded;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static UndoPropertyModification[] CaptureEditedProperties(UndoPropertyModification[] modifications)
        {
            if (_capturing || Application.isPlaying) return modifications;
            _capturing = true;
            try
            {
                foreach (UndoPropertyModification modification in modifications)
                {
                    PropertyModification current = modification.currentValue;
                    if (current == null || !TryGetEditedProperty(current, out GameObject owner,
                            out UIStateProperty property)) continue;
                    UIElement element = owner.GetComponent<UIElement>();
                    if (element == null) continue;
                    UIStateController controller = element.Controller;
                    if (controller == null || !controller.IsPreviewing || !element.BelongsTo(controller)) continue;
                    Undo.RecordObject(element, "Record UI state property");
                    if (!element.CaptureProperty(controller.SelectedState, property)) continue;
                    EditorUtility.SetDirty(element);
                }
            }
            finally
            {
                _capturing = false;
            }
            return modifications;
        }

        internal static bool TryGetEditedProperty(PropertyModification modification,
            out GameObject owner, out UIStateProperty property)
        {
            owner = null;
            property = UIStateProperty.None;
            if (modification == null || string.IsNullOrEmpty(modification.propertyPath)) return false;
            Object target = modification.target;
            if (target is GameObject gameObject)
            {
                if (modification.propertyPath != "m_IsActive") return false;
                owner = gameObject;
                property = UIStateProperty.Active;
                return true;
            }
            if (!(target is Component component)) return false;
            string path = modification.propertyPath;
            if (component is Selectable && path == "m_Interactable") property = UIStateProperty.Interactable;
            else if (component is Graphic && path.StartsWith("m_Color.")) property = UIStateProperty.Color;
            else if (component is CanvasGroup && path == "m_Alpha") property = UIStateProperty.Alpha;
            else if (component is Image && path == "m_Sprite") property = UIStateProperty.Sprite;
            else if (component is UIImage && path == "_spriteIndex") property = UIStateProperty.SpriteIndex;
            else if (component is Slider && path == "m_Value" ||
                     component is Scrollbar && path == "m_Value" ||
                     component is Dropdown && path == "m_Value" ||
                     component is Toggle && path == "m_IsOn" ||
                     component is Image && path == "m_FillAmount") property = UIStateProperty.Value;
            else if (path == "m_Text" || path == "m_text") property = UIStateProperty.Text;
            if (property == UIStateProperty.None) return false;
            owner = component.gameObject;
            return true;
        }

        private static void OnPrefabSaving(GameObject root)
        {
            Dictionary<UIStateController, string> previews = CapturePreviewEdits(root);
            RestoreInHierarchy(root);
            ReapplyAfterSave(root, previews);
        }

        private static void OnSceneSaving(Scene scene, string path)
        {
            if (!scene.isLoaded) return;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Dictionary<UIStateController, string> previews = CapturePreviewEdits(root);
                RestoreInHierarchy(root);
                ReapplyAfterSave(root, previews);
            }
        }

        private static Dictionary<UIStateController, string> CapturePreviewEdits(GameObject root)
        {
            var previews = new Dictionary<UIStateController, string>();
            if (root == null || Application.isPlaying) return previews;
            UIStateController[] controllers = root.GetComponentsInChildren<UIStateController>(true);
            foreach (UIStateController controller in controllers)
            {
                if (controller == null || !controller.IsPreviewing) continue;
                previews[controller] = controller.SelectedState;
                UIElement[] elements = controller.GetComponentsInChildren<UIElement>(true);
                foreach (UIElement element in elements)
                {
                    if (element == null || !element.BelongsTo(controller)) continue;
                    Undo.RecordObject(element, "Record UI state property");
                    if (element.CaptureModifiedProperties(controller.SelectedState))
                        EditorUtility.SetDirty(element);
                }
            }
            return previews;
        }

        private static void ReapplyAfterSave(GameObject root,
            Dictionary<UIStateController, string> previews)
        {
            EditorApplication.delayCall += () =>
            {
                if (root == null) return;
                UIStateController[] controllers = root.GetComponentsInChildren<UIStateController>(true);
                for (int i = 0; i < controllers.Length; i++)
                {
                    UIStateController controller = controllers[i];
                    if (controller == null) continue;
                    if (previews.TryGetValue(controller, out string state)) controller.Preview(state);
                }
            };
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) RestoreLoaded();
        }

        private static void OnUndoRedo()
        {
            if (Application.isPlaying) return;
            EditorApplication.delayCall += () =>
            {
                PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null) RefreshPreviewInHierarchy(stage.prefabContentsRoot);
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    Scene scene = SceneManager.GetSceneAt(i);
                    if (!scene.isLoaded) continue;
                    foreach (GameObject root in scene.GetRootGameObjects())
                        RefreshPreviewInHierarchy(root);
                }
                SceneView.RepaintAll();
            };
        }

        private static void RefreshPreviewInHierarchy(GameObject root)
        {
            if (root == null) return;
            foreach (UIStateController controller in root.GetComponentsInChildren<UIStateController>(true))
                if (controller != null && controller.IsPreviewing)
                    controller.RefreshEditorPreviewAfterUndo();
        }

        private static void RestoreLoaded()
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null) RestoreInHierarchy(stage.prefabContentsRoot);
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                    RestoreInHierarchy(root);
            }
        }

        private static void RestoreInHierarchy(GameObject root)
        {
            if (root == null) return;
            UIStateController[] controllers = root.GetComponentsInChildren<UIStateController>(true);
            for (int i = 0; i < controllers.Length; i++)
            {
                if (controllers[i] != null) controllers[i].RestoreSerializedAppearance();
            }
        }
    }
}
