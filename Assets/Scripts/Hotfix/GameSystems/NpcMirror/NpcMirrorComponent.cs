using UnityEngine;

namespace Hotfix.GameSystems.NpcMirror
{
    public class NpcMirrorComponent
    {
        public long NpcId { get; }
        private Vector3 _targetPosition;
        private Quaternion _targetRotation;
        private NpcAnimationState _targetAnimState;
        private float _lerpSpeed = 10f;

        public GameObject GameObject { get; private set; }
        public Transform Transform { get; private set; }
        private NpcAnimationController _animController;

        public NpcMirrorComponent(long npcId, Vector3 position, Quaternion rotation)
        {
            NpcId = npcId;
            _targetPosition = position;
            _targetRotation = rotation;
        }

        public void SetGameObject(GameObject go)
        {
            GameObject = go;
            Transform = go.transform;
            Transform.position = _targetPosition;
            Transform.rotation = _targetRotation;
            _animController = new NpcAnimationController(go.GetComponent<Animator>());
        }

        public void SetPosition(Vector3 pos)
        {
            _targetPosition = pos;
        }

        public void SetRotation(Quaternion rot)
        {
            _targetRotation = rot;
        }

        public void SetAnimationState(NpcAnimationState state)
        {
            _targetAnimState = state;
            _animController?.SetState(state);
        }

        public void Update(float deltaTime)
        {
            if (Transform == null) return;

            Transform.position = Vector3.Lerp(Transform.position, _targetPosition, _lerpSpeed * deltaTime);
            Transform.rotation = Quaternion.Slerp(Transform.rotation, _targetRotation, _lerpSpeed * deltaTime);
        }
    }
}
