using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/UI/Mask"), RequireComponent(typeof(EasyUIElement))]
    public class EasyMask : Mask, IEasyUIComponent { }
}
