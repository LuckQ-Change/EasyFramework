using System;

namespace EasyFramework.UI
{
    /// <summary>按序列化类型名解析 View 类型。</summary>
    public static class UIFactory
    {
        /// <summary>从当前已加载的程序集查找 View 类型。</summary>
        /// <param name="name">完整类型名或带程序集名的类型名。</param>
        /// <returns>匹配的类型，找不到时返回空。</returns>
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
