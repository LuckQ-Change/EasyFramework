using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/UI/Shadow"), RequireComponent(typeof(EasyUIElement))]
    public class EasyShadow : Shadow, IEasyUIComponent { }
}
