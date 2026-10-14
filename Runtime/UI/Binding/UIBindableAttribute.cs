using System;

namespace EasyFramework.UI
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    /// <summary>标记允许编辑器列出的业务绑定成员。</summary>
    public sealed class UIBindableAttribute : Attribute
    {
        public string Name { get; }

        public UIBindableAttribute(string name = null) => Name = name;
    }

    /// <summary>向 UI 绑定提供业务数据来源。</summary>
    public interface IUIBindingSource
    {
        bool TryGetBinding(string key, out IReadOnlyReactiveProperty property);
    }
}
