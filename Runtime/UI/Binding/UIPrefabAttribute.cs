using System;

namespace EasyFramework.UI
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class UIPrefabAttribute : Attribute
    {
        public string Location { get; }
        public UIPrefabAttribute(string location) => Location = location;
    }
}
