using System;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Skills.Events;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class SwordQiSpawnedEventTests
    {
        [Test]
        public void test_sword_qi_spawned_event_emits_with_payload()
        {
            // Arrange
            SwordQiSpawnedEvent received = null;
            Action<SwordQiSpawnedEvent> handler = e => received = e;
            EventBus.Subscribe(handler);

            try
            {
                // Act
                EventBus.Emit(new SwordQiSpawnedEvent
                {
                    CasterId = 42,
                    SkillId = 10002,
                    Origin = new Vector3(1f, 2f, 3f),
                    Direction = Vector3.forward,
                    Speed = 18f,
                    Range = 12f
                });

                // Assert
                Assert.IsNotNull(received);
                Assert.AreEqual(42, received.CasterId);
                Assert.AreEqual(10002, received.SkillId);
                Assert.AreEqual(new Vector3(1f, 2f, 3f), received.Origin);
                Assert.AreEqual(12f, received.Range);
            }
            finally
            {
                EventBus.Unsubscribe(handler);
            }
        }
    }
}
