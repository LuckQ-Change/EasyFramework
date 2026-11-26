using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using EasyFramework.UI;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;

namespace EasyFramework.Editor.UI
{
    internal sealed class UIControlRename
    {
        private sealed class Edit
        {
            internal string Source;
            internal readonly List<(int Start, int Length)> Spans = new();
        }

        private readonly UIBindingRoot _display;
        private readonly UIControlReference _slot;
        private readonly string _newName;
        private readonly Dictionary<string, Edit> _edits;
        private readonly string _generated;
        private readonly string _originalGenerated;

        internal string Description => $"{_slot.Name} → {_newName}";
        internal IEnumerable<(string Path, int Line)> Usages =>
            _edits.SelectMany(pair => pair.Value.Spans.Select(span =>
                (pair.Key, pair.Value.Source.Take(span.Start).Count(character => character == '\n') + 1)));

        private UIControlRename(UIBindingRoot display, UIControlReference slot, string newName,
            Dictionary<string, Edit> edits, string generated)
        {
            _display = display;
            _slot = slot;
            _newName = newName;
            _edits = edits;
            _generated = generated;
            _originalGenerated = File.ReadAllText(generated);
        }

        internal static UIControlRename Preview(UIBindingRoot display, UIControlReference slot, string newName)
        {
            if (EditorApplication.isCompiling)
                throw new InvalidOperationException("脚本正在编译，编译完成后再预览。");
            string path = UIReferenceCodeGenerator.ScriptPath(display);
            string generated = UIReferenceCodeGenerator.GeneratedPath(display);
            if (File.ReadAllText(generated) != UIReferenceCodeGenerator.Source(display, display.Controls))
                throw new InvalidOperationException("请先保存 Prefab 并等待引用生成完成。");
            UIReferenceCodeGenerator.Source(display, ReplaceSlot(display, slot, newName));

            var assembly = CompilationPipeline.GetAssemblies(AssembliesType.Editor)
                .FirstOrDefault(value => value.sourceFiles.Any(file => string.Equals(ProjectPath(file), path, StringComparison.OrdinalIgnoreCase)));
            if (assembly == null) throw new InvalidOperationException("找不到 View 所属的脚本程序集。");

            // Use the same C# parser Unity ships for script upgrades. Keep compiler assemblies out of the player.
            string compilerDirectory = Path.Combine(EditorApplication.applicationContentsPath, "Tools", "ScriptUpdater");
            var core = System.Reflection.Assembly.LoadFrom(Path.Combine(compilerDirectory, "Microsoft.CodeAnalysis.dll"));
            var csharp = System.Reflection.Assembly.LoadFrom(Path.Combine(compilerDirectory, "Microsoft.CodeAnalysis.CSharp.dll"));
            Type Core(string name) => core.GetType("Microsoft.CodeAnalysis." + name, true);
            Type CSharp(string name) => csharp.GetType("Microsoft.CodeAnalysis.CSharp." + name, true);
            var parseOptions = CSharp("CSharpParseOptions").GetProperty("Default").GetValue(null);
            parseOptions = CSharp("CSharpParseOptions").GetMethod("WithPreprocessorSymbols", new[] { typeof(IEnumerable<string>) })
                .Invoke(parseOptions, new object[] { assembly.defines });
            var parse = CSharp("CSharpSyntaxTree").GetMethod("ParseText",
                new[] { typeof(string), CSharp("CSharpParseOptions"), typeof(string), typeof(Encoding), typeof(CancellationToken) });
            var files = assembly.sourceFiles.Select(file => (Path: ProjectPath(file), Source: File.ReadAllText(file))).ToArray();
            Array trees = Array.CreateInstance(Core("SyntaxTree"), files.Length);
            for (int i = 0; i < files.Length; i++)
                trees.SetValue(parse.Invoke(null, new object[] { files[i].Source, parseOptions, files[i].Path, Encoding.UTF8,
                    CancellationToken.None }), i);

            var references = assembly.compiledAssemblyReferences.Concat(assembly.assemblyReferences.Select(value => value.outputPath))
                .Select(Path.GetFullPath).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            Type referenceType = Core("MetadataReference");
            MethodInfo fromFile = referenceType.GetMethod("CreateFromFile",
                new[] { typeof(string), Core("MetadataReferenceProperties"), Core("DocumentationProvider") });
            object defaultProperties = Activator.CreateInstance(Core("MetadataReferenceProperties"));
            Array metadata = Array.CreateInstance(referenceType, references.Length);
            for (int i = 0; i < references.Length; i++)
                metadata.SetValue(fromFile.Invoke(null, new[] { references[i], defaultProperties, null }), i);

            Type compilationType = CSharp("CSharpCompilation");
            object compilation = compilationType.GetMethods().Single(method => method.Name == "Create" && method.GetParameters().Length == 4)
                .Invoke(null, new object[] { assembly.name, trees, metadata, null });
            object type = Core("Compilation").GetMethod("GetTypeByMetadataName")
                .Invoke(compilation, new object[] { UIFactory.ResolveType(display.ViewTypeName).FullName });
            MethodInfo getMembers = Core("INamespaceOrTypeSymbol").GetMethod("GetMembers", new[] { typeof(string) });
            object[] members = ((IEnumerable)getMembers.Invoke(type, new object[] { slot.Name })).Cast<object>().ToArray();
            if (members.Length != 1 || ((IEnumerable)getMembers.Invoke(type, new object[] { newName })).Cast<object>().Any())
                throw new InvalidOperationException("旧引用无法唯一识别，或新名称与 View 中的现有成员冲突。");

            Type syntaxNode = Core("SyntaxNode");
            Type syntaxTree = Core("SyntaxTree");
            Type expression = CSharp("Syntax.ExpressionSyntax");
            Type identifier = CSharp("Syntax.IdentifierNameSyntax");
            MethodInfo getRoot = syntaxTree.GetMethod("GetRoot", new[] { typeof(CancellationToken) });
            MethodInfo descendants = syntaxNode.GetMethod("DescendantNodes",
                new[] { typeof(Func<,>).MakeGenericType(syntaxNode, typeof(bool)), typeof(bool) });
            MethodInfo getModel = compilationType.GetMethod("GetSemanticModel", new[] { syntaxTree, typeof(bool) });
            MethodInfo getInfo = csharp.GetType("Microsoft.CodeAnalysis.CSharp.CSharpExtensions", true)
                .GetMethod("GetSymbolInfo", new[] { Core("SemanticModel"), expression, typeof(CancellationToken) });
            MethodInfo lookup = Core("SemanticModel").GetMethod("LookupSymbols",
                new[] { typeof(int), Core("INamespaceOrTypeSymbol"), typeof(string), typeof(bool) });
            Type comparer = Core("SymbolEqualityComparer");
            object equality = comparer.GetField("Default").GetValue(null);
            MethodInfo equals = comparer.GetMethod("Equals", new[] { Core("ISymbol"), Core("ISymbol") });
            var edits = new Dictionary<string, Edit>(StringComparer.Ordinal);

            for (int i = 0; i < files.Length; i++)
            {
                var file = files[i];
                if (!file.Path.StartsWith("Assets/", StringComparison.Ordinal) ||
                    file.Path.EndsWith(".g.cs", StringComparison.Ordinal) ||
                    !file.Source.Contains(slot.Name)) continue;
                object tree = trees.GetValue(i);
                object root = getRoot.Invoke(tree, new object[] { CancellationToken.None });
                object model = getModel.Invoke(compilation, new[] { tree, false });
                var edit = new Edit { Source = file.Source };
                foreach (object node in (IEnumerable)descendants.Invoke(root, new object[] { null, false }))
                {
                    if (!identifier.IsInstanceOfType(node) || node.ToString().TrimStart('@') != slot.Name) continue;
                    object info = getInfo.Invoke(null, new[] { model, node, CancellationToken.None });
                    object symbol = Core("SymbolInfo").GetProperty("Symbol").GetValue(info);
                    if (!(bool)equals.Invoke(equality, new[] { members[0], symbol })) continue;
                    object span = syntaxNode.GetProperty("Span").GetValue(node);
                    int start = (int)span.GetType().GetProperty("Start").GetValue(span);
                    if (((IEnumerable)lookup.Invoke(model, new object[] { start, null, newName, false })).Cast<object>().Any())
                        throw new InvalidOperationException($"{file.Path} 中的 {slot.Name} → {newName} 会与现有符号冲突。");
                    edit.Spans.Add((start,
                        (int)span.GetType().GetProperty("Length").GetValue(span)));
                }
                if (edit.Spans.Count != 0) edits.Add(file.Path, edit);
            }
            return new UIControlRename(display, slot, newName, edits, generated);
        }

        internal void Apply()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (EditorApplication.isCompiling || _display == null || stage == null || stage.prefabContentsRoot != _display.gameObject ||
                !_display.Controls.Contains(_slot) ||
                !UIReferenceCodeGenerator.Scan(_display).Any(control => control.Target == _slot.Target && control.Name == _newName) ||
                File.ReadAllText(_generated) != _originalGenerated ||
                _edits.Any(pair => File.ReadAllText(pair.Key) != pair.Value.Source))
                throw new InvalidOperationException("Prefab 或脚本已变化，请重新预览。");

            var previous = _display.Controls.ToArray();
            var next = ReplaceSlot(_display, _slot, _newName);
            string code = UIReferenceCodeGenerator.Source(_display, next);
            var originals = _edits.Keys.Append(_generated).Append(stage.assetPath)
                .ToDictionary(path => path, File.ReadAllBytes);
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var pair in _edits)
                {
                    var source = new StringBuilder(pair.Value.Source);
                    foreach (var span in pair.Value.Spans.OrderByDescending(span => span.Start))
                        source.Remove(span.Start, span.Length).Insert(span.Start, _newName);
                    bool bom = originals[pair.Key].Take(3).SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf });
                    File.WriteAllText(pair.Key, source.ToString(), new UTF8Encoding(bom));
                }
                File.WriteAllText(_generated, code, new UTF8Encoding(false));
                _display.SetControls(next);
                EditorUtility.SetDirty(_display);
                PrefabUtility.SaveAsPrefabAsset(stage.prefabContentsRoot, stage.assetPath, out bool saved);
                if (!saved) throw new InvalidOperationException("Prefab 保存失败。");
            }
            catch
            {
                foreach (var pair in originals) File.WriteAllBytes(pair.Key, pair.Value);
                _display.SetControls(previous);
                EditorSceneManager.MarkSceneDirty(stage.scene);
                throw;
            }
            finally { AssetDatabase.StopAssetEditing(); }
            AssetDatabase.Refresh();
        }

        private static string ProjectPath(string path)
        {
            string relative = Path.IsPathRooted(path) ? FileUtil.GetProjectRelativePath(path) : path;
            return (string.IsNullOrEmpty(relative) ? path : relative).Replace('\\', '/');
        }

        private static UIControlReference[] ReplaceSlot(UIBindingRoot display, UIControlReference slot, string name) =>
            display.Controls.Select(control => ReferenceEquals(control, slot) ?
                new UIControlReference(name, control.Target) : control).ToArray();
    }
}
