using System;

namespace EasyFramework.UI
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class EasyUIPrefabAttribute : Attribute
    {
        public string Location { get; }
        public EasyUIPrefabAttribute(string location) => Location = location;
    }

    [AttributeUsage(AttributeTargets.Property, Inherited = true)]
    public sealed class EasyUIPathAttribute : Attribute
    {
        public string Path { get; }
        public EasyUIPathAttribute(string path) => Path = path;
    }

    [AttributeUsage(AttributeTargets.Property, Inherited = true)]
    public sealed class EasyUIResourceAttribute : Attribute
    {
        public string Location { get; }
        public EasyUIResourceAttribute(string location) => Location = location;
    }
}
