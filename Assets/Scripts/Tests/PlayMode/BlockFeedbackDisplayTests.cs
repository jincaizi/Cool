using System.Collections;
using Hotfix.GameSystems.Nameplate;
using Hotfix.GameSystems.Skills;
using Hotfix.GameSystems.Sys3C.Core.Events;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameSys.PlayModeTests
{
    /// <summary>格挡飘字：生成 + 同实体 0.2s 去重。直接测 DisplayEventBridge，不依赖场景接线。</summary>
    public class BlockFeedbackDisplayTests
    {
        private GameObject _host;
        private FloatTextRenderer _renderer;
        private FloatTextSettings _blockSettings;
        private DisplayEventBridge _bridge;

        [SetUp]
        public void SetUp()
        {
            FloatTextRenderer.EnableVFX = true;
            _host = new GameObject("BlockDisplayTest");
            _host.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            _renderer = new FloatTextRenderer(_host.transform);
            _blockSettings = ScriptableObject.CreateInstance<FloatTextSettings>();
            _blockSettings.Type = FloatTextType.Block;
            _blockSettings.FontSize = 28f;
            _blockSettings.Duration = 0.6f;
            _bridge = new DisplayEventBridge(
                _renderer, _blockSettings, _blockSettings, _blockSettings, _blockSettings);
            _bridge.Enable();
        }

        [TearDown]
        public void TearDown()
        {
            _bridge.Disable();
            _renderer.Cleanup();
            Object.DestroyImmediate(_blockSettings);
            Object.DestroyImmediate(_host);
        }

        [UnityTest]
        public IEnumerator test_block_feedback_spawns_block_text()
        {
            // Act
            EventBus.Emit(new BlockSucceededEvent(42, Vector3.zero, Vector3.up * 1.5f));
            yield return null;

            // Assert
            var texts = _host.GetComponentsInChildren<TextMeshProUGUI>();
            Assert.AreEqual(1, texts.Length, "格挡成功应生成一个飘字");
            Assert.AreEqual("格挡", texts[0].text);
        }

        [UnityTest]
        public IEnumerator test_block_feedback_dedupes_within_window()
        {
            // Act：同一实体同帧两段命中
            EventBus.Emit(new BlockSucceededEvent(42, Vector3.zero, Vector3.up * 1.5f));
            EventBus.Emit(new BlockSucceededEvent(42, Vector3.zero, Vector3.up * 1.5f));
            yield return null;

            // Assert
            var texts = _host.GetComponentsInChildren<TextMeshProUGUI>();
            Assert.AreEqual(1, texts.Length, "0.2s 内同一实体只显示一次「格挡」");
        }
    }
}
