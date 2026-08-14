using System;
using System.Collections.Generic;
using System.Reflection;

namespace EasyFramework.UI
{
    internal static class UIBindingSourceResolver
    {
        private sealed class Accessor
        {
            public string Name;
            public Func<object, IReadOnlyReactiveProperty> Get;
        }

        private static readonly Dictionary<Type, Dictionary<string, Accessor>> Cache =
            new Dictionary<Type, Dictionary<string, Accessor>>();

        public static bool TryResolve(object source, string key, out IReadOnlyReactiveProperty property)
        {
            property = null;
            if (source == null || string.IsNullOrWhiteSpace(key)) return false;
            if (source is IUIBindingSource customSource)
            {
                return customSource.TryGetBinding(key, out property) && property != null;
            }

            var accessors = GetAccessors(source.GetType());
            if (!accessors.TryGetValue(key, out var accessor)) return false;
            try
            {
                property = accessor.Get(source);
                return property != null;
            }
            catch (Exception exception)
            {
                Log.Error($"[UI] Cannot read binding '{key}' from {source.GetType().Name}: {exception}");
                return false;
            }
        }

        private static Dictionary<string, Accessor> GetAccessors(Type type)
        {
            if (Cache.TryGetValue(type, out var cached)) return cached;

            var result = new Dictionary<string, Accessor>(StringComparer.Ordinal);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (var field in type.GetFields(flags))
            {
                if (!typeof(IReadOnlyReactiveProperty).IsAssignableFrom(field.FieldType)) continue;
                var attribute = field.GetCustomAttribute<UIBindableAttribute>();
                if (!field.IsPublic && attribute == null) continue;
                string name = string.IsNullOrWhiteSpace(attribute?.Name) ? field.Name : attribute.Name;
                result[name] = new Accessor
                {
                    Name = name,
                    Get = instance => field.GetValue(instance) as IReadOnlyReactiveProperty,
                };
            }

            foreach (var property in type.GetProperties(flags))
            {
                if (!typeof(IReadOnlyReactiveProperty).IsAssignableFrom(property.PropertyType)) continue;
                if (property.GetIndexParameters().Length != 0 || property.GetMethod == null) continue;
                var attribute = property.GetCustomAttribute<UIBindableAttribute>();
                if (!property.GetMethod.IsPublic && attribute == null) continue;
                string name = string.IsNullOrWhiteSpace(attribute?.Name) ? property.Name : attribute.Name;
                result[name] = new Accessor
                {
                    Name = name,
                    Get = instance => property.GetValue(instance, null) as IReadOnlyReactiveProperty,
                };
            }

            Cache[type] = result;
            return result;
        }
    }
}
