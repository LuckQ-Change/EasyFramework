using UnityEngine;
using UnityEngine.EventSystems;

namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/Event System/Physics 2D Raycaster"), RequireComponent(typeof(EasyUIElement))]
    public class EasyPhysics2DRaycaster : Physics2DRaycaster, IEasyUIComponent { }
}
