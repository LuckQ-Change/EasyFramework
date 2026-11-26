using System.Runtime.CompilerServices;

// Unity 运行时装配读取内部模块注册，并在宿主生命周期回调中阻止自等待。
[assembly: InternalsVisibleTo("com.wjq.easyframework.runtime")]
