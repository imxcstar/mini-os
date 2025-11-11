using System;
using System.Collections.Generic;
using System.Threading;

namespace MiniOS
{
    public enum InputAttachMode
    {
        None,
        Foreground,
        Background
    }

    /// <summary>
    /// Coordinates which process currently owns the terminal input stream and
    /// lets processes wait until they are brought to the foreground.
    /// </summary>
    public sealed class ProcessInputRouter
    {
        private readonly object _lock = new();
        private readonly Dictionary<int, ProcessInputState> _states = new();
        private int _foregroundPid = -1;

        public void Register(int pid, InputAttachMode mode)
        {
            if (mode == InputAttachMode.None) return;
            lock (_lock)
            {
                var state = new ProcessInputState();
                _states[pid] = state;
                if (mode == InputAttachMode.Foreground)
                {
                    PromoteLocked(pid);
                }
                else
                {
                    state.SetBackground();
                }
            }
        }

        public void Unregister(int pid)
        {
            lock (_lock)
            {
                if (_states.Remove(pid) && _foregroundPid == pid)
                {
                    _foregroundPid = -1;
                }
            }
        }

        public bool BringToForeground(int pid)
        {
            lock (_lock)
            {
                if (!_states.ContainsKey(pid)) return false;
                PromoteLocked(pid);
                return true;
            }
        }

        public bool SendToBackground(int pid)
        {
            lock (_lock)
            {
                if (!_states.TryGetValue(pid, out var state)) return false;
                if (_foregroundPid == pid)
                {
                    _foregroundPid = -1;
                }
                state.SetBackground();
                return true;
            }
        }

        public void WaitForForeground(int pid, CancellationToken ct)
        {
            ProcessInputState state;
            lock (_lock)
            {
                if (!_states.TryGetValue(pid, out var found))
                    throw new InvalidOperationException("Process has no input pipe attached to the terminal");
                state = found;
            }
            state.WaitForForeground(ct);
        }

        public bool HasPipe(int pid)
        {
            lock (_lock)
            {
                return _states.ContainsKey(pid);
            }
        }

        private void PromoteLocked(int pid)
        {
            if (_foregroundPid == pid) return;
            if (_foregroundPid != -1 && _states.TryGetValue(_foregroundPid, out var previous))
            {
                previous.SetBackground();
            }
            _foregroundPid = pid;
            if (_states.TryGetValue(pid, out var current))
            {
                current.SetForeground();
            }
        }

        private sealed class ProcessInputState
        {
            private readonly ManualResetEventSlim _foreground = new(false);

            public void SetForeground() => _foreground.Set();
            public void SetBackground() => _foreground.Reset();
            public void WaitForForeground(CancellationToken ct) => _foreground.Wait(ct);
        }
    }
}
