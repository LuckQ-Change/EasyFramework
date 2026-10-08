namespace EasyFramework
{
    public enum RuntimeMode
    {
        Editor,
        Online,
        Offline,
        Web,
    }

    /// <summary>进程级运行环境，在模块初始化前确定。</summary>
    public static class Runtime
    {
        public static RuntimeMode Mode { get; private set; } = RuntimeMode.Editor;
        public static bool IsEditor => Mode == RuntimeMode.Editor;
        public static bool IsOnline => Mode == RuntimeMode.Online;
        public static bool IsOffline => Mode == RuntimeMode.Offline;
        public static bool IsWeb => Mode == RuntimeMode.Web;

        internal static void Configure(RuntimeMode mode) => Mode = mode;
        internal static void Reset() => Mode = RuntimeMode.Editor;
    }
}
