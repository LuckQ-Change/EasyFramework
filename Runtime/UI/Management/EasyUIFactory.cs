using System;
using System.Reflection;

namespace EasyFramework.UI
{
    public static class EasyUIFactory
    {
        public static bool TryGetPrefabLocation<TLogic>(out string location)
            where TLogic : EasyUIView => TryGetPrefabLocation(typeof(TLogic), out location);

        public static bool TryGetPrefabLocation(Type logicType, out string location)
        {
            if (logicType == null)
            {
                location = null;
                return false;
            }
            var attribute = logicType.GetCustomAttribute<EasyUIPrefabAttribute>();
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
            out EasyUIObject logic,
            out EasyUIBinding binding)
        {
            logic = null;
            binding = null;
            Type logicType = Resolve(viewTypeName);
            if (logicType == null || !typeof(EasyUIObject).IsAssignableFrom(logicType)) return false;
            logic = Activator.CreateInstance(logicType) as EasyUIObject;
            binding = new EasyUIBinding();
            return logic != null;
        }

        private static Type Resolve(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return null;
            Type type = Type.GetType(typeName, false);
            if (type != null) return type;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                type = assembly.GetType(typeName, false);
                if (type != null) return type;
            }
            return null;
        }
    }
}
