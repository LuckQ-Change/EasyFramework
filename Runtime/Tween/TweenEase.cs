namespace EasyFramework.Tweening
{
    /// <summary>插值曲线；Back 可短暂超过终值。</summary>
    public enum TweenEase
    {
        Linear,
        InQuad,
        OutQuad,
        InOutQuad,
        InCubic,
        OutCubic,
        InOutCubic,
        OutBack,
    }

    /// <summary>按游戏时间或不受 timeScale 影响的时间推进。</summary>
    public enum TweenTimeMode
    {
        Scaled,
        Unscaled,
    }
}
