using UnityEngine;

namespace Hotfix.GameSystems.Skills.Runtime
{
    public interface IDashComponent
    {
        void StartDash(Vector3 direction, float distance, float duration);
    }
}
