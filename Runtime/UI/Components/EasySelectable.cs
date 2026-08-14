using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/UI/Selectable"), RequireComponent(typeof(EasyUIElement))]
    public class EasySelectable : Selectable, IEasyUIComponent { }
}
