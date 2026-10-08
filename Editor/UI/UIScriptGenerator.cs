using System;
using System.IO;
using System.Text;
using EasyFramework.UI;
using UnityEditor;

namespace EasyFramework.Editor.UI
{
    internal static class UIScriptGenerator
    {
        internal const string DefaultNamespace = "Game.UI";
        internal const string DefaultScriptFolder = "Assets/GameScripts/UI";

        public static string Generate(
            UIDisplay display,
            string className,
            string namespaceName,
            string prefabLocation,
            string scriptFolder)
        {
            if (display == null) throw new ArgumentNullException(nameof(display));
            className = SanitizeTypeName(className);
            namespaceName = string.IsNullOrWhiteSpace(namespaceName) ? DefaultNamespace : namespaceName.Trim();
            string baseTypeName = display.ManagedAsView ? "UIView" : "UIItem";
            prefabLocation = prefabLocation?.Trim();
            if (display.ManagedAsView && string.IsNullOrWhiteSpace(prefabLocation))
                throw new InvalidOperationException("Managed View 必须填写 Prefab Location。");

            string scriptPath = NormalizeAssetPath(scriptFolder) + "/" + className + ".cs";
            bool createdScript = !File.Exists(ToFullPath(scriptPath));
            if (createdScript)
            {
                Directory.CreateDirectory(ToFullPath(Path.GetDirectoryName(scriptPath).Replace('\\', '/')));
                File.WriteAllText(
                    ToFullPath(scriptPath),
                    BuildSource(namespaceName, className, baseTypeName, prefabLocation),
                    new UTF8Encoding(false));
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            string viewTypeName = namespaceName + "." + className;
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
            Type scriptType = script?.GetClass();
            if (scriptType != null)
            {
                if (!typeof(UIObject).IsAssignableFrom(scriptType))
                    throw new InvalidOperationException(
                        $"脚本类型“{scriptType.FullName}”必须继承 UIObject。");
                if (!string.Equals(scriptType.Name, className, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"现有脚本“{scriptPath}”声明的是 {scriptType.Name}，与目标类名 {className} 不一致。");
                if (display.ManagedAsView)
                {
                    if (!UIFactory.TryGetPrefabLocation(scriptType, out string registeredLocation))
                        throw new InvalidOperationException(
                            $"现有 View“{scriptType.FullName}”缺少 UIPrefab 特性，无法确定唯一资源地址。");
                    if (!string.Equals(registeredLocation, prefabLocation, StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            $"现有 View 已注册资源地址“{registeredLocation}”。如需修改，请先修改脚本上的 UIPrefab 特性。");
                    prefabLocation = registeredLocation;
                }
                viewTypeName = $"{scriptType.FullName}, {scriptType.Assembly.GetName().Name}";
            }
            else if (!createdScript)
                throw new InvalidOperationException(
                    $"现有脚本“{scriptPath}”尚未成功编译，暂时不能安全关联。请修复编译错误或等待 Unity 编译完成后重试。");

            Undo.RecordObject(display, "Generate UI script");
            display.SetViewScript(
                viewTypeName,
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
            if (baseTypeName == "UIView")
            {
                builder.AppendLine($"    public sealed class {className}Args");
                builder.AppendLine("    {");
                builder.AppendLine("    }");
                builder.AppendLine();
                builder.AppendLine($"    [UIPrefab(\"{Escape(prefabLocation)}\")] ");
                builder.AppendLine($"    public sealed class {className} : UIView<{className}Args>");
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
