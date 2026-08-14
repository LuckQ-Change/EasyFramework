using EasyFramework.UI;
using UnityEngine;

namespace EasyFramework.Samples
{
    public sealed class ReactiveLevelModel : MonoBehaviour
    {
        public ReactiveProperty<int> Level { get; } = new ReactiveProperty<int>(1);
        public ReactiveProperty<bool> CanEnter { get; } = new ReactiveProperty<bool>(true);

        public void NextLevel()
        {
            Level.Value++;
            CanEnter.Value = Level.Value % 5 != 0;
        }

        private void OnDestroy()
        {
            Level.Dispose();
            CanEnter.Dispose();
        }
    }
}
