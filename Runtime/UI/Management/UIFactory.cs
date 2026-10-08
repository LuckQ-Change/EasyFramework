using System;
using System.Reflection;

namespace EasyFramework.UI
{
    public static class UIFactory
    {
        public static bool TryGetPrefabLocation<TLogic>(out string location)
            where TLogic : UIView => TryGetPrefabLocation(typeof(TLogic), out location);

        public static bool TryGetPrefabLocation(Type logicType, out string location)
        {
            if (logicType == null)
            {
                location = null;
                return false;
            }
            var attribute = logicType.GetCustomAttribute<UIPrefabAttribute>();
            if (attribute != null && !string.IsNullOrWhiteSpace(attribute.Location))
            {
                location = attribute.Location;
                return true;
            }
            location = null;
            return false;
        }

        internal static bool TryCreate(
            string viewTypeName,
            out UIObject logic,
            out UIBinding binding)
        {
            logic = null;
            binding = null;
            Type logicType = ResolveType(viewTypeName);
            if (logicType == null || !typeof(UIObject).IsAssignableFrom(logicType)) return false;
            logic = Activator.CreateInstance(logicType) as UIObject;
            binding = new UIBinding();
            return logic != null;
        }

        internal static Type ResolveType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return null;
            Type type = Type.GetType(typeName, false);
            if (type != null) return type;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName, false);
                if (type != null) return type;
            }

            string fullName = typeName.Split(',')[0].Trim();
            int separator = fullName.LastIndexOf('.');
            string simpleName = separator < 0 ? fullName : fullName.Substring(separator + 1);
            Type uniqueMatch = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException exception) { types = exception.Types; }

                for (int i = 0; i < types.Length; i++)
                {
                    Type candidate = types[i];
                    if (candidate == null || candidate.Name != simpleName ||
                        !typeof(UIObject).IsAssignableFrom(candidate))
                        continue;
                    if (uniqueMatch != null && uniqueMatch != candidate)
                        return null;
                    uniqueMatch = candidate;
                }
            }

            if (uniqueMatch != null)
                Log.Warn($"[UI] 类型“{typeName}”不存在，已按唯一类名解析为“{uniqueMatch.FullName}”。请重新保存 Prefab。");
            return uniqueMatch;
        }
    }
}
