using System;
using System.Reflection;

namespace EasyFramework.UI
{
    public static class UIFactory
    {
        public static string Address(Type type) => type.GetCustomAttribute<UIPrefabAttribute>()?.Location;

        public static Type ResolveType(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            Type type = Type.GetType(name, false);
            if (type != null || name.Contains(",")) return type;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if ((type = assembly.GetType(name, false)) != null) return type;
            return null;
        }
    }
}
