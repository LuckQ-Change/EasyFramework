using System;
using System.Threading.Tasks;
using UnityEngine;

namespace EasyFramework
{
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public class EasyFrameworkLauncher : MonoBehaviour
    {
        private static EasyFrameworkLauncher _instance;

        [SerializeField] private EasyFrameworkStartupConfig _startupConfig;
        private EasyFrameworkStartupConfig _activeConfig;

        public static EasyFrameworkLauncher Instance => _instance;
        public EasyFrameworkStartupConfig StartupConfig => _startupConfig;
        public EasyFrameworkStartupConfig ActiveConfig => _activeConfig;

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
                _activeConfig = EasyFrameworkStartupConfig.Resolve(_startupConfig);
                EasyRuntime.Configure(_activeConfig.RuntimeMode);
                Type startupProcedureType = _activeConfig.StartupProcedureType;
                EasyEntry.Init(modules =>
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
            _activeConfig = null;
            _instance = null;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                EasyEntry.Shutdown();
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
