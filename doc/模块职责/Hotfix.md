# Hotfix

`HotfixModule` 组织元数据和程序集加载、入口调用；`IHotfixLoader` 是加载边界；`DefaultHotfixLoader` 与 `HybridCLRLoader` 分别实现普通程序集和可选 HybridCLR 适配。依赖 Core，具体 DLL 与目标平台元数据由项目提供。

Loader 由项目注入，模块记录成功加载的程序集；失败或空结果不得进入已加载列表。关闭清理模块状态，入口反射歧义给出定位异常。`HotfixModuleTests` 覆盖默认路径；HybridCLR 需安装包和宏后做目标工程验证。
