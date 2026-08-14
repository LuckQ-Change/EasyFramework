using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/UI/Aspect Ratio Fitter"), RequireComponent(typeof(EasyUIElement))]
    public class EasyAspectRatioFitter : AspectRatioFitter, IEasyUIComponent { }
}
