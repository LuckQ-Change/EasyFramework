namespace EasyFramework.UI
{
    /// <summary>定义页面共享背景的显示策略。</summary>
    public enum UIBackgroundMode
    {
        None,
        Shared,
        Modal,
    }

    /// <summary>定义页面在框架 UI 栈中的排序层。</summary>
    public enum UILayer
    {
        Background = -200,
        Screen = 0,
        Window = 100,
        Popup = 200,
        Guide = 300,
        Toast = 400,
        System = 500,
    }
}
