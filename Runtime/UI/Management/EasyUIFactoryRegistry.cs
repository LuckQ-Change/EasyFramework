using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace EasyFramework.UI
{
    public static class EasyUIFactoryRegistry
    {
        private sealed class Registration
        {
            public Func<EasyUIObject> CreateLogic;
            public Func<EasyUIBinding> CreateBinding;
        }

        private static readonly Dictionary<string, Registration> Registrations =
            new Dictionary<string, Registration>(StringComparer.Ordinal);
        private static readonly Dictionary<Type, string> PrefabLocations =
            new Dictionary<Type, string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Registrations.Clear();
            PrefabLocations.Clear();
        }

        public static void Register<TLogic, TBinding>(string recordId, string prefabLocation)
            where TLogic : EasyUIObject, new()
            where TBinding : EasyUIBinding, new()
        {
            if (string.IsNullOrWhiteSpace(recordId))
                throw new ArgumentException("UI script record id is required.", nameof(recordId));
            Registrations[recordId] = new Registration
            {
                CreateLogic = () => new TLogic(),
                CreateBinding = () => new TBinding(),
            };
            if (!string.IsNullOrWhiteSpace(prefabLocation))
                PrefabLocations[typeof(TLogic)] = prefabLocation;
        }

        public static bool TryGetPrefabLocation<TLogic>(out string location)
            where TLogic : EasyUIView => TryGetPrefabLocation(typeof(TLogic), out location);

        public static bool TryGetPrefabLocation(Type logicType, out string location)
        {
            if (logicType == null)
            {
                location = null;
                return false;
            }
            for (Type current = logicType; current != null; current = current.BaseType)
                if (PrefabLocations.TryGetValue(current, out location)) return true;
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
            EasyUIScriptRecord record,
            out EasyUIObject logic,
            out EasyUIBinding binding)
        {
            logic = null;
            binding = null;
            if (record == null) return false;
            if (!string.IsNullOrWhiteSpace(record.RecordId) &&
                Registrations.TryGetValue(record.RecordId, out var registration))
            {
                binding = registration.CreateBinding();
                logic = registration.CreateLogic();
                return true;
            }

            Type logicType = Resolve(record.LogicTypeName);
            Type bindingType = Resolve(record.BindingTypeName);
            if (logicType == null || !typeof(EasyUIObject).IsAssignableFrom(logicType)) return false;
            if (bindingType != null && !typeof(EasyUIBinding).IsAssignableFrom(bindingType)) return false;
            logic = Activator.CreateInstance(logicType) as EasyUIObject;
            binding = bindingType == null
                ? new EmptyEasyUIBinding()
                : Activator.CreateInstance(bindingType) as EasyUIBinding;
            return logic != null && binding != null;
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
