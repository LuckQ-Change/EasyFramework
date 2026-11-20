# Tween

`TweenEaseUtility` 只计算缓动；`ValueTween` 计算属性值；`TweenHandle` 管理单条播放状态；`TweenSequence` 编排串并行；`TweenScheduler` 推进活动集合；`TweenModule` 连接 Unity 更新；`TweenFactory` 创建常见类型 Tween。UI 动画依赖 Tween，Tween 不依赖 UI。

调用方可手动 Tick 或交给模块驱动。Owner 销毁和模块关闭会 Kill，完成回调只在动画完成时执行一次。
