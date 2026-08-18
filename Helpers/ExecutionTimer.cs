using System;

namespace AgentSyncConsole.Helpers
{
    /// <summary>
    /// Replaces the original `Date.now() - START_TIME > MAX_RUNTIME` mid-page
    /// safety net. In the original, each Job execution processed exactly one
    /// page and START_TIME was fresh per execution. Since Program.cs now
    /// loops through pages in one long-running process, Reset() is called at
    /// the start of every page so the guard's semantics ("this page's
    /// processing must not run longer than MAX_RUNTIME_MS") stay identical.
    /// </summary>
    public class ExecutionTimer
    {
        private readonly int _maxRuntimeMs;
        private DateTime _start;

        public ExecutionTimer(int maxRuntimeMs)
        {
            _maxRuntimeMs = maxRuntimeMs;
            _start = DateTime.UtcNow;
        }

        public void Reset()
        {
            _start = DateTime.UtcNow;
        }

        public bool IsRuntimeExceeded()
        {
            return (DateTime.UtcNow - _start).TotalMilliseconds > _maxRuntimeMs;
        }

        public long ElapsedMilliseconds => (long)(DateTime.UtcNow - _start).TotalMilliseconds;
    }
}
