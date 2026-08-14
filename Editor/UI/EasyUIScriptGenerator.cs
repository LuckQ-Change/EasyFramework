using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using EasyFramework.UI;
using UnityEditor;
using UnityEngine;

namespace EasyFramework.Editor.UI
{
    internal static class EasyUIScriptGenerator
    {
        internal const string DefaultNamespace = "Game.UI";
        internal const string DefaultLogicFolder = "Assets/GameScripts/UI/Logic";
        internal const string DefaultBindingFolder = "Assets/GameScripts/UI/Generated/Bindings";

        private sealed class ReferenceInfo
        {
            public string PropertyName;
            public string Path;
            public string TypeName;
            public string ResourceLocation;
        }

        public static string Generate(
            EasyUIDisplay display,
            string className,
            string namespaceName,
            string baseTypeName,
            string prefabLocation,
            string logicFolder,
            string bindingFolder)
        {
            if (display == null) throw new ArgumentNullException(nameof(display));
            className = SanitizeTypeName(className);
            namespaceName = string.IsNullOrWhiteSpace(namespaceName) ? DefaultNamespace : namespaceName.Trim();
            baseTypeName = string.IsNullOrWhiteSpace(baseTypeName)
                ? (display.ManagedAsView ? "EasyUIView" : "EasyUIItem")
                : baseTypeName.Trim();
            if (baseTypeName == typeof(EasyUIView).FullName) baseTypeName = "EasyUIView";
            if (baseTypeName == typeof(EasyUIItem).FullName) baseTypeName = "EasyUIItem";
            prefabLocation = prefabLocation?.Trim();
            if (display.ManagedAsView && string.IsNullOrWhiteSpace(prefabLocation))
                throw new InvalidOperationException("Managed View 必须填写 Prefab Location，生成后用于无字符串 OpenAsync。");

            string recordId = string.IsNullOrWhiteSpace(display.Scripts.RecordId)
                ? Guid.NewGuid().ToString("N")
                : display.Scripts.RecordId;
            string logicTypeName = namespaceName + "." + className;
            string bindingName = className + "Binding";
            string bindingTypeName = namespaceName + ".Generated." + bindingName;
            string bindingBaseTypeName = ResolveBindingBaseType(namespaceName, baseTypeName);
            string logicPath = NormalizeAssetPath(logicFolder) + "/" + className + ".cs";
            string bindingPath = NormalizeAssetPath(bindingFolder) + "/" + bindingName + ".g.cs";
            List<ReferenceInfo> references = CollectReferences(display.transform);
            UIBindingContext context = display.BindingContext;
            string displaySignature = ComputeSignature(display);
            context.SetGeneratedBindingId(recordId);
            EditorUtility.SetDirty(context);

            EnsureFolder(Path.GetDirectoryName(bindingPath));
            WriteGenerated(bindingPath, BuildBindingSource(
                namespaceName, className, bindingName, recordId, logicTypeName,
                prefabLocation, references, context, bindingBaseTypeName));

            if (!File.Exists(ToFullPath(logicPath)))
            {
                EnsureFolder(Path.GetDirectoryName(logicPath));
                File.WriteAllText(
                    ToFullPath(logicPath),
                    BuildLogicSource(namespaceName, className, bindingName, baseTypeName),
                    new UTF8Encoding(false));
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            Undo.RecordObject(display, "Generate Easy UI scripts");
            display.SetScriptRecord(
                recordId,
                logicTypeName,
                bindingTypeName,
                baseTypeName,
                logicPath,
                bindingPath,
                displaySignature,
                prefabLocation);
            EditorUtility.SetDirty(display);
            PrefabUtility.RecordPrefabInstancePropertyModifications(display);
            AssetDatabase.SaveAssets();
            return bindingPath;
        }

        private static List<ReferenceInfo> CollectReferences(Transform root)
        {
            var result = new List<ReferenceInfo>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (EasyUIReference marker in root.GetComponentsInChildren<EasyUIReference>(true))
            {
                foreach (EasyUIReferenceEntry entry in marker.Entries)
                {
                    if (entry == null || entry.Target == null || string.IsNullOrWhiteSpace(entry.PropertyName)) continue;
                    string propertyName = SanitizeTypeName(entry.PropertyName);
                    if (!names.Add(propertyName))
                        throw new InvalidOperationException($"生成属性名重复：{propertyName}");
                    result.Add(new ReferenceInfo
                    {
                        PropertyName = propertyName,
                        Path = GetPath(root, entry.Target.transform),
                        TypeName = "global::" + entry.Target.GetType().FullName.Replace('+', '.'),
                        ResourceLocation = entry.ResourceLocation?.Trim(),
                    });
                }
            }
            return result;
        }

        private static string BuildBindingSource(
            string rootNamespace,
            string logicName,
            string bindingName,
            string recordId,
            string logicTypeName,
            string prefabLocation,
            IReadOnlyList<ReferenceInfo> references,
            UIBindingContext context,
            string bindingBaseTypeName)
        {
            var builder = new StringBuilder();
            builder.AppendLine("// <auto-generated>");
            builder.AppendLine("// EasyFramework generated display binding. Edit the prefab markers and regenerate.");
            builder.AppendLine("// </auto-generated>");
            builder.AppendLine("using EasyFramework.UI;");
            builder.AppendLine("using UnityEngine;");
            builder.AppendLine();
            builder.AppendLine($"namespace {rootNamespace}.Generated");
            builder.AppendLine("{");
            builder.AppendLine($"    public class {bindingName} : {bindingBaseTypeName}");
            builder.AppendLine("    {");
            foreach (ReferenceInfo reference in references)
            {
                builder.AppendLine($"        [EasyUIPath(\"{Escape(reference.Path)}\")]");
                if (!string.IsNullOrWhiteSpace(reference.ResourceLocation))
                    builder.AppendLine($"        [EasyUIResource(\"{Escape(reference.ResourceLocation)}\")]");
                builder.AppendLine($"        public {reference.TypeName} {reference.PropertyName} {{ get; private set; }}");
                if (!string.IsNullOrWhiteSpace(reference.ResourceLocation))
                    builder.AppendLine($"        public const string {reference.PropertyName}Resource = \"{Escape(reference.ResourceLocation)}\";");
                builder.AppendLine();
            }
            builder.AppendLine("        protected override void OnBind()");
            builder.AppendLine("        {");
            builder.AppendLine("            base.OnBind();");
            foreach (ReferenceInfo reference in references)
                builder.AppendLine($"            {reference.PropertyName} = Find<{reference.TypeName}>(\"{Escape(reference.Path)}\");");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    internal static class " + bindingName + "Registration");
            builder.AppendLine("    {");
            builder.AppendLine("        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]");
            builder.AppendLine("        private static void Register()");
            builder.AppendLine("        {");
            builder.AppendLine(
                $"            EasyUIFactoryRegistry.Register<global::{logicTypeName}, {bindingName}>(\"{Escape(recordId)}\", \"{Escape(prefabLocation)}\");");
            builder.AppendLine(
                $"            GeneratedUIBindingRegistry.Register(\"{Escape(recordId)}\", InstallReactiveBindings);");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        private static void InstallReactiveBindings(UIBindingContext context)");
            builder.AppendLine("        {");
            foreach (UIBindingDefinition binding in context.Bindings.Where(item => item != null && item.Target != null))
            {
                string path = GetPath(context.transform, binding.Target.transform);
                string typeName = "global::" + binding.Target.GetType().FullName.Replace('+', '.');
                builder.Append("            context.RegisterGenerated(\"")
                    .Append(Escape(path)).Append("\", typeof(").Append(typeName).Append("), UIBindingProperty.")
                    .Append(binding.TargetProperty).Append(", \"").Append(Escape(binding.SourceKey)).Append("\", \"")
                    .Append(Escape(binding.Format)).Append("\", ").Append(binding.TwoWay ? "true" : "false").AppendLine(");");
            }
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine("}");
            builder.AppendLine();
            builder.AppendLine($"namespace {rootNamespace}");
            builder.AppendLine("{");
            builder.AppendLine($"    [EasyUIPrefab(\"{Escape(prefabLocation)}\")]");
            builder.AppendLine($"    public partial class {logicName} {{ }}");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static string BuildLogicSource(
            string rootNamespace,
            string className,
            string bindingName,
            string baseTypeName)
        {
            string bindingType = "global::" + rootNamespace + ".Generated." + bindingName;
            string baseDeclaration;
            if (baseTypeName == "EasyUIView")
                baseDeclaration = $"EasyUIView<{bindingType}, {className}Args>";
            else
                baseDeclaration = baseTypeName;

            var builder = new StringBuilder();
            builder.AppendLine("using EasyFramework.UI;");
            builder.AppendLine();
            builder.AppendLine($"namespace {rootNamespace}");
            builder.AppendLine("{");
            if (baseTypeName == "EasyUIView")
            {
                builder.AppendLine($"    public sealed class {className}Args");
                builder.AppendLine("    {");
                builder.AppendLine("        // Add strongly typed open parameters here.");
                builder.AppendLine("    }");
                builder.AppendLine();
            }
            builder.AppendLine($"    public partial class {className} : {baseDeclaration}");
            builder.AppendLine("    {");
            if (baseTypeName == "EasyUIView")
            {
                builder.AppendLine($"        protected override void OnOpened({className}Args args)");
                builder.AppendLine("        {");
                builder.AppendLine("        }");
            }
            else
            {
                builder.AppendLine($"        public new {bindingType} Binding => ({bindingType})base.Binding;");
                builder.AppendLine();
                builder.AppendLine("        protected override void OnCreated()");
                builder.AppendLine("        {");
                builder.AppendLine("        }");
            }
            builder.AppendLine("    }");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static string ResolveBindingBaseType(string rootNamespace, string baseTypeName)
        {
            if (baseTypeName == "EasyUIView" || baseTypeName == "EasyUIItem" ||
                baseTypeName == typeof(EasyUIView).FullName || baseTypeName == typeof(EasyUIItem).FullName)
                return "EasyUIBinding";

            string fullName = baseTypeName.Contains(".") ? baseTypeName : rootNamespace + "." + baseTypeName;
            Type baseType = ResolveType(fullName);
            if (baseType == null)
                throw new InvalidOperationException(
                    $"找不到业务基类 {fullName}。请先生成并等待基类编译完成，再生成派生 Display。");
            PropertyInfo binding = baseType.GetProperty(
                "Binding", BindingFlags.Instance | BindingFlags.Public);
            if (binding == null || !typeof(EasyUIBinding).IsAssignableFrom(binding.PropertyType) ||
                binding.PropertyType == typeof(EasyUIBinding))
                return "EasyUIBinding";
            return "global::" + binding.PropertyType.FullName.Replace('+', '.');
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }

        private static void WriteGenerated(string assetPath, string content)
        {
            string fullPath = ToFullPath(assetPath);
            if (File.Exists(fullPath)) File.SetAttributes(fullPath, FileAttributes.Normal);
            File.WriteAllText(fullPath, content, new UTF8Encoding(false));
            File.SetAttributes(fullPath, File.GetAttributes(fullPath) | FileAttributes.ReadOnly);
        }

        private static string GetPath(Transform root, Transform target)
        {
            if (root == target) return string.Empty;
            var names = new List<string>();
            while (target != null && target != root)
            {
                if (target.parent != null)
                {
                    int sameNameCount = 0;
                    for (int i = 0; i < target.parent.childCount; i++)
                        if (target.parent.GetChild(i).name == target.name) sameNameCount++;
                    if (sameNameCount > 1)
                        throw new InvalidOperationException(
                            $"路径存在同级重名节点 '{target.name}'，无法生成稳定属性路径。");
                }
                names.Add(target.name);
                target = target.parent;
            }
            if (target != root) throw new InvalidOperationException("标记组件必须位于 EasyUIDisplay 子树中。");
            names.Reverse();
            return string.Join("/", names);
        }

        private static void EnsureFolder(string assetFolder)
        {
            if (string.IsNullOrWhiteSpace(assetFolder)) return;
            Directory.CreateDirectory(ToFullPath(assetFolder.Replace('\\', '/')));
        }

        private static string NormalizeAssetPath(string path)
        {
            string result = string.IsNullOrWhiteSpace(path) ? "Assets" : path.Trim().Replace('\\', '/').TrimEnd('/');
            if (!result.StartsWith("Assets/", StringComparison.Ordinal) && result != "Assets")
                throw new InvalidOperationException($"生成路径必须位于 Assets 下：{result}");
            return result;
        }

        private static string ToFullPath(string assetPath) => Path.GetFullPath(assetPath);

        private static string SanitizeTypeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("脚本或属性名称不能为空。");
            var builder = new StringBuilder();
            foreach (char character in value.Trim())
                builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
            if (char.IsDigit(builder[0])) builder.Insert(0, '_');
            return builder.ToString();
        }

        private static string Escape(string value) => (value ?? string.Empty)
            .Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");

        internal static string ComputeSignature(EasyUIDisplay display)
        {
            if (display == null) return string.Empty;
            var builder = new StringBuilder();
            foreach (Transform node in display.GetComponentsInChildren<Transform>(true))
            {
                builder.Append(GetSignaturePath(display.transform, node)).Append('|');
                foreach (Component component in node.GetComponents<Component>())
                    builder.Append(component == null ? "Missing" : component.GetType().FullName).Append(';');
            }
            foreach (UIBindingDefinition binding in display.BindingContext.Bindings)
            {
                if (binding == null) continue;
                builder.Append("B:").Append(binding.Target == null ? "Missing" : GetSignaturePath(display.transform, binding.Target.transform))
                    .Append(':').Append(binding.TargetProperty).Append(':').Append(binding.SourceKey)
                    .Append(':').Append(binding.Format).Append(':').Append(binding.TwoWay).Append('|');
            }
            ulong hash = 14695981039346656037UL;
            foreach (char character in builder.ToString())
            {
                hash ^= character;
                hash *= 1099511628211UL;
            }
            return hash.ToString("x16");
        }

        private static string GetSignaturePath(Transform root, Transform target)
        {
            if (root == target) return string.Empty;
            var parts = new List<string>();
            while (target != null && target != root)
            {
                parts.Add(target.name + "#" + target.GetSiblingIndex());
                target = target.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
