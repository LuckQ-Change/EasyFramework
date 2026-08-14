using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/UI/Scrollbar"), RequireComponent(typeof(EasyUIElement))]
    public class EasyScrollbar : Scrollbar, IEasyUIComponent { }
}
