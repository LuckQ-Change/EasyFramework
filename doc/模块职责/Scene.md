# Scene

`SceneModule` 管理主场景切换、叠加加载、卸载和场景资源所有权。业务从 Unity 主线程发起操作。默认 `UnitySceneBackend` 按 Build Settings 中的场景加载，交给 Unity 的操作按顺序执行。同名场景使用完整路径定位。`SceneHandle` 标识一个加载实例，`CurrentScene` 和 `GetLoadedScenes()` 可查询本模块管理的场景。

项目界面可读取 `SceneModule.State` 并订阅 `Changed`，按需展示操作类型、阶段、场景路径、进度和错误。`IsRunning` 表示当前操作仍在执行，包括提交后的清理。界面关闭时解除订阅。

## 场景生命周期

- `SwitchAsync` 将目标叠加加载，完成可选的 `prepare` 后设置为活动场景，再卸载先前的主场景。默认宿主首次切换会替换启动时的主场景，同一宿主独立叠加加载的其他场景保留。
- `LoadAdditiveAsync` 加载并管理叠加场景，沿用当前活动场景。`UnloadAsync` 通过 `SceneHandle` 卸载资源所属的实际实例。业务卸载保留至少一个 Unity 场景。关闭宿主时，如需卸载最后一个受管场景，Unity 后端先保留一个空场景作为收尾落点。 [Unity 场景卸载](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/SceneManagement.SceneManager.UnloadSceneAsync.html)
- `SceneHandle.LoadAssetAsync<T>()` 将资源绑定到场景作用域，需要同一个宿主装配 `AssetModule`。实际卸载完成后释放活跃租约，空闲缓存仍按资源模块预算回收。关闭宿主时，场景模块收尾后再关闭其依赖的资源模块。
- 同一宿主可复用已经就绪的叠加场景。`prepare` 只运行在新加载的实例上。`GetLoadedScenes()` 也会列出仍需清理的已加载实例，可以通过 `IsReady` 区分。

## 取消与失败

`prepare` 和状态监听回调应在当前事务结束后再发起下一次场景操作或等待关闭宿主，避免回调反过来等待自己。

- 等待执行期间可以取消。已提交的叠加加载由 Unity 完成，正式切换前若取消或准备失败，则按句柄卸载本次新增场景，再结束任务。叠加加载期间可以触发目标场景内的 Unity 生命周期回调，业务接管可放在准备成功并完成切换之后。 [Unity 场景激活与操作队列](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/AsyncOperation-allowSceneActivation.html) · [Unity 活动场景](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/SceneManagement.SceneManager.SetActiveScene.html)
- 设置活动场景或提交底层卸载后，等待操作实际完成并报告结果。清理作废场景失败时，可从 `GetLoadedScenes()` 取得句柄重试卸载。如果旧主场景卸载失败，新的活动场景仍然可用，旧场景由原句柄保留。`State.Error` 可供自定义界面定位失败。

## 手动验收

- 在 Build Settings 启用两个现有场景。使用同一个默认宿主执行 A → B → A，核对活动场景和 Hierarchy。每次操作结束后，先前的主场景应已卸载，`State.IsRunning` 应为 `false`。
- 在 A 活动时叠加 B，再用返回的句柄卸载 B。叠加期间两个场景都可用，活动场景保持 A。卸载后可再次叠加 B。
- 在等候执行、底层叠加加载及 `prepare` 阶段分别取消。任务结束后核对活动场景、`GetLoadedScenes()` 和操作状态。若准备步骤曾通过场景作用域取得资源，可在 OnGUI Debug 的资源页核对本次操作对应的活跃租约已归还。
- 请求未启用的场景，或让现有准备流程返回错误。核对 `SceneModule.State.Error` 和活动场景，再发起一次有效的切换。停宿主时核对受管叠加场景与租约均已收尾。

[调用示例与配置](../启动流程与运行模式.md#场景操作)
