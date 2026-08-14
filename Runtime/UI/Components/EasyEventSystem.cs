using UnityEngine;
using UnityEngine.EventSystems;

namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/Event System/Event System"), RequireComponent(typeof(EasyUIElement))]
    public class EasyEventSystem : EventSystem, IEasyUIComponent { }
}
