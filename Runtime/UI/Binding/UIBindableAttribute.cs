using System;

namespace EasyFramework.UI
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public sealed class UIBindableAttribute : Attribute
    {
        public string Name { get; }

        public UIBindableAttribute(string name = null) => Name = name;
    }

    public interface IUIBindingSource
    {
        bool TryGetBinding(string key, out IReadOnlyReactiveProperty property);
    }
}
