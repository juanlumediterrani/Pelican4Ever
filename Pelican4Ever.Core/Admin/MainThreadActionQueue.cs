using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Pelican4Ever.Core.Admin
{
    /// <summary>
    /// Resultado de ejecutar una acción en el hilo principal del juego.
    /// </summary>
    public sealed record MainThreadExecutionResult<T>(T Value, int ExecutedOnThreadId);

    /// <summary>
    /// Cola segura entre hilos para despachar acciones mutadoras desde los hilos HTTP del <c>ThreadPool</c>
    /// al hilo principal del bucle de juego (<c>UpdateTicked</c>) (TASK-803).
    /// </summary>
    public sealed class MainThreadActionQueue
    {
        private sealed class WorkItem
        {
            public WorkItem(Action execute, Action<Exception> fail, Func<bool> isAbandoned)
            {
                Execute = execute;
                Fail = fail;
                IsAbandoned = isAbandoned;
            }

            public Action Execute { get; }
            public Action<Exception> Fail { get; }

            /// <summary>Quien la encoló ya recibió una respuesta (tiempo agotado): no debe ejecutarse.</summary>
            public Func<bool> IsAbandoned { get; }
        }

        private readonly ConcurrentQueue<WorkItem> queue = new();
        private long totalExecuted;
        private long totalAbandoned;
        private int lastDrainThreadId;

        /// <summary>Número de acciones pendientes en la cola.</summary>
        public int PendingCount => queue.Count;

        /// <summary>Total histórico de acciones drenadas y ejecutadas.</summary>
        public long TotalExecuted => Interlocked.Read(ref totalExecuted);

        /// <summary>Acciones descartadas sin ejecutar porque su petición ya había agotado el tiempo.</summary>
        public long TotalAbandoned => Interlocked.Read(ref totalAbandoned);

        /// <summary>ID del último hilo que drenó la cola (hilo principal del juego).</summary>
        public int LastDrainThreadId => Volatile.Read(ref lastDrainThreadId);

        /// <summary>
        /// Encola una función para que se ejecute en el próximo tick del hilo principal y devuelve su resultado.
        /// </summary>
        public async Task<MainThreadExecutionResult<T>> EnqueueWithThreadInfoAsync<T>(Func<T> action, TimeSpan? timeout = null)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            var tcs = new TaskCompletionSource<MainThreadExecutionResult<T>>(TaskCreationOptions.RunContinuationsAsynchronously);

            queue.Enqueue(new WorkItem(
                execute: () =>
                {
                    try
                    {
                        int threadId = Environment.CurrentManagedThreadId;
                        T value = action();
                        tcs.TrySetResult(new MainThreadExecutionResult<T>(value, threadId));
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetException(ex);
                    }
                },
                fail: ex => tcs.TrySetException(ex),
                // Si la espera ya terminó por tiempo, el llamante respondió con error y puede reintentar:
                // ejecutar la acción ahora aplicaría la mutación sin que nadie lo sepa (o dos veces).
                isAbandoned: () => tcs.Task.IsCompleted));

            TimeSpan effectiveTimeout = timeout ?? TimeSpan.FromSeconds(10);
            using var cts = new CancellationTokenSource(effectiveTimeout);
            using var registration = cts.Token.Register(() =>
                tcs.TrySetException(new TimeoutException($"La acción en el hilo principal superó el tiempo límite ({effectiveTimeout.TotalSeconds:0.#}s).")));

            return await tcs.Task.ConfigureAwait(false);
        }

        /// <summary>
        /// Encola una función para que se ejecute en el próximo tick del hilo principal y devuelve su valor.
        /// </summary>
        public async Task<T> EnqueueAsync<T>(Func<T> action, TimeSpan? timeout = null)
        {
            MainThreadExecutionResult<T> res = await EnqueueWithThreadInfoAsync(action, timeout).ConfigureAwait(false);
            return res.Value;
        }

        /// <summary>
        /// Encola una acción sin valor de retorno para ejecutarse en el próximo tick del hilo principal.
        /// </summary>
        public Task EnqueueAsync(Action action, TimeSpan? timeout = null)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            return EnqueueAsync(() =>
            {
                action();
                return true;
            }, timeout);
        }

        /// <summary>
        /// Drena y ejecuta hasta <paramref name="maxActions"/> acciones en el hilo llamante (debe invocarse desde <c>UpdateTicked</c>).
        /// </summary>
        public int Drain(int maxActions = 64)
        {
            if (maxActions <= 0)
                return 0;

            Volatile.Write(ref lastDrainThreadId, Environment.CurrentManagedThreadId);

            int executed = 0;
            while (executed < maxActions && queue.TryDequeue(out WorkItem? item))
            {
                if (item.IsAbandoned())
                {
                    Interlocked.Increment(ref totalAbandoned);
                    continue;
                }

                item.Execute();
                executed++;
                Interlocked.Increment(ref totalExecuted);
            }

            return executed;
        }

        /// <summary>
        /// Cancela todas las acciones pendientes (p. ej. al detener el servidor).
        /// </summary>
        public void ClearPending(string reason = "La cola de acciones del servidor se ha detenido.")
        {
            var ex = new OperationCanceledException(reason);
            while (queue.TryDequeue(out WorkItem? item))
            {
                item.Fail(ex);
            }
        }
    }
}
