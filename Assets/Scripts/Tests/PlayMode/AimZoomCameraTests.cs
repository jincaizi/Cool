using System.Collections;
using Hotfix.GameSystems.Skills.Definition;
using Hotfix.GameSystems.SyncLayer;
using Hotfix.GameSystems.Sys3C;
using Hotfix.GameSystems.Sys3C.Camera;
using Hotfix.GameSystems.Sys3C.Input;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace KcpNet.PlayModeTests
{
    /// <summary>
    /// 火球瞄准拉远的真实生命周期：验证 Sys3CEntry 接线与恢复路径。
    /// </summary>
    public class AimZoomCameraTests
    {
        private NetworkMode _previousMode;
        private GameObject _cameraGo;
        private GameObject _playerGo;
        private Sys3CEntry _entry;
        private ThirdPersonCameraController _camera;
        private InputManager _input;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _previousMode = NetworkModeService.Load();
            NetworkModeService.Save(NetworkMode.SinglePlayer);

            _cameraGo = new GameObject("AimZoomCameraTests_Camera");
            _camera = _cameraGo.AddComponent<ThirdPersonCameraController>();

            var prefab = Resources.Load<GameObject>("PlayerEntryForTest");
            Assert.IsNotNull(prefab,
                "测试用玩家 Prefab 缺失：Assets/Resources/PlayerEntryForTest.prefab");
            _playerGo = Object.Instantiate(prefab);
            _entry = _playerGo.GetComponent<Sys3CEntry>();

            yield return null; // 等 Start：相机 Target / InputManager / 技能注册
            yield return null;

            _input = _playerGo.GetComponent<InputManager>();
            Assert.IsNotNull(_input, "玩家必须有 InputManager");
        }

        [TearDown]
        public void TearDown()
        {
            NetworkModeService.Save(_previousMode);
            if (_playerGo != null) Object.DestroyImmediate(_playerGo);
            if (_cameraGo != null) Object.DestroyImmediate(_cameraGo);
        }

        private IEnumerator WaitForSubState(SkillSubState state)
        {
            float deadline = Time.realtimeSinceStartup + 2f;
            while (_entry.SkillCoordinator.CurrentSubState != state
                   && Time.realtimeSinceStartup < deadline)
                yield return null;
        }

        private void PressFireball()
        {
            _input.SetSkillFireballPressedFromUI(true);
            _input.SetSkillFireballHeldFromUI(true);
            _input.SetSkillFireballAimFromUI(true, new Vector2(0f, 1f), false);
        }

        private void ReleaseFireball()
        {
            _input.SetSkillFireballHeldFromUI(false);
            _input.SetSkillFireballAimFromUI(false, Vector2.zero, false);
        }

        /// <summary>PlayMode 帧率远高于 60fps，按真实时间等待距离收敛而不是数帧。</summary>
        private IEnumerator WaitForDistanceAbove(float target, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (_camera.CurrentDistance <= target
                   && Time.realtimeSinceStartup < deadline)
                yield return null;
        }

        private IEnumerator WaitForDistanceBelow(float target, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (_camera.CurrentDistance >= target
                   && Time.realtimeSinceStartup < deadline)
                yield return null;
        }

        [UnityTest]
        public IEnumerator test_fireball_aim_zooms_camera_out()
        {
            // Arrange
            PressFireball();
            yield return WaitForSubState(SkillSubState.Charging);
            Assert.AreEqual(SkillSubState.Charging, _entry.SkillCoordinator.CurrentSubState,
                "火球应进入 Charging");

            // Act：等缓动推进
            yield return WaitForDistanceAbove(10f, 2f);

            // Assert
            Assert.IsTrue(_camera.AimZoomActive, "瞄准期间应开启拉远");
            Assert.Greater(_camera.CurrentDistance, 8.5f, "瞄准期间相机距离应大于普通距离");
        }

        [UnityTest]
        public IEnumerator test_fireball_release_restores_camera_distance()
        {
            // Arrange
            PressFireball();
            yield return WaitForSubState(SkillSubState.Charging);
            yield return WaitForDistanceAbove(10f, 2f);
            Assert.Greater(_camera.CurrentDistance, 8.5f);

            // Act
            ReleaseFireball();
            yield return WaitForDistanceBelow(8.1f, 2f);

            // Assert
            Assert.IsFalse(_camera.AimZoomActive, "松手后应关闭拉远");
            Assert.AreEqual(8f, _camera.CurrentDistance, 0.2f, "松手后应回到普通距离");
        }

        [UnityTest]
        public IEnumerator test_death_restores_camera_distance()
        {
            // Arrange
            PressFireball();
            yield return WaitForSubState(SkillSubState.Charging);
            yield return WaitForDistanceAbove(10f, 2f);
            Assert.Greater(_camera.CurrentDistance, 8.5f);

            // Act：致死伤害走 ApplyServerDamage（单机模式本地判死）
            _entry.ApplyServerDamage(150f, Vector3.back);
            yield return WaitForDistanceBelow(8.1f, 2f);

            // Assert
            Assert.IsTrue(_entry.IsDead, "150 伤害应致死");
            Assert.IsFalse(_camera.AimZoomActive, "死亡后必须关闭拉远");
            Assert.AreEqual(8f, _camera.CurrentDistance, 0.2f, "死亡后应回到普通距离");
        }
    }
}
