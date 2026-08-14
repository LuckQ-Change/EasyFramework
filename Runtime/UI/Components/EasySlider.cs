using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/UI/Slider"), RequireComponent(typeof(EasyUIElement))]
    public class EasySlider : Slider, IEasyUIComponent { }
}
