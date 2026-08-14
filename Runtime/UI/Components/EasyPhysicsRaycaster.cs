using UnityEngine;
using UnityEngine.EventSystems;

namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/Event System/Physics Raycaster"), RequireComponent(typeof(EasyUIElement))]
    public class EasyPhysicsRaycaster : PhysicsRaycaster, IEasyUIComponent { }
}
