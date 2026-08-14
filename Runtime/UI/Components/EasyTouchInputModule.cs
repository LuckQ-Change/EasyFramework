using UnityEngine;
using UnityEngine.EventSystems;

#pragma warning disable 618
namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/Event System/Touch Input Module"), RequireComponent(typeof(EasyUIElement))]
    public class EasyTouchInputModule : TouchInputModule, IEasyUIComponent { }
}
#pragma warning restore 618
