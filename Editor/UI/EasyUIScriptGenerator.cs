using System;
using System.IO;
using System.Text;
using EasyFramework.UI;
using UnityEditor;

namespace EasyFramework.Editor.UI
{
    internal static class EasyUIScriptGenerator
    {
        internal const string DefaultNamespace = "Game.UI";
        internal const string DefaultScriptFolder = "Assets/GameScripts/UI";

        public static string Generate(
            EasyUIDisplay display,
            string className,
            string namespaceName,
            string prefabLocation,
            string scriptFolder)
        {
            if (display == null) throw new ArgumentNullException(nameof(display));
            className = SanitizeTypeName(className);
            namespaceName = string.IsNullOrWhiteSpace(namespaceName) ? DefaultNamespace : namespaceName.Trim();
            string baseTypeName = display.ManagedAsView ? "EasyUIView" : "EasyUIItem";
            prefabLocation = prefabLocation?.Trim();
            if (display.ManagedAsView && string.IsNullOrWhiteSpace(prefabLocation))
                throw new InvalidOperationException("Managed View 必须填写 Prefab Location。");

            string scriptPath = NormalizeAssetPath(scriptFolder) + "/" + className + ".cs";
            if (!File.Exists(ToFullPath(scriptPath)))
            {
                Directory.CreateDirectory(ToFullPath(Path.GetDirectoryName(scriptPath).Replace('\\', '/')));
                File.WriteAllText(
                    ToFullPath(scriptPath),
                    BuildSource(namespaceName, className, baseTypeName, prefabLocation),
                    new UTF8Encoding(false));
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            Undo.RecordObject(display, "Generate Easy UI script");
            display.SetViewScript(
                namespaceName + "." + className,
                scriptPath,
                prefabLocation);
            EditorUtility.SetDirty(display);
            PrefabUtility.RecordPrefabInstancePropertyModifications(display);
            AssetDatabase.SaveAssets();
            return scriptPath;
        }

        private static string BuildSource(
            string rootNamespace,
            string className,
            string baseTypeName,
            string prefabLocation)
        {
            var builder = new StringBuilder();
            builder.AppendLine("using EasyFramework.UI;");
            builder.AppendLine();
            builder.AppendLine($"namespace {rootNamespace}");
            builder.AppendLine("{");
            if (baseTypeName == "EasyUIView")
            {
                builder.AppendLine($"    public sealed class {className}Args");
                builder.AppendLine("    {");
                builder.AppendLine("    }");
                builder.AppendLine();
                builder.AppendLine($"    [EasyUIPrefab(\"{Escape(prefabLocation)}\")] ");
                builder.AppendLine($"    public sealed class {className} : EasyUIView<{className}Args>");
                builder.AppendLine("    {");
                builder.AppendLine($"        protected override void OnOpened({className}Args args)");
                builder.AppendLine("        {");
                builder.AppendLine("            // Example: Binding.Get<UnityEngine.UI.Button>(\"CloseButton\");");
                builder.AppendLine("        }");
            }
            else
            {
                builder.AppendLine($"    public sealed class {className} : {baseTypeName}");
                builder.AppendLine("    {");
                builder.AppendLine("        protected override void OnCreated()");
                builder.AppendLine("        {");
                builder.AppendLine("        }");
            }
            builder.AppendLine("    }");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static string NormalizeAssetPath(string path)
        {
            string result = string.IsNullOrWhiteSpace(path)
                ? DefaultScriptFolder
                : path.Trim().Replace('\\', '/').TrimEnd('/');
            if (!result.StartsWith("Assets/", StringComparison.Ordinal) && result != "Assets")
                throw new InvalidOperationException($"脚本路径必须位于 Assets 下：{result}");
            return result;
        }

        private static string ToFullPath(string assetPath) => Path.GetFullPath(assetPath);

        private static string SanitizeTypeName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("脚本名称不能为空。");
            var builder = new StringBuilder();
            foreach (char character in value.Trim())
                builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
            if (char.IsDigit(builder[0])) builder.Insert(0, '_');
            return builder.ToString();
        }

        private static string Escape(string value) => (value ?? string.Empty)
            .Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
