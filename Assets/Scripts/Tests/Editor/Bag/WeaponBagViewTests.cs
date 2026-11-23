using Cool.Protocol;
using Cool.SyncCore;
using Hotfix.GameSystems.Bag.Data;
using Hotfix.GameSystems.Bag.Runtime;
using Hotfix.GameSystems.Bag.UI;
using NUnit.Framework;

namespace GameSys.EditorTests
{
    public class WeaponBagViewTests
    {
        private const long DefaultId = 42;
        private const long IceId = 43;
        private const long FireId = 44;

        [SetUp]
        public void SetUp() => BagManager.Instance.Clear();

        [TearDown]
        public void TearDown() => BagManager.Instance.Clear();

        [Test]
        public void test_rows_preserve_login_order_and_mark_authoritative_equipped()
        {
            var data = MakeBag();

            var rows = WeaponBagView.BuildRows(data, DefaultId, 0);

            Assert.AreEqual(3, rows.Length);
            Assert.AreEqual(DefaultId, rows[0].Item.InstanceId);
            Assert.AreEqual(IceId, rows[1].Item.InstanceId);
            Assert.AreEqual(FireId, rows[2].Item.InstanceId);
            Assert.IsTrue(rows[0].Equipped);
            StringAssert.Contains("[已装备]", rows[0].Label);
            Assert.IsFalse(rows[0].Interactable);
            Assert.IsTrue(rows[1].Interactable);
        }

        [Test]
        public void test_pending_row_is_marked_and_locks_the_other_rows()
        {
            var rows = WeaponBagView.BuildRows(MakeBag(), DefaultId, IceId);

            Assert.IsTrue(rows[0].Equipped, "equipped mark stays on the authoritative item while pending");
            Assert.IsFalse(rows[0].Interactable);
            Assert.IsTrue(rows[1].Pending);
            StringAssert.Contains("[换装中]", rows[1].Label);
            Assert.IsFalse(rows[1].Interactable);
            Assert.IsFalse(rows[2].Interactable, "a single in-flight request locks every row");
        }

        [Test]
        public void test_request_sends_once_and_keeps_equipped_mark_until_confirm()
        {
            var harness = new Harness();
            harness.Controller.ApplyAuthoritative(DefaultId, 0, 1f);

            Assert.IsTrue(harness.Controller.TryRequest(IceId, 10f));

            Assert.AreEqual(1, harness.RequestCount);
            Assert.AreEqual(IceId, harness.Controller.PendingInstanceId);
            Assert.AreEqual(DefaultId, harness.Controller.EquippedInstanceId);
            Assert.AreEqual(WeaponBagController.PendingStatus, harness.Controller.DisplayStatus);

            var rows = WeaponBagView.BuildRows(MakeBag(), harness.Controller.EquippedInstanceId,
                harness.Controller.PendingInstanceId);
            Assert.IsTrue(rows[0].Equipped);
            Assert.IsTrue(rows[1].Pending);
        }

        [Test]
        public void test_clicking_equipped_item_sends_nothing()
        {
            var harness = new Harness();
            harness.Controller.ApplyAuthoritative(DefaultId, 0, 1f);

            Assert.IsFalse(harness.Controller.TryRequest(DefaultId, 1f));

            Assert.AreEqual(0, harness.RequestCount);
        }

        [Test]
        public void test_rapid_clicks_send_only_one_request()
        {
            var harness = new Harness();
            harness.Controller.ApplyAuthoritative(DefaultId, 0, 1f);

            Assert.IsTrue(harness.Controller.TryRequest(IceId, 1f));
            Assert.IsFalse(harness.Controller.TryRequest(FireId, 1.1f));

            Assert.AreEqual(1, harness.RequestCount);
            Assert.AreEqual(IceId, harness.Controller.PendingInstanceId);
        }

        [Test]
        public void test_dead_click_shows_reason_without_request()
        {
            var harness = new Harness { Dead = true };

            Assert.IsFalse(harness.Controller.TryRequest(IceId, 1f));

            Assert.AreEqual(0, harness.RequestCount);
            Assert.AreEqual(WeaponBagController.DeadStatus, harness.Controller.DisplayStatus);

            harness.Dead = false;
            harness.Controller.Tick(2f);
            Assert.AreEqual(string.Empty, harness.Controller.DisplayStatus,
                "the death prompt clears once the player can act again");
        }

        [Test]
        public void test_skill_click_shows_reason_without_request()
        {
            var harness = new Harness { SkillActive = true };

            Assert.IsFalse(harness.Controller.TryRequest(IceId, 1f));

            Assert.AreEqual(0, harness.RequestCount);
            Assert.AreEqual(WeaponBagController.SkillStatus, harness.Controller.DisplayStatus);

            harness.SkillActive = false;
            harness.Controller.Tick(2f);
            Assert.AreEqual(string.Empty, harness.Controller.DisplayStatus,
                "the skill prompt clears once the action ends");
        }

        [Test]
        public void test_accepted_confirm_clears_pending_and_applies_appearance_once()
        {
            var harness = new Harness();
            harness.Controller.ApplyAuthoritative(DefaultId, 0, 1f);
            int appearanceCalls = harness.AppearanceCalls;
            harness.Controller.TryRequest(IceId, 10f);

            var outcome = harness.Controller.ApplyConfirm(Confirm(1, true), IceId, 1, 1.2f);

            Assert.AreEqual(WeaponEquipOutcome.Accepted, outcome);
            Assert.IsFalse(harness.Controller.HasPending);
            Assert.AreEqual(IceId, harness.Controller.EquippedInstanceId);
            Assert.AreEqual(string.Empty, harness.Controller.DisplayStatus);
            Assert.AreEqual(appearanceCalls + 1, harness.AppearanceCalls);
            Assert.AreEqual(1, harness.LastElement);
            Assert.AreEqual(1.2f, harness.LastAttackSpeed);
        }

        [Test]
        public void test_stale_confirm_does_not_clear_pending_or_override_status()
        {
            var harness = new Harness();
            harness.Controller.ApplyAuthoritative(DefaultId, 0, 1f);
            harness.Controller.TryRequest(IceId, 10f);

            var outcome = harness.Controller.ApplyConfirm(Confirm(99, false, "old"), DefaultId, 0, 1f);

            Assert.AreEqual(WeaponEquipOutcome.Stale, outcome);
            Assert.IsTrue(harness.Controller.HasPending);
            Assert.AreEqual(IceId, harness.Controller.PendingInstanceId);
            Assert.AreEqual(WeaponBagController.PendingStatus, harness.Controller.DisplayStatus);
        }

        [Test]
        public void test_stale_confirm_can_advance_authoritative_equipment()
        {
            var harness = new Harness();
            harness.Controller.ApplyAuthoritative(DefaultId, 0, 1f);
            harness.Controller.TryRequest(IceId, 10f);

            harness.Controller.ApplyConfirm(Confirm(99, true), FireId, 2, 1f);

            Assert.AreEqual(FireId, harness.Controller.EquippedInstanceId,
                "a late result still advances the authoritative summary");
            Assert.IsTrue(harness.Controller.HasPending);
            Assert.AreEqual(IceId, harness.Controller.PendingInstanceId);
            Assert.AreEqual(WeaponBagController.PendingStatus, harness.Controller.DisplayStatus,
                "a late result must not overwrite the in-flight request prompt");
        }

        [Test]
        public void test_rejected_confirm_restores_authoritative_mark_and_reason()
        {
            var harness = new Harness();
            harness.Controller.ApplyAuthoritative(IceId, 1, 1f);
            harness.Controller.TryRequest(FireId, 10f);

            var outcome = harness.Controller.ApplyConfirm(Confirm(1, false, "not-owned"),
                IceId, 1, 1f);

            Assert.AreEqual(WeaponEquipOutcome.Rejected, outcome);
            Assert.IsFalse(harness.Controller.HasPending);
            Assert.AreEqual(IceId, harness.Controller.EquippedInstanceId);
            StringAssert.Contains("换装失败", harness.Controller.DisplayStatus);
            StringAssert.Contains("武器不可用", harness.Controller.DisplayStatus);
        }

        [Test]
        public void test_unknown_reject_reason_uses_generic_message()
        {
            var harness = new Harness();
            harness.Controller.ApplyAuthoritative(IceId, 1, 1f);
            harness.Controller.TryRequest(FireId, 10f);

            harness.Controller.ApplyConfirm(Confirm(1, false, "unrecognized-reason"), IceId, 1, 1f);

            Assert.AreEqual("换装失败", harness.Controller.DisplayStatus);
        }

        [Test]
        public void test_timeout_allows_retry_and_late_old_seq_does_not_unlock_new_request()
        {
            var harness = new Harness();
            harness.Controller.ApplyAuthoritative(DefaultId, 0, 1f);
            Assert.IsTrue(harness.Controller.TryRequest(IceId, 10f));

            harness.Controller.Tick(10f + WeaponBagController.ConfirmTimeoutSeconds + 0.01f);

            Assert.IsFalse(harness.Controller.HasPending);
            Assert.AreEqual(WeaponBagController.TimeoutStatus, harness.Controller.DisplayStatus);
            Assert.IsTrue(harness.Controller.TryRequest(FireId, 20f), "timeout must restore clickability");

            var outcome = harness.Controller.ApplyConfirm(Confirm(1, false, "late"), DefaultId, 0, 1f);

            Assert.AreEqual(WeaponEquipOutcome.Stale, outcome);
            Assert.IsTrue(harness.Controller.HasPending);
            Assert.AreEqual(FireId, harness.Controller.PendingInstanceId);
            Assert.AreEqual(WeaponBagController.PendingStatus, harness.Controller.DisplayStatus);
        }

        [Test]
        public void test_confirm_during_skill_refreshes_mark_and_applies_latest_after_skill()
        {
            var harness = new Harness();
            harness.Controller.ApplyAuthoritative(DefaultId, 0, 1f);
            harness.Controller.TryRequest(IceId, 10f);
            harness.SkillActive = true;
            int appearanceCalls = harness.AppearanceCalls;

            harness.Controller.ApplyConfirm(Confirm(1, true), IceId, 1, 1f);

            Assert.AreEqual(IceId, harness.Controller.EquippedInstanceId,
                "the list mark follows the authoritative summary immediately");
            Assert.IsFalse(harness.Controller.HasPending);
            Assert.AreEqual(appearanceCalls, harness.AppearanceCalls,
                "appearance waits for the action to end");

            harness.Controller.ApplyConfirm(Confirm(99, true), FireId, 2, 1f);
            Assert.AreEqual(FireId, harness.Controller.EquippedInstanceId);
            Assert.AreEqual(appearanceCalls, harness.AppearanceCalls);

            harness.SkillActive = false;
            harness.Controller.Tick(11f);

            Assert.AreEqual(appearanceCalls + 1, harness.AppearanceCalls,
                "only the latest appearance is applied once");
            Assert.AreEqual(2, harness.LastElement);
        }

        [Test]
        public void test_missing_appearance_falls_back_and_clears_after_valid_model()
        {
            var harness = new Harness { AppearanceResult = WeaponAppearanceResult.Missing };
            harness.Controller.ApplyAuthoritative(IceId, 1, 1f);

            Assert.AreEqual(WeaponBagController.MissingAppearanceStatus, harness.Controller.DisplayStatus);

            harness.AppearanceResult = WeaponAppearanceResult.Applied;
            harness.Controller.ApplyAuthoritative(IceId, 2, 1f);

            Assert.AreEqual(string.Empty, harness.Controller.DisplayStatus);
        }

        [Test]
        public void test_reapply_retries_until_the_character_is_available()
        {
            var harness = new Harness { AppearanceResult = WeaponAppearanceResult.Unavailable };
            harness.Controller.ApplyAuthoritative(IceId, 1, 1f);
            Assert.AreEqual(string.Empty, harness.Controller.DisplayStatus);
            Assert.AreEqual(1, harness.AppearanceCalls);

            harness.Controller.Tick(2f);
            Assert.AreEqual(2, harness.AppearanceCalls, "unavailable character retries next tick");

            harness.AppearanceResult = WeaponAppearanceResult.Applied;
            harness.Controller.Tick(3f);

            Assert.AreEqual(3, harness.AppearanceCalls);
            Assert.AreEqual(1, harness.LastElement);
            Assert.AreEqual(string.Empty, harness.Controller.DisplayStatus);
        }

        [Test]
        public void test_forced_authoritative_replays_same_appearance_on_rebuilt_character()
        {
            var harness = new Harness();
            harness.Controller.ApplyAuthoritative(IceId, 1, 1f);
            int appearanceCalls = harness.AppearanceCalls;

            harness.Controller.ApplyAuthoritative(IceId, 1, 1f, force: true);

            Assert.AreEqual(appearanceCalls + 1, harness.AppearanceCalls,
                "a rebuilt character must receive the same sword view even when the element is unchanged");
        }

        [Test]
        public void test_reset_clears_pending_feedback_and_equipment()
        {
            var harness = new Harness();
            harness.Controller.ApplyAuthoritative(IceId, 1, 1f);
            harness.Controller.TryRequest(FireId, 1f);
            int revision = harness.Controller.Revision;

            harness.Controller.Reset();

            Assert.IsFalse(harness.Controller.HasPending);
            Assert.AreEqual(0L, harness.Controller.EquippedInstanceId);
            Assert.AreEqual(0L, harness.Controller.PendingInstanceId);
            Assert.AreEqual(string.Empty, harness.Controller.DisplayStatus);
            Assert.Greater(harness.Controller.Revision, revision);
        }

        [Test]
        public void test_duplicate_result_changes_presentation_once()
        {
            var harness = new Harness();
            harness.Controller.ApplyAuthoritative(DefaultId, 0, 1f);
            harness.Controller.TryRequest(IceId, 1f);
            harness.Controller.ApplyConfirm(Confirm(1, true), IceId, 1, 1f);
            int revision = harness.Controller.Revision;
            int appearanceCalls = harness.AppearanceCalls;

            harness.Controller.ApplyConfirm(Confirm(1, true), IceId, 1, 1f);

            Assert.AreEqual(revision, harness.Controller.Revision,
                "the same result must not bump the presentation revision twice");
            Assert.AreEqual(appearanceCalls, harness.AppearanceCalls,
                "an unchanged appearance is not re-applied");
        }

        private static BagData MakeBag()
        {
            BagManager.Instance.ApplyServerWeapons(new[]
            {
                new WeaponInstanceSummary
                {
                    InstanceId = DefaultId,
                    TemplateId = ElementalCatalogDefaults.DefaultSwordTemplateId,
                    ElementId = 0
                },
                new WeaponInstanceSummary
                {
                    InstanceId = IceId,
                    TemplateId = ElementalCatalogDefaults.IceSwordTemplateId,
                    ElementId = 1
                },
                new WeaponInstanceSummary
                {
                    InstanceId = FireId,
                    TemplateId = ElementalCatalogDefaults.FireSwordTemplateId,
                    ElementId = 2
                }
            });
            return BagManager.Instance.Data;
        }

        private static EquipWeaponConfirm Confirm(uint seq, bool accepted, string reason = "")
            => new EquipWeaponConfirm
            {
                ClientEquipSeq = seq,
                Accepted = accepted,
                RejectReason = reason
            };

        private sealed class Harness
        {
            public bool Dead;
            public bool SkillActive;
            public int RequestCount;
            public uint NextSeq = 1;
            public WeaponAppearanceResult AppearanceResult = WeaponAppearanceResult.Applied;
            public int AppearanceCalls;
            public int LastElement = -1;
            public float LastAttackSpeed;

            public readonly WeaponBagController Controller;

            public Harness()
            {
                Controller = new WeaponBagController(
                    () => Dead,
                    () => SkillActive,
                    id =>
                    {
                        RequestCount++;
                        return NextSeq++;
                    },
                    (elementId, attackSpeed) =>
                    {
                        AppearanceCalls++;
                        LastElement = elementId;
                        LastAttackSpeed = attackSpeed;
                        return AppearanceResult;
                    });
            }
        }
    }
}
