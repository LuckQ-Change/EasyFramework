using System;
using UnityEngine;

[assembly: UnityEngine.Scripting.AlwaysLinkAssembly]

namespace EasyFramework.Debugging
{
    /// <summary>将已启用的诊断采集和绘制组件装配到现有框架宿主。</summary>
    internal static class RuntimeDebugBootstrap
    {
        private static RuntimeDebugWindow _window;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            _window = null;
            FrameworkLauncher.Starting -= Attach;
            FrameworkLauncher.Starting += Attach;
        }

        private static void Attach(FrameworkLauncher launcher, FrameworkStartupConfig config)
        {
            if (launcher == null || launcher.HostMode != FrameworkHostMode.Default || config == null ||
                !config.ShowDebugWindow || _window != null) return;

            RuntimeDebugModule collector = null;
            RuntimeDebugWindow window = null;
            try
            {
                // 生命周期跟随启动器，业务模块回滚时仍能查看诊断日志。
                collector = launcher.gameObject.AddComponent<RuntimeDebugModule>();
                window = launcher.gameObject.AddComponent<RuntimeDebugWindow>();
                if (collector == null || window == null)
                    throw new InvalidOperationException("无法装配运行时诊断组件。");
                collector.Initialize(launcher, config);
                window.Initialize(collector);
                _window = window;
            }
            catch
            {
                if (window != null)
                {
                    window.enabled = false;
                    UnityEngine.Object.Destroy(window);
                }
                if (collector != null)
                {
                    collector.enabled = false;
                    UnityEngine.Object.Destroy(collector);
                }
                throw;
            }
        }
    }
}
