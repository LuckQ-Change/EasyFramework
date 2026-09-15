using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EasyFramework.UI;

namespace EasyFramework.Editor.UI
{
    internal static class UIBindingEditorReflection
    {
        public static Type ResolveSourceType(UIBindingContext context)
        {
            if (context == null) return null;
            if (context.Source != null) return context.Source.GetType();
            EasyUIDisplay display = context.GetComponentInParent<EasyUIDisplay>();
            string typeName = display == null ? null : display.ViewTypeName;
            if (string.IsNullOrWhiteSpace(typeName)) return null;

            // Display 保存的是程序集限定名称。Assembly.GetType 只能接收不带程序集后缀的
            // FullName，因此先走 Type.GetType，再为旧 Prefab 的非限定名称保留遍历回退。
            Type resolved = Type.GetType(typeName, false);
            if (resolved != null) return resolved;
            string fullName = typeName.Split(',')[0].Trim();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }

        public static string[] GetBindableMembers(Type type)
        {
            if (type == null) return Array.Empty<string>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (FieldInfo field in type.GetFields(flags))
            {
                var attribute = field.GetCustomAttribute<UIBindableAttribute>();
                if (!typeof(IReadOnlyReactiveProperty).IsAssignableFrom(field.FieldType) ||
                    (!field.IsPublic && attribute == null)) continue;
                names.Add(string.IsNullOrWhiteSpace(attribute?.Name) ? field.Name : attribute.Name);
            }
            foreach (PropertyInfo property in type.GetProperties(flags))
            {
                var attribute = property.GetCustomAttribute<UIBindableAttribute>();
                if (!typeof(IReadOnlyReactiveProperty).IsAssignableFrom(property.PropertyType) ||
                    property.GetMethod == null || (!property.GetMethod.IsPublic && attribute == null)) continue;
                names.Add(string.IsNullOrWhiteSpace(attribute?.Name) ? property.Name : attribute.Name);
            }
            return names.OrderBy(item => item).ToArray();
        }

        public static bool HasBindableMember(Type type, string name) =>
            GetBindableMembers(type).Contains(name, StringComparer.Ordinal);
    }
}
