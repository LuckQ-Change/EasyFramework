using System;

namespace EasyFramework.UI
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class EasyUIPrefabAttribute : Attribute
    {
        public string Location { get; }
        public EasyUIPrefabAttribute(string location) => Location = location;
    }
}
