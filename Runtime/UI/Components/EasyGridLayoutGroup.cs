using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/UI/Grid Layout Group"), RequireComponent(typeof(EasyUIElement))]
    public class EasyGridLayoutGroup : GridLayoutGroup, IEasyUIComponent { }
}
