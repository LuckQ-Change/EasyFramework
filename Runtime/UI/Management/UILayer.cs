namespace EasyFramework.UI
{
    public enum UIBackgroundMode
    {
        None,
        Shared,
        Modal,
    }

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
