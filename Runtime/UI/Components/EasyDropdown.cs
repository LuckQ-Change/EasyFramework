using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/UI/Dropdown"), RequireComponent(typeof(EasyUIElement))]
    public class EasyDropdown : Dropdown, IEasyUIComponent { }
}
