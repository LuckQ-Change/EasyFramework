using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/UI/Text"), RequireComponent(typeof(EasyUIElement))]
    public class EasyText : Text, IEasyUIComponent { }
}
