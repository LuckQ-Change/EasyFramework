using System;

namespace EasyFramework.UI
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    /// <summary>声明 View 的 Prefab 资源地址，供 UIService 推导打开位置。</summary>
    public sealed class UIPrefabAttribute : Attribute
    {
        public string Location { get; }
        public UIPrefabAttribute(string location) => Location = location;
    }
}
