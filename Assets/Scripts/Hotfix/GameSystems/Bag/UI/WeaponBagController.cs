using System;
using Cool.Protocol;

namespace Hotfix.GameSystems.Bag.UI
{
    /// <summary>外观应用结果：Missing = 权威元素剑资源缺失，Unavailable = 本地角色尚未装配。</summary>
    internal enum WeaponAppearanceResult
    {
        Applied,
        Missing,
        Unavailable
    }

    internal enum WeaponEquipOutcome
    {
        Stale,
        Accepted,
        Rejected
    }

    /// <summary>
    /// 简易武器背包的交互会话：单在途请求、5 秒确认超时、权威装备对账与外观延迟应用。
    /// 生命周期跟随连接——角色重建保留摘要与在途序号，断线由宿主 Reset。
    /// </summary>
    internal sealed class WeaponBagController
    {
        public const float ConfirmTimeoutSeconds = 5f;
        public const string PendingStatus = "换装中";
        public const string TimeoutStatus = "换装确认超时，可重试";
        public const string MissingAppearanceStatus = "武器外观暂不可用";
        public const string DeadStatus = "死亡状态无法换装";
        public const string SkillStatus = "技能执行中无法换装";

        private readonly Func<bool> _isDead;
        private readonly Func<bool> _isSkillActive;
        private readonly Func<long, uint> _requestEquip;
        private readonly Func<int, float, WeaponAppearanceResult> _applyAppearance;

        private long _equippedInstanceId;
        private long _pendingInstanceId;
        private uint _pendingSeq;
        private float _pendingSince;
        private string _status = string.Empty;
        private bool _appearanceMissing;
        private bool _appearancePending;
        private int _lastElementId;
        private float _lastAttackSpeed = 1f;

        public WeaponBagController(Func<bool> isDead, Func<bool> isSkillActive,
            Func<long, uint> requestEquip,
            Func<int, float, WeaponAppearanceResult> applyAppearance)
        {
            _isDead = isDead;
            _isSkillActive = isSkillActive;
            _requestEquip = requestEquip;
            _applyAppearance = applyAppearance;
        }

        public long EquippedInstanceId => _equippedInstanceId;
        public long PendingInstanceId => _pendingInstanceId;
        public bool HasPending => _pendingSeq != 0;

        /// <summary>呈现版本：仅当装备标记、在途项、提示文本或外观缺失状态变化时递增。</summary>
        public int Revision { get; private set; }

        public string DisplayStatus => _status.Length > 0
            ? _status
            : (_appearanceMissing ? MissingAppearanceStatus : string.Empty);

        /// <summary>点击一行：通过时已发送请求并进入“换装中”。</summary>
        public bool TryRequest(long weaponInstanceId, float now)
        {
            if (weaponInstanceId == 0 || weaponInstanceId == _equippedInstanceId) return false;
            if (HasPending) return false;

            if (_isDead())
            {
                SetStatus(DeadStatus);
                return false;
            }
            if (_isSkillActive())
            {
                SetStatus(SkillStatus);
                return false;
            }

            uint seq = _requestEquip(weaponInstanceId);
            if (seq == 0) return false;

            _pendingSeq = seq;
            _pendingInstanceId = weaponInstanceId;
            _pendingSince = now;
            SetStatus(PendingStatus);
            Revision++;
            return true;
        }

        /// <summary>
        /// 处理一个换装确认。只有 ClientEquipSeq 与当前在途请求匹配时才结束等待；
        /// 过期确认仍可推进权威摘要，但不覆盖当前请求的提示。
        /// </summary>
        public WeaponEquipOutcome ApplyConfirm(EquipWeaponConfirm confirm,
            long authoritativeInstanceId, int elementId, float attackSpeed)
        {
            if (confirm == null) return WeaponEquipOutcome.Stale;

            bool matched = HasPending && confirm.ClientEquipSeq == _pendingSeq;
            var outcome = WeaponEquipOutcome.Stale;
            if (matched)
            {
                ClearPending();
                if (confirm.Accepted)
                {
                    outcome = WeaponEquipOutcome.Accepted;
                    SetStatus(string.Empty);
                }
                else
                {
                    outcome = WeaponEquipOutcome.Rejected;
                    SetStatus(BuildRejectStatus(confirm.RejectReason));
                }
            }

            SetEquipped(authoritativeInstanceId);
            ApplyOrDeferAppearance(elementId, attackSpeed);
            return outcome;
        }

        /// <summary>登录基线或确认后的权威装备（列表标记与外观的共同来源）。
        /// force = 角色重建后必须把同一份外观重放到新角色，不能因缓存元素未变而跳过。</summary>
        public void ApplyAuthoritative(long instanceId, int elementId, float attackSpeed, bool force = false)
        {
            SetEquipped(instanceId);
            ApplyOrDeferAppearance(elementId, attackSpeed, force);
        }

        /// <summary>角色重建：保留摘要与在途序号，把同一份外观重放到新角色。</summary>
        public void Reapply()
        {
            ApplyOrDeferAppearance(_lastElementId, _lastAttackSpeed, force: true);
        }

        /// <summary>主线程每帧：5 秒超时、技能/死亡结束后应用最新外观。</summary>
        public void Tick(float now)
        {
            if (HasPending && now - _pendingSince >= ConfirmTimeoutSeconds)
            {
                ClearPending();
                SetStatus(TimeoutStatus);
            }

            if (!HasPending && !_isDead() && !_isSkillActive()
                && (_status == DeadStatus || _status == SkillStatus))
                SetStatus(string.Empty);

            if (!_appearancePending) return;
            if (_isDead() || _isSkillActive()) return;

            ApplyAppearanceNow();
        }

        /// <summary>断线：清空请求、提示与外观状态。</summary>
        public void Reset()
        {
            _equippedInstanceId = 0;
            _pendingInstanceId = 0;
            _pendingSeq = 0;
            _pendingSince = 0f;
            _status = string.Empty;
            _appearanceMissing = false;
            _appearancePending = false;
            _lastElementId = 0;
            _lastAttackSpeed = 1f;
            Revision++;
        }

        private void ApplyOrDeferAppearance(int elementId, float attackSpeed, bool force = false)
        {
            bool unchanged = elementId == _lastElementId
                             && Math.Abs(attackSpeed - _lastAttackSpeed) < 0.0001f;
            _lastElementId = elementId;
            _lastAttackSpeed = attackSpeed;

            if (!force && !_appearancePending && unchanged) return;

            if (_isDead() || _isSkillActive())
            {
                _appearancePending = true;
                return;
            }

            ApplyAppearanceNow();
        }

        private void ApplyAppearanceNow()
        {
            var result = _applyAppearance(_lastElementId, _lastAttackSpeed);
            switch (result)
            {
                case WeaponAppearanceResult.Applied:
                    _appearancePending = false;
                    SetAppearanceMissing(false);
                    break;
                case WeaponAppearanceResult.Missing:
                    _appearancePending = false;
                    SetAppearanceMissing(true);
                    break;
                default:
                    _appearancePending = true;
                    break;
            }
        }

        private void SetEquipped(long instanceId)
        {
            if (_equippedInstanceId == instanceId) return;
            _equippedInstanceId = instanceId;
            Revision++;
        }

        private void SetStatus(string status)
        {
            string next = status ?? string.Empty;
            if (_status == next) return;
            _status = next;
            Revision++;
        }

        private void SetAppearanceMissing(bool missing)
        {
            if (_appearanceMissing == missing) return;
            _appearanceMissing = missing;
            Revision++;
        }

        private void ClearPending()
        {
            if (_pendingSeq == 0) return;
            _pendingSeq = 0;
            _pendingInstanceId = 0;
            _pendingSince = 0f;
            Revision++;
        }

        private static string BuildRejectStatus(string reason)
            => reason switch
            {
                "caster-dead" => "换装失败：角色已阵亡",
                "action-busy" => "换装失败：角色动作尚未结束",
                "stale-revision" or "stale-seq" => "换装失败：装备已更新，请重试",
                "not-owned" or "unknown-template" => "换装失败：武器不可用",
                _ => "换装失败"
            };
    }
}
