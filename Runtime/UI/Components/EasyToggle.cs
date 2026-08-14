using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/UI/Toggle"), RequireComponent(typeof(EasyUIElement))]
    public class EasyToggle : Toggle, IEasyUIComponent { }
}
