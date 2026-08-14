using System;
using System.Collections.Generic;
using EasyFramework.UI;
using UnityEditor;

namespace EasyFramework.Editor.UI
{
    internal static class EasyUIEditorTypeUtility
    {
        public static string[] GetBusinessBaseTypes(bool managedAsView)
        {
            var names = new List<string> { managedAsView ? "EasyUIView" : "EasyUIItem" };
            foreach (Type type in TypeCache.GetTypesDerivedFrom<EasyUIObject>())
            {
                if (type.IsAbstract || type.ContainsGenericParameters) continue;
                if (managedAsView != typeof(EasyUIView).IsAssignableFrom(type)) continue;
                string name = type.FullName;
                if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name)) names.Add(name);
            }
            names.Sort(1, names.Count - 1, StringComparer.Ordinal);
            return names.ToArray();
        }
    }
}
