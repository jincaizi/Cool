using System;
using DG.Tweening;

namespace Hotfix.GameSystems.UI
{
    public enum Direction
    {
        Left,
        Right,
        Top,
        Bottom
    }

    [Serializable]
    public class UIAnimPreset
    {
        public float Duration = 0.3f;
        public Ease Ease = Ease.OutCubic;
        public float Delay;
        public bool Fade;
        public bool Scale;
        public bool Slide;
        public Direction SlideDir;
    }
}
