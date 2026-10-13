using UnityEngine;

namespace EasyFramework.Tweening
{
    /// <summary>把框架主循环接到 Tween 调度器。</summary>
    public sealed class TweenModule : ModuleSingleton<TweenModule>
    {
        protected override void OnUpdate(float deltaTime) =>
            TweenScheduler.Tick(deltaTime, Time.unscaledDeltaTime);

        protected override void OnShutdown() => TweenScheduler.KillAll();
    }
}
