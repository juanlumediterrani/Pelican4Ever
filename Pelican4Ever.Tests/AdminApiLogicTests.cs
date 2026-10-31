using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.Config;
using Pelican4Ever.Core.Players;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class AdminApiLogicTests
    {
        [Theory]
        [InlineData("Bearer secret_token_123", "secret_token_123", true)]
        [InlineData("bearer secret_token_123", "secret_token_123", true)]
        [InlineData("secret_token_123", "secret_token_123", true)]
        [InlineData("Bearer wrong_token", "secret_token_123", false)]
        [InlineData("", "secret_token_123", false)]
        [InlineData(null, "secret_token_123", false)]
        [InlineData("Bearer secret_token_123", "", false)]
        public void IsAuthorized_ValidatesBearerTokenCorrectly(string? authHeader, string expectedToken, bool expectedValid)
        {
            bool valid = AdminApiLogic.IsAuthorized(authHeader, expectedToken);
            Assert.Equal(expectedValid, valid);
        }

        [Fact]
        public void GetDashboardHtml_ContainsExpectedElements()
        {
            string html = AdminApiLogic.GetDashboardHtml("test_token");
            Assert.Contains("Pelican 4Ever Dashboard", html);
            Assert.Contains("/api/status", html);
            Assert.Contains("/api/rollover", html);
            Assert.Contains("/api/mines/reset", html);
            Assert.Contains("test_token", html);
        }

        // ─────────────────────────── TASK-803 Criterio 1: Seguridad y Token ───────────────────────────

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("   ", false)]
        [InlineData(ServerConfig.DefaultInsecureAdminToken, false)]
        [InlineData("  pelican4ever_secret_admin_token  ", false)]
        [InlineData(ServerConfig.ExampleAdminTokenPlaceholder, false)]
        [InlineData("my_custom_production_token_9981", true)]
        public void IsTokenConfiguredSecurely_RejectsDefaultOrEmptyToken(string? configuredToken, bool expectedSecure)
        {
            Assert.Equal(expectedSecure, AdminApiLogic.IsTokenConfiguredSecurely(configuredToken));
        }

        [Theory]
        [InlineData("/")]
        [InlineData("/dashboard")]
        [InlineData("/api/status")]
        [InlineData("/api/rollover")]
        [InlineData("/api/v1/host/clock")]
        [InlineData("/api/v1/host/players")]
        [InlineData("/api/v1/host/festival")]
        [InlineData("/api/v1/host/festival/start")]
        [InlineData("/api/v1/host/events")]
        public void CheckAccess_WithoutToken_Returns401ForRootAndAllApiRoutes(string path)
        {
            const string configured = "voyager_secure_token_2026";

            ApiAccessCheckResult noToken = AdminApiLogic.CheckAccess(path, authHeader: null, queryToken: null, configured);
            Assert.False(noToken.Allowed);
            Assert.Equal(401, noToken.StatusCode);

            ApiAccessCheckResult wrongToken = AdminApiLogic.CheckAccess(path, authHeader: "Bearer wrong", queryToken: null, configured);
            Assert.False(wrongToken.Allowed);
            Assert.Equal(401, wrongToken.StatusCode);

            ApiAccessCheckResult validBearer = AdminApiLogic.CheckAccess(path, authHeader: $"Bearer {configured}", queryToken: null, configured);
            Assert.True(validBearer.Allowed);
            Assert.Equal(200, validBearer.StatusCode);
        }

        [Fact]
        public void CheckAccess_WithDefaultInsecureToken_AlwaysReturns401()
        {
            ApiAccessCheckResult result = AdminApiLogic.CheckAccess(
                "/api/v1/host/clock",
                authHeader: $"Bearer {ServerConfig.DefaultInsecureAdminToken}",
                queryToken: null,
                configuredToken: ServerConfig.DefaultInsecureAdminToken);

            Assert.False(result.Allowed);
            Assert.Equal(401, result.StatusCode);
        }

        [Fact]
        public void GetListenerPrefixes_BindsToLoopbackByDefault()
        {
            IReadOnlyList<string> defaultPrefixes = AdminApiLogic.GetListenerPrefixes(8085, bindAllInterfaces: false);
            Assert.Contains("http://127.0.0.1:8085/", defaultPrefixes);
            Assert.Contains("http://localhost:8085/", defaultPrefixes);
            Assert.DoesNotContain(defaultPrefixes, p => p.Contains("*"));

            IReadOnlyList<string> wildcardPrefixes = AdminApiLogic.GetListenerPrefixes(8085, bindAllInterfaces: true);
            Assert.Single(wildcardPrefixes);
            Assert.Equal("http://*:8085/", wildcardPrefixes[0]);
        }

        // ─────────────────────────── TASK-803 Criterio 2: Cola del Hilo del Juego ───────────────────────────

        [Fact]
        public async Task MainThreadActionQueue_ExecutesEnqueuedActionsOnDrainingGameThread()
        {
            var queue = new MainThreadActionQueue();
            int workerThreadId = -1;
            int simulatedGameThreadId = -2;

            // Encolar desde un hilo de ThreadPool (simulando petición HTTP)
            Task<MainThreadExecutionResult<string>> enqueuedTask = Task.Run(async () =>
            {
                workerThreadId = Environment.CurrentManagedThreadId;
                return await queue.EnqueueWithThreadInfoAsync(() => $"ok-on-{Environment.CurrentManagedThreadId}");
            });

            // Esperar a que la tarea esté en la cola
            SpinWait.SpinUntil(() => queue.PendingCount == 1, TimeSpan.FromSeconds(2));
            Assert.Equal(1, queue.PendingCount);

            // Drenar desde un hilo dedicado que simula el hilo principal de UpdateTicked
            var gameThread = new Thread(() =>
            {
                simulatedGameThreadId = Environment.CurrentManagedThreadId;
                queue.Drain();
            });
            gameThread.Start();
            gameThread.Join();

            MainThreadExecutionResult<string> result = await enqueuedTask;
            Assert.Equal(simulatedGameThreadId, result.ExecutedOnThreadId);
            Assert.Equal(simulatedGameThreadId, queue.LastDrainThreadId);
            Assert.Equal($"ok-on-{simulatedGameThreadId}", result.Value);
            Assert.NotEqual(workerThreadId, result.ExecutedOnThreadId);
            Assert.Equal(1, queue.TotalExecuted);
        }

        [Fact]
        public async Task MainThreadActionQueue_DoesNotRunAnActionWhoseRequestTimedOut()
        {
            // Regresión: tras agotar el tiempo, la API ya respondió con error; si la acción se ejecutara después,
            // un reintento del cliente aplicaría la mutación dos veces.
            var queue = new MainThreadActionQueue();
            int runs = 0;

            await Assert.ThrowsAsync<TimeoutException>(() =>
                queue.EnqueueAsync(() => { runs++; return true; }, TimeSpan.FromMilliseconds(20)));

            Assert.Equal(0, queue.Drain());
            Assert.Equal(0, runs);
            Assert.Equal(1, queue.TotalAbandoned);
            Assert.Equal(0, queue.TotalExecuted);
        }

        // ─────────────────────────── TASK-803 Criterio 3: Contrato docs/host-api.md ───────────────────────────

        [Fact]
        public void HostApiCatalog_MatchesDocsHostApiMarkdownContract()
        {
            string docsPath = FindHostApiDocPath();
            Assert.True(File.Exists(docsPath), $"No se encontró {docsPath}");

            string markdown = File.ReadAllText(docsPath);
            IReadOnlyList<(string Method, string Path)> docRoutes = HostApiCatalog.ExtractDocumentedRoutes(markdown);

            Assert.NotEmpty(docRoutes);
            Assert.Equal(HostApiCatalog.Routes.Count, docRoutes.Count);

            for (int i = 0; i < HostApiCatalog.Routes.Count; i++)
            {
                HostApiRouteSpec spec = HostApiCatalog.Routes[i];
                Assert.Equal((spec.Method, spec.Path), docRoutes[i]);

                HostApiRouteSpec? matched = HostApiCatalog.MatchRoute(spec.Method, spec.Path);
                Assert.NotNull(matched);
                Assert.Equal(spec.Path, matched!.Path);
            }
        }

        private static string FindHostApiDocPath()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "docs", "host-api.md");
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }

            return Path.GetFullPath("docs/host-api.md");
        }

        // ─────────────────────────── TASK-803 Criterio 4: Registro de Sucesos (events?since=N) ───────────────────────────

        [Fact]
        public void HostEventLog_EventsAppearExactlyOnceAndInStrictAscendingOrderAcrossIncrementalPolls()
        {
            var log = new HostEventLog();

            log.Append(HostEventTypes.PlayerConnected, 10, 610, "Vecino 1 conectado");
            log.Append(HostEventTypes.FestivalSignUp, 10, 1810, "Vecino 1 apuntado");
            log.Append(HostEventTypes.FestivalRunStarted, 10, 1830, "Tanda #1 iniciada");
            log.Append(HostEventTypes.FestivalRunEnded, 10, 1900, "Tanda #1 terminada");

            var seenIds = new List<long>();
            long cursor = 0;

            // Primera consulta: since=0
            HostEventsResponseDto batch1 = log.GetSince(cursor);
            Assert.Equal(4, batch1.Events.Count);
            seenIds.AddRange(batch1.Events.Select(e => e.Id));
            cursor = batch1.LastEventId;

            // Consulta sin nuevos sucesos: devuelve vacío
            HostEventsResponseDto emptyBatch = log.GetSince(cursor);
            Assert.Empty(emptyBatch.Events);
            Assert.Equal(cursor, emptyBatch.LastEventId);

            // Nuevos sucesos de rollover, copia y desconexión
            log.Append(HostEventTypes.RolloverStarted, 10, 2550, "Rollover iniciado");
            log.Append(HostEventTypes.BackupCreated, 11, 600, "Backup creado");
            log.Append(HostEventTypes.RolloverCompleted, 11, 600, "Rollover completado");
            log.Append(HostEventTypes.PlayerDisconnected, 11, 610, "Vecino 1 desconectado");

            HostEventsResponseDto batch2 = log.GetSince(cursor);
            Assert.Equal(4, batch2.Events.Count);
            seenIds.AddRange(batch2.Events.Select(e => e.Id));

            // Verificar que cada suceso apareció exactamente una vez y en orden 1..8
            Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6, 7, 8 }, seenIds);
            Assert.Equal(seenIds.Distinct().Count(), seenIds.Count);
        }

        // ─────────────────────────── TASK-804 Criterios 1, 2 y 3: AgentHost y Reinicio Controlado ───────────────────────────

        [Fact]
        public void AgentHostLogic_WhenAgentHostEnabled_HostRestsWithoutDisconnecting_WhileFarmhandsStillDisconnect()
        {
            var hostRecord = new PlayerRecord { UniqueMultiplayerID = 1, PlayerName = "HostVoyager" };
            var clientRecord = new PlayerRecord { UniqueMultiplayerID = 2, PlayerName = "GranjeroCliente" };

            HostRestResult hostRes = AgentHostLogic.ApplyRest(hostRecord, currentGameDay: 15, isHostPlayer: true, agentHostEnabled: true);
            Assert.True(hostRes.RestRecorded);
            Assert.False(hostRes.ShouldDisconnectSession);
            Assert.Equal(15, hostRecord.RestedOnGameDay);
            Assert.Equal(15, hostRecord.LastActiveGameDay);

            HostRestResult clientRes = AgentHostLogic.ApplyRest(clientRecord, currentGameDay: 15, isHostPlayer: false, agentHostEnabled: true);
            Assert.True(clientRes.RestRecorded);
            Assert.True(clientRes.ShouldDisconnectSession);
            Assert.Equal(15, clientRecord.RestedOnGameDay);
            Assert.Equal(15, clientRecord.LastActiveGameDay);
        }

        [Fact]
        public void AgentHostLogic_WhenAgentHostDisabled_HostDisconnectsAsBefore()
        {
            var hostRecord = new PlayerRecord { UniqueMultiplayerID = 1, PlayerName = "HostNormal" };

            HostRestResult hostRes = AgentHostLogic.ApplyRest(hostRecord, currentGameDay: 15, isHostPlayer: true, agentHostEnabled: false);
            Assert.True(hostRes.RestRecorded);
            Assert.True(hostRes.ShouldDisconnectSession);
            Assert.Equal(15, hostRecord.RestedOnGameDay);
            Assert.Equal(15, hostRecord.LastActiveGameDay);
        }

        [Fact]
        public void SafeRestartCoordinator_DefaultMode_WarnsPlayersAndWaitsForNextSave()
        {
            var coordinator = new SafeRestartCoordinator();
            var now = new DateTimeOffset(2026, 10, 31, 16, 0, 0, TimeSpan.Zero);

            SafeRestartStatusDto status = coordinator.Schedule(
                reason: "Actualización del mod",
                immediate: false,
                warningMinutes: 5,
                currentGameDay: 20,
                currentGameTime: 1430,
                onlinePlayersCount: 3,
                unrestedActivePlayersCount: 2,
                nowUtc: now);

            Assert.True(status.IsPending);
            Assert.True(status.WaitingForNextSave);
            Assert.False(status.Immediate);
            Assert.Equal("after_next_save", status.Mode);
            Assert.Contains("Actualización del mod", status.WarningAnnouncement);
            Assert.True(status.ProgressAtRisk.WouldLoseUnsavedDayProgress);
            Assert.Equal(510, status.ProgressAtRisk.GameMinutesSinceMorning);

            // No debe dispararse por temporizador si está esperando al guardado
            Assert.False(coordinator.ConsumeIfImmediateTimerElapsed(now.AddMinutes(10), out _));

            // Se dispara justo al completarse el siguiente guardado/Rollover
            Assert.True(coordinator.ConsumeOnSaveCompleted(out SafeRestartStatusDto? triggered));
            Assert.NotNull(triggered);
            Assert.Equal("Actualización del mod", triggered!.Reason);
            Assert.False(coordinator.GetStatus().IsPending);
        }

        [Fact]
        public void SafeRestartCoordinator_ImmediateMode_ExecutesAfterConfiguredWarningMinutes()
        {
            var coordinator = new SafeRestartCoordinator();
            var now = new DateTimeOffset(2026, 10, 31, 16, 0, 0, TimeSpan.Zero);

            SafeRestartStatusDto status = coordinator.Schedule(
                reason: "Fallo crítico de red",
                immediate: true,
                warningMinutes: 3,
                currentGameDay: 20,
                currentGameTime: 1800,
                onlinePlayersCount: 1,
                unrestedActivePlayersCount: 1,
                nowUtc: now);

            Assert.True(status.IsPending);
            Assert.True(status.Immediate);
            Assert.False(status.WaitingForNextSave);
            Assert.Equal("immediate", status.Mode);

            // Antes de que pasen los 3 minutos no se dispara
            Assert.False(coordinator.ConsumeIfImmediateTimerElapsed(now.AddMinutes(2), out _));

            // Al cumplirse los 3 minutos se dispara
            Assert.True(coordinator.ConsumeIfImmediateTimerElapsed(now.AddMinutes(3), out SafeRestartStatusDto? triggered));
            Assert.NotNull(triggered);
            Assert.Equal("Fallo crítico de red", triggered!.Reason);
            Assert.False(coordinator.GetStatus().IsPending);
        }
    }
}
