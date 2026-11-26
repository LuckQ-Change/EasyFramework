namespace EasyFramework
{
    /// <summary>由宿主逐帧提供的缩放与非缩放时间。</summary>
    public readonly struct FrameTime
    {
        /// <summary>本帧经过的游戏时间，单位为秒。</summary>
        public float ScaledDeltaTime { get; }

        /// <summary>本帧实际经过的时间，单位为秒。</summary>
        public float UnscaledDeltaTime { get; }

        /// <summary>记录本帧的两路时间，负值和非有限值按零处理。</summary>
        /// <param name="scaledDeltaTime">受时间缩放影响的秒数。</param>
        /// <param name="unscaledDeltaTime">独立于时间缩放的秒数。</param>
        public FrameTime(float scaledDeltaTime, float unscaledDeltaTime)
        {
            ScaledDeltaTime = NormalizeDelta(scaledDeltaTime);
            UnscaledDeltaTime = NormalizeDelta(unscaledDeltaTime);
        }

        private static float NormalizeDelta(float deltaTime) =>
            deltaTime > 0f && !float.IsPositiveInfinity(deltaTime) ? deltaTime : 0f;
    }

    /// <summary>供模块实现由所属宿主驱动的同步帧更新。</summary>
    public interface IFrameUpdate
    {
        /// <summary>在所属宿主推进一帧。</summary>
        /// <param name="frameTime">宿主为本帧提供的两路时间。</param>
        void Tick(FrameTime frameTime);
    }
}
