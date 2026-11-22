using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.Admin
{
    /// <summary>
    /// Registro aislado de callbacks síncronos ejecutados justo antes de <c>SaveGame.Save()</c>
    /// en checkpoints o rollovers donde SMAPI no dispara <c>GameLoop.Saving</c> (TASK-2601 / CP-604).
    /// </summary>
    public sealed class PreSaveHookRegistry
    {
        private readonly object sync = new();
        private readonly List<Action> callbacks = new();

        public int Count
        {
            get
            {
                lock (sync)
                    return callbacks.Count;
            }
        }

        public void Register(Action? callback)
        {
            if (callback == null)
                return;

            lock (sync)
            {
                if (!callbacks.Contains(callback))
                    callbacks.Add(callback);
            }
        }

        public void Unregister(Action? callback)
        {
            if (callback == null)
                return;

            lock (sync)
                callbacks.Remove(callback);
        }

        public int InvokeAll(Action<Exception>? onError = null)
        {
            Action[] snapshot;
            lock (sync)
            {
                if (callbacks.Count == 0)
                    return 0;
                snapshot = callbacks.ToArray();
            }

            int succeeded = 0;
            foreach (Action cb in snapshot)
            {
                try
                {
                    cb();
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
