using EasyFramework.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EasyFramework.Editor.UI
{
    [InitializeOnLoad]
    internal static class EasyUIStatePreview
    {
        static EasyUIStatePreview()
        {
            PrefabStage.prefabSaving -= OnPrefabSaving;
            PrefabStage.prefabSaving += OnPrefabSaving;
            EditorSceneManager.sceneSaving -= OnSceneSaving;
            EditorSceneManager.sceneSaving += OnSceneSaving;
            AssemblyReloadEvents.beforeAssemblyReload -= RestoreLoaded;
            AssemblyReloadEvents.beforeAssemblyReload += RestoreLoaded;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPrefabSaving(GameObject root)
        {
            RestoreInHierarchy(root);
            ReapplyAfterSave(root);
        }

        private static void OnSceneSaving(Scene scene, string path)
        {
            if (!scene.isLoaded) return;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                RestoreInHierarchy(root);
                ReapplyAfterSave(root);
            }
        }

        private static void ReapplyAfterSave(GameObject root)
        {
            EditorApplication.delayCall += () =>
            {
                if (root == null) return;
                EasyUIStateController[] controllers = root.GetComponentsInChildren<EasyUIStateController>(true);
                for (int i = 0; i < controllers.Length; i++)
                {
                    if (controllers[i] != null) controllers[i].Refresh();
                }
            };
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) RestoreLoaded();
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
            EasyUIStateController[] controllers = root.GetComponentsInChildren<EasyUIStateController>(true);
            for (int i = 0; i < controllers.Length; i++)
            {
                if (controllers[i] != null) controllers[i].RestoreSerializedAppearance();
            }
        }
    }
}
