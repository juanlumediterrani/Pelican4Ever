using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Pelican4Ever.Core.Admin;
using StardewModdingAPI;

namespace Pelican4Ever.Framework
{
    /// <summary>
    /// Hilo ligero que emite <c>WATCHDOG=1</c> por <c>NOTIFY_SOCKET</c> cada 15 s mientras el hilo principal
    /// siga vivo dentro de <c>HangThresholdSeconds</c> (ADR-0010, TASK-2504). Sin <c>NOTIFY_SOCKET</c> no hace nada.
    /// </summary>
    public sealed class SystemdWatchdogService : IDisposable
    {
        private static readonly byte[] WatchdogPayload = Encoding.ASCII.GetBytes("WATCHDOG=1");

        private readonly HostHealthTracker healthTracker;
        private readonly Func<int> hangThresholdProvider;
        private readonly IMonitor monitor;
        private readonly string? notifySocket;
        private readonly ManualResetEventSlim stopEvent = new(false);
        private Thread? workerThread;
        private bool hangLogged;

        public bool IsActive => !string.IsNullOrWhiteSpace(notifySocket);

        public SystemdWatchdogService(
            HostHealthTracker healthTracker,
            Func<int> hangThresholdProvider,
            IMonitor monitor,
            string? notifySocketOverride = null)
        {
            this.healthTracker = healthTracker;
            this.hangThresholdProvider = hangThresholdProvider;
            this.monitor = monitor;
            notifySocket = notifySocketOverride ?? Environment.GetEnvironmentVariable("NOTIFY_SOCKET");
        }

        public void Start()
        {
            if (!IsActive || workerThread != null)
                return;

            workerThread = new Thread(RunLoop)
            {
                IsBackground = true,
                Name = "Pelican4Ever-Watchdog"
            };
            workerThread.Start();
            monitor.Log($"[Watchdog] Emisor sd_notify activo cada {WatchdogPolicy.PulseIntervalSeconds} s (umbral {hangThresholdProvider()} s).", LogLevel.Info);
        }

        private void RunLoop()
        {
            int intervalMs = WatchdogPolicy.PulseIntervalSeconds * 1000;
            while (!stopEvent.Wait(intervalMs))
            {
                double staleSeconds = healthTracker.GetSecondsSinceLastTick();
                int threshold = hangThresholdProvider();
                if (!WatchdogPolicy.ShouldSendPulse(staleSeconds, threshold))
                {
                    if (!hangLogged)
                    {
                        hangLogged = true;
                        monitor.Log(
                            $"[Watchdog] Hilo principal detenido ({staleSeconds:F1} s > {threshold} s): se suspende WATCHDOG=1.",
                            LogLevel.Error);
                    }
                    continue;
                }

                hangLogged = false;
                TrySendWatchdogDatagram();
            }
        }

        private void TrySendWatchdogDatagram()
        {
            if (string.IsNullOrWhiteSpace(notifySocket))
                return;

            try
            {
                string target = notifySocket.StartsWith("@", StringComparison.Ordinal)
                    ? "\0" + notifySocket.Substring(1)
                    : notifySocket;
                using var socket = new Socket(AddressFamily.Unix, SocketType.Dgram, ProtocolType.Unspecified);
                socket.Connect(new UnixDomainSocketEndPoint(target));
                socket.Send(WatchdogPayload);
            }
            catch (Exception ex)
            {
                monitor.Log($"[Watchdog] No se pudo enviar WATCHDOG=1 a NOTIFY_SOCKET: {ex.Message}", LogLevel.Warn);
            }
        }

        public void Dispose()
        {
            stopEvent.Set();
        }
    }
}
