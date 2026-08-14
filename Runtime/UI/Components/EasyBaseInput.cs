using UnityEngine;
using UnityEngine.EventSystems;

namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/Event System/Base Input"), RequireComponent(typeof(EasyUIElement))]
    public class EasyBaseInput : BaseInput, IEasyUIComponent { }
}
