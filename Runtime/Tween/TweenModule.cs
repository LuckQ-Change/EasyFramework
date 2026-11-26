using System;

namespace EasyFramework.Tweening
{
    /// <summary>在所属宿主的主循环中推进该宿主的 Tween。</summary>
    public sealed class TweenModule : Module<TweenModule>, IFrameUpdate
    {
        internal TweenQueue Queue { get; } = new TweenQueue();

        /// <summary>在此宿主首次播放动画，或恢复由此宿主暂停的动画。</summary>
        /// <param name="tween">要播放的根动画。</param>
        /// <returns>用于继续控制播放的动画句柄。</returns>
        public TweenHandle Play(TweenHandle tween)
        {
            if (tween == null) throw new ArgumentNullException(nameof(tween));
            using var scope = Host.EnterScope();
            return tween.PlayOn(Queue);
        }

        protected override void OnInit() => Queue.BindHost(Host);

        void IFrameUpdate.Tick(FrameTime frameTime) =>
            Queue.Tick(frameTime.ScaledDeltaTime, frameTime.UnscaledDeltaTime);

        protected override void OnShutdown() => Queue.Close();
    }
}
