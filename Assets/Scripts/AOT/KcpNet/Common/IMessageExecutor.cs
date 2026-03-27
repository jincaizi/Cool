using System;

namespace KcpNet
{
    /// <summary>
    /// 消息执行器接口（用于Unity主线程调度）
    /// </summary>
    public interface IMessageExecutor
    {
        /// <summary>
        /// 执行操作（在适当的上下文中，如Unity主线程）
        /// </summary>
        /// <param name="action">要执行的操作</param>
        void Execute(Action action);

        /// <summary>
        /// 异步执行操作
        /// </summary>
        /// <param name="action">要执行的操作</param>
        /// <returns>任务</returns>
        System.Threading.Tasks.Task ExecuteAsync(Action action);
    }

    /// <summary>
    /// 直接执行器（在当前线程执行）
    /// </summary>
    public sealed class DirectMessageExecutor : IMessageExecutor
    {
        /// <summary>
        /// 直接执行器实例
        /// </summary>
        public static readonly DirectMessageExecutor Instance = new DirectMessageExecutor();

        private DirectMessageExecutor() { }

        /// <inheritdoc/>
        public void Execute(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            action();
        }

        /// <inheritdoc/>
        public System.Threading.Tasks.Task ExecuteAsync(Action action)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            action();
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }

    /// <summary>
    /// Unity主线程执行器（需要在Unity中实现）
    /// </summary>
    public abstract class UnityMainThreadExecutor : IMessageExecutor
    {
        /// <inheritdoc/>
        public abstract void Execute(Action action);

        /// <inheritdoc/>
        public virtual System.Threading.Tasks.Task ExecuteAsync(Action action)
        {
            Execute(action);
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }
}