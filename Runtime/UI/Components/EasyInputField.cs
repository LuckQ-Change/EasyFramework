using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/UI/Input Field"), RequireComponent(typeof(EasyUIElement))]
    public class EasyInputField : InputField, IEasyUIComponent { }
}
