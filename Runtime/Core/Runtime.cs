namespace EasyFramework
{
    /// <summary>表示项目选择的运行环境类型。</summary>
    public enum RuntimeMode
    {
        Editor,
        Online,
        Offline,
        Web,
    }

    /// <summary>读取当前作用域或默认宿主的运行模式。</summary>
    public static class Runtime
    {
        /// <summary>当前宿主未提供运行模式时使用编辑器模式。</summary>
        public static RuntimeMode Mode => ModuleManager.Current?.RuntimeMode ?? RuntimeMode.Editor;
        /// <summary>是否运行于编辑器配置。</summary>
        public static bool IsEditor => Mode == RuntimeMode.Editor;
        /// <summary>是否运行于在线配置。</summary>
        public static bool IsOnline => Mode == RuntimeMode.Online;
        /// <summary>是否运行于离线配置。</summary>
        public static bool IsOffline => Mode == RuntimeMode.Offline;
        /// <summary>是否运行于 Web 配置。</summary>
        public static bool IsWeb => Mode == RuntimeMode.Web;
    }
}
