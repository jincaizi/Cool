using Hotfix.GameSystems.SyncLayer;
using NUnit.Framework;
using UnityEngine;

namespace GameSys.EditorTests
{
    public class NetworkModeServiceTests
    {
        private NetworkMode _previousMode;
        private string _previousHost;
        private int _previousPort;
        private string _previousUser;

        [SetUp]
        public void SetUp()
        {
            _previousMode = NetworkModeService.Load();
            _previousHost = NetworkModeService.LoadHost();
            _previousPort = NetworkModeService.LoadPort();
            _previousUser = NetworkModeService.LoadUsername();
        }

        [TearDown]
        public void TearDown()
        {
            NetworkModeService.Save(_previousMode);
            NetworkModeService.SaveConnection(
                _previousHost, _previousPort.ToString(), _previousUser);
        }

        [Test]
        public void test_default_mode_is_online_without_save()
        {
            PlayerPrefs.DeleteKey("net.mode");
            Assert.AreEqual(NetworkMode.Online, NetworkModeService.Load());
        }

        [Test]
        public void test_saved_single_player_wins()
        {
            NetworkModeService.Save(NetworkMode.SinglePlayer);
            Assert.AreEqual(NetworkMode.SinglePlayer, NetworkModeService.Load());
        }

        [Test]
        public void test_username_is_generated_once_and_persisted()
        {
            PlayerPrefs.DeleteKey("net.username");

            string first = NetworkModeService.LoadUsername();
            string second = NetworkModeService.LoadUsername();

            Assert.AreEqual(first, second);
            Assert.AreEqual(4, first.Length);
            Assert.AreNotEqual("Player", first);
            Assert.AreEqual(first, PlayerPrefs.GetString("net.username"));
        }

        [Test]
        public void test_empty_connection_username_falls_back_to_saved_name()
        {
            PlayerPrefs.DeleteKey("net.username");
            string generated = NetworkModeService.LoadUsername();

            NetworkModeService.SaveConnection("127.0.0.1", "8000", "");

            Assert.AreEqual(generated, NetworkModeService.LoadUsername());
        }
    }
}
