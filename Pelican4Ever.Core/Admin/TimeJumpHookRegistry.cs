using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.Admin
{
    /// <summary>
    /// Registro aislado de callbacks síncronos invocados tras ajustar manualmente el reloj del servidor
    /// (<c>/time</c> / <c>p4e_time</c>) y justo antes de recolocar los horarios de aldeanos (<c>CatchUpVillagerSchedules</c>),
    /// para que mods externos corten su control off-role y liberen al NPC antes del reposicionamiento (TASK-2605 / CP-611).
    /// </summary>
    public sealed class TimeJumpHookRegistry
    {
        private readonly object sync = new();
        private readonly List<Action<int, int>> callbacks = new();

        public int Count
        {
            get
            {
                lock (sync)
                    return callbacks.Count;
            }
        }

        public void Register(Action<int, int>? callback)
        {
            if (callback == null)
                return;

            lock (sync)
            {
                if (!callbacks.Contains(callback))
                    callbacks.Add(callback);
            }
        }

        public void Unregister(Action<int, int>? callback)
        {
            if (callback == null)
                return;

            lock (sync)
                callbacks.Remove(callback);
        }

        public int InvokeAll(int previousTimeOfDay, int newTimeOfDay, Action<Exception>? onError = null)
        {
            Action<int, int>[] snapshot;
            lock (sync)
            {
                if (callbacks.Count == 0)
                    return 0;
                snapshot = callbacks.ToArray();
            }

            int succeeded = 0;
            foreach (Action<int, int> cb in snapshot)
            {
                try
                {
                    cb(previousTimeOfDay, newTimeOfDay);
                    succeeded++;
                }
                catch (Exception ex)
                {
                    onError?.Invoke(ex);
                }
            }

            return succeeded;
        }
    }
}
