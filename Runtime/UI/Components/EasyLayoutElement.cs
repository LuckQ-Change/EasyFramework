using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/UI/Layout Element"), RequireComponent(typeof(EasyUIElement))]
    public class EasyLayoutElement : LayoutElement, IEasyUIComponent { }
}
