# Loading

`LoadingModule` 管理加载作用域、进度与场景切换；`LoadingPresenter` 只处理展示；`LoadingRuntimeHost` 将 Unity 更新接到展示；`LoadingSettings` 保存配置。依赖 Core 和 Unity 场景 API，不持有业务流程。

调用方拥有加载任务和取消令牌，模块拥有作用域与展示状态。取消、单个监听器异常和关闭后迟到进度不会造成悬挂 UI。`ProcedureAndLoadingTests` 覆盖这些边界；已提交给 Unity 的场景激活仍受 Unity API 生命周期限制。
