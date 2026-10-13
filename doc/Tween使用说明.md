# Tween 使用说明

Tween 是 EasyFramework 默认注册的运行时模块。`TweenFactory` 创建动画，`TweenHandle` 管理播放状态，`TweenSequence` 组合动画，`TweenScheduler` 负责逐帧推进。`TweenModule` 将调度器接到框架主循环。它们各自只处理一个职责。

## 数值动画

```csharp
using EasyFramework.Tweening;

TweenHandle fade = TweenFactory.To(group.alpha, 1f, 0.2f,
        value => group.alpha = value)
    .SetEase(TweenEase.OutCubic)
    .SetTimeMode(TweenTimeMode.Unscaled)
    .SetOwner(group)
    .OnComplete(() => group.blocksRaycasts = true)
    .Play();
```

`To` 支持 `float`、`Vector2`、`Vector3` 和 `Color`。`SetOwner` 接受 Unity 对象；对象销毁后，下一次调度会停止动画。调用 `Kill()` 会保留当前值，且不会执行 `OnComplete`；调用 `Complete()` 会立即写入终值，并且只执行一次完成回调。`Pause()` 保留进度；`Play()` 继续播放；`Restart()` 从起点重播。

## 组合动画

```csharp
var sequence = TweenFactory.Sequence()
    .Append(TweenFactory.To(0f, 1f, 0.2f, value => group.alpha = value))
    .Join(TweenFactory.To(startScale, endScale, 0.2f,
        value => rect.localScale = value))
    .AppendInterval(0.1f)
    .Append(TweenFactory.To(startColor, endColor, 0.2f,
        value => image.color = value));

sequence.SetTimeMode(TweenTimeMode.Unscaled).SetOwner(group).Play();
```

`Append` 在当前末尾开始，`Join` 与上一次加入的动画同时开始，`Insert` 指定开始时间。子动画加入 Sequence 后由 Sequence 独占推进，不能单独播放；需要重复播放时对 Sequence 调用 `Restart()`。

## 预览与时间

编辑器和测试可对尚未播放或已暂停的动画调用 `Seek(seconds)` 设置画面，或通过 `TweenScheduler.Tick(scaledDelta, unscaledDelta)` 手动推进。`Seek` 不派发完成回调，播放中的动画需先 `Pause()`。运行时通常由默认注册的 TweenModule 推进，不应另行调用 Tick。页面转场默认使用非缩放时间，使游戏暂停时也能播放。

编辑器预览结束时需要恢复预览前记录的组件属性；`Seek(0)` 只返回动画起点，不负责恢复用户原本的 Prefab 值。
