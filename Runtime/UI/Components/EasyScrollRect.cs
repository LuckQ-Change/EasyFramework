using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/UI/Scroll Rect"), RequireComponent(typeof(EasyUIElement))]
    public class EasyScrollRect : ScrollRect, IEasyUIComponent { }
}
