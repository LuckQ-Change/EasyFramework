using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/UI/Raw Image"), RequireComponent(typeof(EasyUIElement))]
    public class EasyRawImage : RawImage, IEasyUIComponent { }
}
