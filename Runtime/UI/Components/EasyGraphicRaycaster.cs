using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/UI/Graphic Raycaster"), RequireComponent(typeof(EasyUIElement))]
    public class EasyGraphicRaycaster : GraphicRaycaster, IEasyUIComponent { }
}
