using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/UI/Outline"), RequireComponent(typeof(EasyUIElement))]
    public class EasyOutline : Outline, IEasyUIComponent { }
}
