using System;

namespace EasyFramework
{
    /// <summary>由场景模块管理的操作类型。</summary>
    public enum SceneOperationKind
    {
        None,
        Switch,
        LoadAdditive,
        Unload,
    }

    /// <summary>场景操作当前所处的阶段。</summary>
    public enum SceneOperationPhase
    {
        Idle,
        Preparing,
        Loading,
        Activating,
        Unloading,
        CleaningUp,
        Completed,
        Canceled,
        Failed,
    }

    /// <summary>场景操作的只读进度和终态，可由 Loading 或诊断工具观察。</summary>
    public readonly struct SceneOperationState
    {
        /// <summary>状态对应的操作。</summary>
        public SceneOperationKind Kind { get; }
        /// <summary>正在执行或最后结束的阶段。</summary>
        public SceneOperationPhase Phase { get; }
        /// <summary>目标场景路径。解析失败时为调用方提供的名称。</summary>
        public string ScenePath { get; }
        /// <summary>操作的归一化进度。</summary>
        public float Progress { get; }
        /// <summary>应用可以直接展示的状态描述。</summary>
        public string Message { get; }
        /// <summary>失败原因，成功或取消时为空。</summary>
        public Exception Error { get; }
        /// <summary>是否正在执行，包括提交后的清理。</summary>
        public bool IsRunning => Phase is SceneOperationPhase.Preparing or SceneOperationPhase.Loading or
            SceneOperationPhase.Activating or SceneOperationPhase.Unloading or SceneOperationPhase.CleaningUp;

        internal SceneOperationState(SceneOperationKind kind, SceneOperationPhase phase, string scenePath,
            float progress, string message, Exception error = null)
        {
            Kind = kind;
            Phase = phase;
            ScenePath = scenePath;
            Progress = progress;
            Message = message;
            Error = error;
        }
    }
}
