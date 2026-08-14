using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/UI/Button"), RequireComponent(typeof(EasyUIElement))]
    public class EasyButton : Button, IEasyUIComponent { }
}
