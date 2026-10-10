using System;
using System.Threading.Tasks;
using UnityEngine;

namespace EasyFramework
{
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public class FrameworkLauncher : MonoBehaviour
    {
        private static FrameworkLauncher _instance;

        [SerializeField] private FrameworkStartupConfig _startupConfig;
        private FrameworkStartupConfig _activeConfig;

        public static FrameworkLauncher Instance => _instance;
        public FrameworkStartupConfig StartupConfig => _startupConfig;
        public FrameworkStartupConfig ActiveConfig => _activeConfig;

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
                _activeConfig = FrameworkStartupConfig.Resolve(_startupConfig);
                Runtime.Configure(_activeConfig.RuntimeMode);
                if (_activeConfig.ShowDebugWindow && RuntimeDebugWindow.Instance == null)
                    gameObject.AddComponent<RuntimeDebugWindow>();
                Type startupProcedureType = _activeConfig.StartupProcedureType;
                Entry.Init(modules =>
                {
                    ModuleRegistry.ApplyAll(modules);
                    if (startupProcedureType != null)
                    {
                        modules.Register<ProcedureModule>();
                    }
                });
                _activeConfig.Apply();
                if (startupProcedureType != null)
                    _ = StartProcedureSafelyAsync(startupProcedureType);
            }
            catch
            {
                Entry.Shutdown();
                _instance = null;
                throw;
            }
        }

        private void Update()
        {
            Entry.Update(Time.deltaTime);
        }

        private void OnApplicationQuit()
        {
            Entry.Shutdown();
            _activeConfig = null;
            _instance = null;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                Entry.Shutdown();
                _activeConfig = null;
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
            Log.ExceptionHandler = (_, exception) => Debug.LogException(exception);
        }

        private static async Task StartProcedureSafelyAsync(Type startupProcedureType)
        {
            try
            {
                await ProcedureModule.Instance.StartAsync(
                    startupProcedureType,
                    new ProcedureContext());
            }
            catch (OperationCanceledException) { }
            catch (Exception exception)
            {
                Log.Error("[Startup] 启动流程执行失败。", exception);
            }
        }
    }
}
