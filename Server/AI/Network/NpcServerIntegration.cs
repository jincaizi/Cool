using System;
using KcpServer.AI.Core;

namespace KcpServer.AI.Network
{
    /// <summary>
    /// Integration point between AiManager and Server3C.
    /// This would be used to hook NPC AI into the existing server infrastructure.
    /// </summary>
    public class NpcServerIntegration
    {
        private readonly AiManager _aiManager;
        private readonly NpcSyncBroadcaster _broadcaster;

        public NpcServerIntegration(AiManager aiManager, NpcSyncBroadcaster broadcaster)
        {
            _aiManager = aiManager;
            _broadcaster = broadcaster;

            _aiManager.OnAiStateChanged += OnAiStateChanged;
        }

        private void OnAiStateChanged(long npcId, Vector3 pos, Quaternion rot, NpcAnimationState animState)
        {
            _broadcaster.QueueSync(npcId, pos, rot, animState);
        }

        public void Start()
        {
            _aiManager.Start();
        }

        public void Stop()
        {
            _aiManager.Stop();
        }

        public AiManager AiManager => _aiManager;
    }
}
