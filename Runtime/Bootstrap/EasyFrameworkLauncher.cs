#if UNITY_5_3_OR_NEWER
using UnityEngine;

namespace EasyFramework
{
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public class EasyFrameworkLauncher : MonoBehaviour
    {
        private static EasyFrameworkLauncher _instance;

        public static EasyFrameworkLauncher Instance => _instance;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            RedirectLogToUnity();
            EasyEntry.Init(ModuleRegistry.ApplyAll);
        }

        private void Update()
        {
            EasyEntry.Update(Time.deltaTime);
        }

        private void OnApplicationQuit()
        {
            EasyEntry.Shutdown();
            _instance = null;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                EasyEntry.Shutdown();
                _instance = null;
            }
        }

        private static void RedirectLogToUnity()
        {
            Log.Handler = (level, msg) =>
            {
                switch (level)
                {
                    case LogLevel.Warn: Debug.LogWarning(msg); break;
                    case LogLevel.Error: Debug.LogError(msg); break;
                    default: Debug.Log(msg); break;
                }
            };
        }
    }
}
#endif
