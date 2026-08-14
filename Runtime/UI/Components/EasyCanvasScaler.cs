using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/UI/Canvas Scaler"), RequireComponent(typeof(EasyUIElement))]
    public class EasyCanvasScaler : CanvasScaler, IEasyUIComponent { }
}
