# 音频管理

`FrameworkLauncher` 默认注册 `AudioModule`。将 `AudioClip` 收进 Startup Config 中的资源包，使用资源地址播放。播放组件会在首次播放时创建，并随框架跨场景保留。场景使用现有的 `AudioListener`。

```csharp
await FrameworkLauncher.Instance.Ready;
var audio = AudioModule.Instance;

await audio.PlayMusicAsync("main:audio/lobby");
long sound = await audio.PlaySoundAsync("main:audio/click");

audio.SetVolume(AudioChannel.Master, 0.8f);
audio.SetVolume(AudioChannel.Music, 0.6f);
audio.SetMuted(AudioChannel.Sound, true);
```

- BGM 默认循环。新曲就绪时替换旧曲，重复播放正在播放的同一曲目会保留进度。若请求被更新的 BGM 请求取代，或被 `StopMusic()` 取消，返回 `false`。
- 一次性音效返回播放 ID，可以用 `StopSound(id)` 提前停止。`MaxSoundVoices` 默认是 16，同时用于播放上限和普通待播队列上限，两者分别计数。`StopSounds()` 会停止全部音效并取消待播请求。
- 常用点击音放在本地引用包里，通过 `PlayFeedbackAsync(address)` 播放。即时反馈有独立的待播名额，满声道时可以替换最早开始的普通音效。演示场景在展开或折叠列表分组时会使用本地点击音。
- 玩法可通过 `CreateSoundScope()` 创建作用域，退出时释放，并批量取消它的待播音效。界面内用 `PlaySoundAsync(address, feedback: true)` 播放反馈音时，框架在页面隐藏或刷新后取消它所属的待播请求。已经开始的短音效继续播完。
- 总音量与分类音量相乘。总静音与分类静音可独立控制。音量状态在当前框架实例内生效，可由项目从自己的设置存档读取。
- 调用方传入的取消令牌控制待播请求，播放开始后可调用停止方法。音源回收时先清掉 `AudioSource.clip`，再归还租约，底层资源进入原有空闲缓存策略。
