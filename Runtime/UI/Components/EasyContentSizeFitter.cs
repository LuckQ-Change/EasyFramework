using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/UI/Content Size Fitter"), RequireComponent(typeof(EasyUIElement))]
    public class EasyContentSizeFitter : ContentSizeFitter, IEasyUIComponent { }
}
