# Bootstrap

`FrameworkLauncher` 将 Unity 场景生命周期接到框架启动和关闭；`FrameworkStartupConfig` 保存 UI、运行模式及启动流程配置。`ModuleRegistry` 只注册默认模块和项目扩展，`Runtime` 保存当前运行模式。依赖方向为 Bootstrap → Core/UI，不由业务模块反向启动框架。默认模块为 Event、Timer、Pool、Tween 和 Audio。Asset、Hotfix、Loading、Network、Procedure 按需注册。

配置资产由项目持有。Launcher 创建并驱动本次运行的模块管理器，退出和销毁共用关闭入口。启动失败由 Core 回滚。Startup Config 可注册资源包和启动流程，资源包在首次请求时初始化。
