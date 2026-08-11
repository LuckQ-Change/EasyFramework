#if UNITY_5_3_OR_NEWER
using UnityEngine;

namespace EasyFramework
{
    public static class EasyFrameworkBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            ModuleRegistry.ClearRegistrars();
        }

#if !EASY_NO_AUTO_BOOTSTRAP
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoStart()
        {
            if (Object.FindObjectOfType<EasyFrameworkLauncher>() != null) return;

            var go = new GameObject("[EasyFramework]");
            Object.DontDestroyOnLoad(go);
            go.AddComponent<EasyFrameworkLauncher>();
        }
#endif
    }
}
#endif
