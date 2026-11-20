using EasyFramework.UI;

namespace EasyFramework.Editor.UI
{
    internal static class UIScriptGenerator
    {
        internal static string Create(string name, string space, UIPageKind kind)
        {
            if (!UIBindingRoot.IsPageKind(kind))
            {
                string parent = kind == UIPageKind.Node ? "UINodeView" : "UIItem";
                return $"using EasyFramework.UI;\n\nnamespace {space}\n{{\n    public sealed partial class {name} : {parent} {{ }}\n}}\n";
            }
            string baseType = kind == UIPageKind.Popup ? $"UIPopup<{name}Args, {name}Result>" : $"UIView<{name}Args>";
            return $"using EasyFramework.UI;\n\nnamespace {space}\n{{\n" +
                $"    public readonly struct {name}Args {{ }}\n\n" +
                (kind == UIPageKind.Popup ? $"    public readonly struct {name}Result {{ }}\n\n" : "") +
                $"    public sealed partial class {name} : {baseType}\n    {{\n" +
                $"        protected override void Refresh({name}Args args) {{ }}\n    }}\n}}\n";
        }
    }
}
