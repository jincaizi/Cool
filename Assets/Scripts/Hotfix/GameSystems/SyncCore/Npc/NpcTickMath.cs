namespace Cool.SyncCore
{
    /// <summary>Valid for relative windows strictly shorter than 2^31 ticks.</summary>
    public static class NpcTickMath
    {
        public static bool IsAtOrAfter(uint tick, uint other) => unchecked((int)(tick - other)) >= 0;
        public static bool IsBefore(uint tick, uint other) => unchecked((int)(tick - other)) < 0;
        public static uint Elapsed(uint tick, uint start) => unchecked(tick - start);
        public static bool IsInWindow(uint tick, uint start, uint end) => IsAtOrAfter(tick, start) && IsBefore(tick, end);
    }
}
