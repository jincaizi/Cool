using Cysharp.Threading.Tasks;

namespace Hotfix.GameSystems.UI
{
    /// <summary>
    /// 一次性放行的就绪闸门。
    ///
    /// **关键性质：等待与放行解耦** —— 放行之前 await 会挂起，放行之后 await 立刻通过。
    /// 于是"谁先谁后"不再影响正确性，这正是不再需要
    /// `while (…) await UniTask.Delay(50)` 轮询的原因。
    ///
    /// Release 幂等：重复放行不会出错，也不会有第二次效果。
    /// </summary>
    internal sealed class ReadinessGate
    {
        private readonly UniTaskCompletionSource _tcs = new UniTaskCompletionSource();

        public bool IsReleased { get; private set; }

        public void Release()
        {
            if (IsReleased) return;

            IsReleased = true;
            _tcs.TrySetResult();
        }

        public UniTask WaitAsync() => _tcs.Task;
    }
}
