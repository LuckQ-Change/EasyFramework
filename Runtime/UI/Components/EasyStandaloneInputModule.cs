using UnityEngine;
using UnityEngine.EventSystems;

namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/Event System/Standalone Input Module"), RequireComponent(typeof(EasyUIElement))]
    public class EasyStandaloneInputModule : StandaloneInputModule, IEasyUIComponent { }
}
