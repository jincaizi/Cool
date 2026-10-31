using Hotfix.GameSystems.Skills.Runtime.Projectile;
using Hotfix.GameSystems.Skills.Runtime.SwordQi;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class RemoteProjectileApiTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
        }

        [Test]
        public void Fireball_ExplodeAt_Is_Idempotent_And_Moves_To_The_Point()
        {
            _go = new GameObject("FireballTest");
            var projectile = _go.AddComponent<FireballProjectile>();
            projectile.ExplodeAt(new Vector3(3f, 0f, 4f));
            Assert.AreEqual(new Vector3(3f, 0f, 4f), _go.transform.position);
            Assert.IsFalse(projectile.IsActive);
            Assert.DoesNotThrow(() => projectile.ExplodeAt(Vector3.zero));
        }
    }
}
