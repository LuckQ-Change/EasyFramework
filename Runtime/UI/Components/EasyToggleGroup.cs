using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/UI/Toggle Group"), RequireComponent(typeof(EasyUIElement))]
    public class EasyToggleGroup : ToggleGroup, IEasyUIComponent { }
}
