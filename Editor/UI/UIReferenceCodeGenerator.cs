using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using EasyFramework.UI;
using UnityEditor;
using UnityEngine;

namespace EasyFramework.Editor.UI
{
    internal static class UIReferenceCodeGenerator
    {
        internal static bool IsIdentifier(string name) =>
            !string.IsNullOrEmpty(name) && Regex.IsMatch(name, "\\A[A-Za-z_][A-Za-z0-9_]*\\z");

        internal static bool IsName(string name) => IsIdentifier(name) && char.IsUpper(name[0]);

        internal static IEnumerable<UIControlReference> Scan(UIBindingRoot display)
        {
            var keywords = UIAuthoringSettings.instance.BindingKeywords;
            foreach (Transform node in display.GetComponentsInChildren<Transform>(true))
            {
                if (node.name.IndexOf('_') < 0) continue;
                UIBindingRoot owner = node.GetComponentInParent<UIBindingRoot>(true);
                bool childNode = owner is UINode && owner.transform == node && owner.ParentBindingRoot == display;
                if (owner != display && !childNode) continue;
                Type type = MappedType(node.name, keywords, out string member);
                if (type == null || childNode && type != typeof(UINode) ||
                    node == display.transform && type == typeof(UINode)) continue;
                if (!IsIdentifier(member) || !char.IsLetter(member[0]))
                    throw new InvalidOperationException($"节点 {node.name} 需要以英文字母开头的控件属性名。");

                if (type == typeof(UINode) && !childNode)
                    throw new InvalidOperationException($"节点 {node.name} 需要挂 UINode。");
                Component[] matches = node.GetComponents(type).Where(component => component != null).ToArray();
                var exact = matches.Where(component => component.GetType() == type).ToArray();
                if (exact.Length != 0) matches = exact;
                if (matches.Length == 0)
                    throw new InvalidOperationException($"节点 {node.name} 缺少关键字指定的 {type.FullName} 组件。");
                if (matches.Length > 1)
                    throw new InvalidOperationException($"节点 {node.name} 有多个匹配 {type.FullName} 的组件。");

                yield return new UIControlReference(char.ToUpperInvariant(member[0]) + member.Substring(1), matches[0]);
            }
        }

        private static Type MappedType(string nodeName,
            ILookup<string, UIAuthoringSettings.BindingKeyword> keywords, out string member)
        {
            int separator = nodeName.IndexOf('_');
            member = separator < 0 ? nodeName : nodeName.Substring(separator + 1);
            if (separator < 0) return null;
            string keyword = nodeName.Substring(0, separator);
            var rules = keywords[keyword];
            var rule = rules.FirstOrDefault();
            if (rule == null) return null;
            if (rules.Skip(1).Any())
                throw new InvalidOperationException($"控件关键字 {keyword} 重复，请在项目设置中修正。");
            return rule.ResolveType() ??
                throw new InvalidOperationException($"控件关键字 {keyword} 缺少可用的组件类型，请在项目设置中修正。");
        }

        internal static bool SameNode(UIControlReference first, UIControlReference second) =>
            first?.Target != null && second?.Target != null && first.Target.gameObject == second.Target.gameObject;

        internal static string ScriptPath(UIBindingRoot display)
        {
            string path = AssetDatabase.GUIDToAssetPath(display.ViewScriptGuid);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException($"{display.name} 缺少可写的 View 脚本。");
            return path;
        }

        internal static string GeneratedPath(UIBindingRoot display, bool prepare = false) =>
            UIGeneratedScriptFiles.PathFor(ScriptPath(display), display.ViewScriptGuid, prepare);

        internal static void Sync(UIBindingRoot display)
        {
            var slots = display.Controls.ToList();
            var names = new HashSet<string>(StringComparer.Ordinal);
            bool changed = false;
            foreach (UIControlReference control in Scan(display))
            {
                if (!names.Add(control.Name))
                    throw new InvalidOperationException($"控件属性 {control.Name} 重复。");
                int index = slots.FindIndex(slot => SameNode(slot, control));
                if (index < 0)
                    index = slots.FindIndex(slot => slot != null && slot.Target == null && slot.Name == control.Name);
                if (index >= 0)
                {
                    UIControlReference previous = slots[index];
                    if (previous.Target != control.Target)
                    {
                        slots[index] = new UIControlReference(previous.Name, control.Target);
                        changed = true;
                    }
                    continue;
                }
                if (slots.Any(slot => slot?.Name == control.Name))
                    throw new InvalidOperationException($"控件引用 {control.Name} 重名或原目标已丢失。");
                slots.Add(control);
                changed = true;
            }
            string source = Source(display, slots);
            Write(GeneratedPath(display, prepare: true), source);
            if (changed)
            {
                display.SetControls(slots.ToArray());
                EditorUtility.SetDirty(display);
            }
        }

        internal static void Write(string path, string source)
        {
            if (File.Exists(path) && File.ReadAllText(path) == source) return;
            File.WriteAllText(path, source, new UTF8Encoding(false));
            EditorApplication.delayCall += () => { if (File.Exists(path)) AssetDatabase.ImportAsset(path); };
        }

        internal static string Source(UIBindingRoot display, IReadOnlyList<UIControlReference> slots)
        {
            Type view = UIFactory.ResolveType(display.ViewTypeName) ??
                throw new InvalidOperationException($"{display.name} 的 View 尚未完成编译。");
            Type[] arguments = ViewArguments(view, display.Kind);
            return Source(view.FullName, display.Kind, display.PrefabLocation,
                arguments.Length > 0 ? TypeSyntax(arguments[0]) : null,
                arguments.Length == 2 ? TypeSyntax(arguments[1]) : null, slots);
        }

        internal static Type[] ViewArguments(Type type, UIPageKind kind)
        {
            if (type != null && type.IsVisible && type.IsSealed && !type.IsGenericType && !type.IsNested &&
                type.GetConstructor(Type.EmptyTypes) != null)
            {
                if (kind == UIPageKind.Item && typeof(UIItem).IsAssignableFrom(type)) return Array.Empty<Type>();
                if (kind == UIPageKind.Node && typeof(UINodeView).IsAssignableFrom(type)) return Array.Empty<Type>();
                if (UIBindingRoot.IsPageKind(kind))
                {
                    for (Type current = type.BaseType; current != null; current = current.BaseType)
                    {
                        if (!current.IsGenericType) continue;
                        Type baseType = current.GetGenericTypeDefinition();
                        if (baseType == typeof(UIPopup<,>) && kind == UIPageKind.Popup ||
                            baseType == typeof(UIView<>) && kind == UIPageKind.Screen)
                            return current.GetGenericArguments();
                        if (baseType == typeof(UIPopup<,>)) break;
                    }
                }
            }
            throw new InvalidOperationException($"{type?.Name ?? "业务脚本"} 需要公开、非泛型的 sealed partial 类、公开无参构造函数和匹配的基类。");
        }

        private static string TypeSyntax(Type type)
        {
            if (type.IsArray)
                return TypeSyntax(type.GetElementType()) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
            if (!type.IsGenericType) return type.FullName;
            Type[] arguments = type.GetGenericArguments();
            int start = 0;
            return Regex.Replace(type.GetGenericTypeDefinition().FullName, "`([0-9]+)", match =>
            {
                int count = int.Parse(match.Groups[1].Value);
                string types = string.Join(", ", arguments.Skip(start).Take(count).Select(argument => TypeName(TypeSyntax(argument))));
                start += count;
                return "<" + types + ">";
            });
        }

        internal static string Source(string fullName, UIPageKind kind, string address, string args, string result,
            IReadOnlyList<UIControlReference> slots)
        {
            int dot = fullName.LastIndexOf('.');
            string name = fullName.Substring(dot + 1);
            string space = dot < 0 ? null : fullName.Substring(0, dot);
            var code = new StringBuilder(UIGeneratedScriptFiles.GeneratedHeader + "\n");
            if (space != null) code.Append("namespace ").Append(space).AppendLine("\n{");
            if (UIBindingRoot.IsPageKind(kind))
                code.Append("    [global::EasyFramework.UI.UIPrefab(\"").Append(Escape(address)).AppendLine("\")]");
            code.AppendLine("    [global::UnityEngine.Scripting.Preserve]");
            code.Append("    public sealed partial class ").Append(name).AppendLine("\n    {");
            if (UIBindingRoot.IsPageKind(kind))
            {
                bool popup = kind == UIPageKind.Popup;
                string generic = name + ", " + TypeName(args) + (popup ? ", " + TypeName(result) : "");
                string method = popup ? "ShowAsync" : "OpenAsync";
                string returns = popup ? $"global::EasyFramework.UI.UIResult<{TypeName(result)}>" : name;
                code.AppendLine($"        public static global::System.Threading.Tasks.Task<{returns}> {method}({TypeName(args)} args = default, global::System.Threading.CancellationToken token = default) =>");
                code.AppendLine($"            global::EasyFramework.UI.UIService.{method}<{generic}>(args, token);");
            }
            var names = new HashSet<string>(StringComparer.Ordinal)
            {
                name, "OpenAsync", "ShowAsync", "Args", "Refresh", "ConfirmAsync", "Rebind",
                "IsOpen", "CloseAsync", "Display", "Control", "BindNode", "Click", "Watch", "LoadAssetAsync", "BindControls",
                "OnCreated", "OnHidden", "OnDispose", "Dispose", "Equals", "GetType", "GetHashCode", "ToString",
            };
            var keywords = UIAuthoringSettings.instance.BindingKeywords;
            var componentTypes = new Type[slots.Count];
            for (int i = 0; i < slots.Count; i++)
            {
                UIControlReference slot = slots[i];
                if (slot == null || !IsName(slot.Name) || !names.Add(slot.Name) || slot.Target == null)
                    throw new InvalidOperationException($"控件名冲突或引用丢失：{slot?.Name}。");
                Type mapped = MappedType(slot.Target.name, keywords, out _);
                Type type;
                if (slot.Target is UINode node)
                {
                    type = UIFactory.ResolveType(node.ViewTypeName);
                    if (type == null)
                        throw new InvalidOperationException($"{node.name} 缺少 Node 业务脚本。");
                    ViewArguments(type, UIPageKind.Node);
                }
                else type = mapped != null && mapped.IsInstanceOfType(slot.Target) ? mapped : slot.Target.GetType();
                if (!type.IsVisible || type.IsGenericType)
                    throw new InvalidOperationException($"{slot.Name} 需要可公开访问的组件类型。");
                componentTypes[i] = type;
                code.AppendLine($"        private {TypeName(type.FullName)} {slot.Name} {{ get; set; }}");
            }
            if (slots.Count > 0)
            {
                code.AppendLine("        protected override void BindControls()\n        {");
                for (int i = 0; i < slots.Count; i++)
                {
                    UIControlReference slot = slots[i];
                    string bind = slot.Target is UINode ? "BindNode" : "Control";
                    code.AppendLine($"            {slot.Name} = {bind}<{TypeName(componentTypes[i].FullName)}>({i}, nameof({slot.Name}));");
                }
                code.AppendLine("        }");
            }
            code.AppendLine("    }");
            if (space != null) code.AppendLine("}");
            return code.ToString().Replace("\r\n", "\n");
        }

        private static string TypeName(string name) => "global::" + name.Replace('+', '.');
        private static string Escape(string value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
