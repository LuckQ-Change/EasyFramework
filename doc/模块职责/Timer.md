# Timer

`TimerModule` 根据所属宿主提供的 `FrameTime` 推进一次性或循环回调。任务 ID 是取消句柄，调用方负责取消不再需要的循环任务，模块关闭会清空全部任务。

`Delay`、`Loop` 和 `Schedule` 默认使用 `TimerTimeMode.Scaled`。指定 `TimerTimeMode.Unscaled` 后，只要宿主仍在推进帧循环，该任务在 `Time.timeScale = 0` 时仍可计时。每个任务独立选择时钟。

长帧时，循环任务每帧最多触发一次，保留不足一个周期的余量。回调中新建的计时器从下次计时器更新开始推进，可以在回调中立即取消。
