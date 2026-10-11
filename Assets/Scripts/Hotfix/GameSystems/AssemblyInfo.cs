using System.Runtime.CompilerServices;

// N4：HitFSM 的死亡链步进接缝（ForceAdvanceForTest）是 internal——它不是公开 API。
// 没有这个接缝，"死亡 → 倒地 → 起身"这条由服务端驱动的链就只能靠真机 PlayMode 才验得了。
[assembly: InternalsVisibleTo("GameSys.EditorTests")]
