using UnityEngine;

namespace Hotfix.GameSystems.Sys3C.Core.Combat
{
    /// <summary>
    /// 命中部位（相对包围盒高度的计算式分区）
    /// </summary>
    public enum HitZonePart { Head = 0, Torso = 1, Legs = 2 }

    /// <summary>
    /// 命中上下文，随 TakeDamage 透传。注意：HitZonePart.Head = 0，
    /// 禁止用 default(HitContact) 代替 None（默认实例的 Part 会是 Head）。
    /// </summary>
    public struct HitContact
    {
        public Vector3 HitPoint;    // 真实接触点（世界坐标）
        public HitZonePart Part;    // 计算式部位
        public Color ElementColor;  // 攻击元素色（默认白）
        public bool HasContact;     // false = 无命中上下文（接收方用 FromDirection 重建）

        /// <summary>显式空值：Part=Torso、HasContact=false、元素色白</summary>
        public static readonly HitContact None = new HitContact
        {
            Part = HitZonePart.Torso,
            ElementColor = Color.white
        };

        /// <summary>
        /// 从攻击方向重建接触点：锚定 bounds.center（躯干高度），取自身 collider 朝攻击反方向
        /// （即攻击者一侧）的表面最近点，部位照常分类。
        /// 用于无碰撞箱路径（怪物 AttackShape 直调、技能直伤）。方向无效时兜底躯干高度点。
        /// </summary>
        public static HitContact FromDirection(Collider collider, Vector3 hitDirection)
        {
            var bounds = collider.bounds;
            Vector3 point;
            if (hitDirection.sqrMagnitude < 0.0001f)
            {
                // 兜底：躯干高度点
                point = collider.transform.position + Vector3.up * (bounds.size.y * 0.6f);
            }
            else
            {
                // 攻击反方向（攻击者一侧）的 surface 最近点，锚定 bounds.center（躯干高度）
                point = collider.ClosestPoint(bounds.center - hitDirection * 100f);
            }
            return new HitContact
            {
                HitPoint = point,
                Part = HitZoneClassifier.Classify(point, bounds),
                ElementColor = Color.white,
                HasContact = true
            };
        }
    }
}
