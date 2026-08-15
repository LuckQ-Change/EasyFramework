namespace EasyFramework
{
    public enum EasyRuntimeMode
    {
        Editor,
        Online,
        Offline,
        Web,
    }

    /// <summary>Process-wide runtime profile, selected before modules are initialized.</summary>
    public static class EasyRuntime
    {
        public static EasyRuntimeMode Mode { get; private set; } = EasyRuntimeMode.Editor;
        public static bool IsEditor => Mode == EasyRuntimeMode.Editor;
        public static bool IsOnline => Mode == EasyRuntimeMode.Online;
        public static bool IsOffline => Mode == EasyRuntimeMode.Offline;
        public static bool IsWeb => Mode == EasyRuntimeMode.Web;

        internal static void Configure(EasyRuntimeMode mode) => Mode = mode;
        internal static void Reset() => Mode = EasyRuntimeMode.Editor;
    }
}
