using UnityEngine;

namespace EasyFramework
{
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public class EasyFrameworkLauncher : MonoBehaviour
    {
        private static EasyFrameworkLauncher _instance;

        [SerializeField] private EasyFrameworkStartupPreset _startupPreset;
        private EasyFrameworkStartupPreset _activePreset;

        public static EasyFrameworkLauncher Instance => _instance;
        public EasyFrameworkStartupPreset StartupPreset => _startupPreset;
        public EasyFrameworkStartupPreset ActivePreset => _activePreset;

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
            try
            {
                _activePreset = EasyFrameworkStartupPreset.Resolve(_startupPreset);
                EasyRuntime.Configure(_activePreset.RuntimeMode);
                EasyEntry.Init(ModuleRegistry.ApplyAll);
                if (_activePreset.StartProcedureFlow)
                    _ = StartFlowSafelyAsync(_activePreset);
                else
                    _activePreset.Apply();
            }
            catch
            {
                EasyEntry.Shutdown();
                _instance = null;
                throw;
            }
        }

        private void Update()
        {
            EasyEntry.Update(Time.deltaTime);
        }

        private void OnApplicationQuit()
        {
            EasyEntry.Shutdown();
            _activePreset = null;
            _instance = null;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                EasyEntry.Shutdown();
                _activePreset = null;
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

        private static async System.Threading.Tasks.Task StartFlowSafelyAsync(
            EasyFrameworkStartupPreset preset)
        {
            try { await preset.StartFlowAsync(); }
            catch (System.OperationCanceledException) { }
            catch (System.Exception exception)
            {
                Log.Error($"[Startup] procedure flow failed: {exception}");
            }
        }
    }
}
