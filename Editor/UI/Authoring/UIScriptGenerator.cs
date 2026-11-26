using EasyFramework.UI;

namespace EasyFramework.Editor.UI
{
    /// <summary>生成业务方可继续编辑的 View 和参数起始代码。</summary>
    internal static class UIScriptGenerator
    {
        internal static string Create(string name, string space, UIPageKind kind)
        {
            if (!UIBindingRoot.IsPageKind(kind))
            {
                string parent = kind == UIPageKind.Node ? "UINodeView" : "UIItem";
                return $"using EasyFramework.UI;\n\nnamespace {space}\n{{\n" +
                    $"    /// <summary>定义 {name} 的界面逻辑。</summary>\n" +
                    $"    public sealed partial class {name} : {parent} {{ }}\n}}\n";
            }
            string baseType = kind == UIPageKind.Popup ? $"UIPopup<{name}Args, {name}Result>" : $"UIView<{name}Args>";
            return $"using EasyFramework.UI;\n\nnamespace {space}\n{{\n" +
                $"    /// <summary>打开 {name} 时传入的页面参数。</summary>\n" +
                $"    public readonly struct {name}Args {{ }}\n\n" +
                (kind == UIPageKind.Popup ? $"    /// <summary>{name} 确认后返回的结果。</summary>\n" +
                    $"    public readonly struct {name}Result {{ }}\n\n" : "") +
                $"    /// <summary>处理 {name} 的界面逻辑。</summary>\n" +
                $"    public sealed partial class {name} : {baseType}\n    {{\n" +
                $"        protected override void Refresh({name}Args args) {{ }}\n    }}\n}}\n";
        }
    }
}
