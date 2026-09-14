using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EasyFramework.UI
{
    [AddComponentMenu("EasyFramework/UI/Image")]
    public class EasyImage : Image, IEasyUIComponent
    {
        [SerializeField] private List<Sprite> _sprites = new List<Sprite>();
        [SerializeField] private int _spriteIndex = -1;
        [SerializeField] private bool _applySpriteIndexOnEnable = true;

        public IReadOnlyList<Sprite> Sprites => _sprites;
        public int SpriteIndex => _spriteIndex;
        public int SpriteCount => _sprites.Count;

        protected override void OnEnable()
        {
            base.OnEnable();
            if (_applySpriteIndexOnEnable) ApplySpriteIndex();
        }

        public bool SetSpriteIndex(int index)
        {
            if (index == -1)
            {
                _spriteIndex = -1;
                return true;
            }
            if (index < 0 || index >= _sprites.Count) return false;
            _spriteIndex = index;
            sprite = _sprites[index];
            return true;
        }

        public bool SetSpriteIndexLooped(int index)
        {
            if (_sprites.Count == 0) return false;
            int wrapped = ((index % _sprites.Count) + _sprites.Count) % _sprites.Count;
            return SetSpriteIndex(wrapped);
        }

        public bool ApplySpriteIndex() => SetSpriteIndex(_spriteIndex);

        public void SetSprites(IEnumerable<Sprite> sprites, int selectedIndex = -1)
        {
            _sprites.Clear();
            if (sprites != null) _sprites.AddRange(sprites);
            if (_sprites.Count == 0)
            {
                _spriteIndex = -1;
                return;
            }
            SetSpriteIndex(selectedIndex < 0 ? 0 : Mathf.Clamp(selectedIndex, 0, _sprites.Count - 1));
        }

        public bool NextSprite() => SetSpriteIndexLooped(_spriteIndex + 1);
        public bool PreviousSprite() => SetSpriteIndexLooped(_spriteIndex - 1);
    }
}
