using UnityEngine;
using UnityEngine.EventSystems;

namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/Event System/Event Trigger"), RequireComponent(typeof(EasyUIElement))]
    public class EasyEventTrigger : EventTrigger, IEasyUIComponent { }
}
