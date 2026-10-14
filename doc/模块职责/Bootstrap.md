# Bootstrap

`FrameworkLauncher` 将 Unity 场景生命周期接到框架启动和关闭；`FrameworkStartupConfig` 保存 UI、运行模式及启动流程配置。`ModuleRegistry` 只注册默认模块和项目扩展，`Runtime` 保存当前运行模式。依赖方向为 Bootstrap → Core/UI，不由业务模块反向启动框架。默认模块为 Event、Timer、Pool、Tween；Asset、Hotfix、Loading、Network、Procedure 按需注册。

配置资产由项目持有，Launcher 持有本次启动生命周期。启动失败由 Core 逆序回滚；重复启动、关闭及晚到注册由 `ModuleManagerTests`、`StartupConfigTests` 覆盖。业务 Loader 必须在调用对应模块加载 API 前设置并完成初始化。
