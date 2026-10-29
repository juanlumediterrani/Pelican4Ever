using System;
using System.IO;
using System.Linq;
using Pelican4Ever.Core.Players;
using Pelican4Ever.Core.Time;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Gestiona las alertas previas, el disparo del Rollover nativo (NewDay + SaveGame.Save)
    /// a las 06:00 AM y la evaluación de la política de descanso (TASK-103 / ADR-0001 / ADR-0002).
    /// </summary>
    public sealed class DailyRolloverModule : IModule
    {
        private static ModContext context = null!;
        private bool warned15 = false;
        private bool warned5 = false;
        private bool warned1 = false;
        private int lastProcessedDay = -1;

        public string Name => "DailyRollover";

        public ModuleSide Side => ModuleSide.Host;

        public void Register(ModContext ctx)
        {
            context = ctx;

            ctx.Helper.Events.GameLoop.OneSecondUpdateTicked += OnOneSecondUpdateTicked;
            ctx.Helper.Events.GameLoop.DayStarted += OnDayStarted;
            ctx.Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;

            ctx.Helper.ConsoleCommands.Add("p4e_rollover_now", "Fuerza el Rollover diario inmediatamente en caliente.", OnForceRollover);
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            if (!Context.IsMainPlayer)
                return;

            lastProcessedDay = Game1.Date.TotalDays;
            CheckAndPlanCatchUp();
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            if (!Context.IsMainPlayer)
                return;

            int newDay = Game1.Date.TotalDays;
            int endedDay = newDay - 1;

            context.Monitor.Log($"[DailyRolloverModule] Día iniciado: {Game1.Date} (TotalDays: {newDay}). Procesando consecuencias del día {endedDay}...", LogLevel.Info);

            // Evaluar estado de descanso y penalizaciones para todos los jugadores registrados
            var worldState = context.WorldState;
            var cfg = context.Config;

            foreach (var kvp in worldState.Players)
            {
                var record = kvp.Value;
                bool isOnline = Game1.getOnlineFarmers().Any(f => f.UniqueMultiplayerID == record.UniqueMultiplayerID);
                RolloverOutcome outcome = SleepPolicy.Evaluate(record, endedDay, isOnline);
                SleepPolicy.Commit(record, outcome);

                // Si el jugador está conectado o en la partida, ajustar energía y aplicar gasto médico si fue penalizado
                Farmer? farmer = Game1.GetPlayer(record.UniqueMultiplayerID);
                if (farmer != null)
                {
                    farmer.Stamina = SleepPolicy.WakeUpEnergy(outcome, farmer.MaxStamina, cfg.PenaltyEnergyFraction, farmer.Stamina);

                    if (outcome == RolloverOutcome.Penalized)
                    {
                        int fee = SleepPolicy.MedicalFee(farmer.Money, cfg.PenaltyGoldPercent, cfg.PenaltyGoldCap);
                        if (fee > 0)
                        {
                            int newMoney = Math.Max(0, farmer.Money - fee);
                            SetFarmerMoney(farmer, newMoney);
                            context.Monitor.Log($"[DailyRolloverModule] Cargo médico de {fee}g aplicado a {record.PlayerName} ({record.UniqueMultiplayerID}).", LogLevel.Info);
                        }
                    }
                }

                // Resetear contadores de comida y spa para el nuevo día
                record.EnsureDailyCounters(newDay);
            }

            worldState.LastRolloverGameDay = endedDay;

            // Congelar caída de amistad por inactividad para jugadores desconectados
            FreezeFriendshipDecayForOfflinePlayers();

            // Crear copia de seguridad rotativa del guardado
            CreateSaveBackup();

            // Reiniciar banderas de aviso para el próximo rollover
            warned15 = false;
            warned5 = false;
            warned1 = false;
            lastProcessedDay = newDay;
        }

        private void OnOneSecondUpdateTicked(object? sender, OneSecondUpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady || !Context.IsMainPlayer)
                return;

            var cfg = context.Config;
            DateTime now = ServerTime.LocalNow(cfg.TimeZoneId);
            TimeSpan timeOfDay = now.TimeOfDay;
            TimeSpan rolloverTime = cfg.ParsedDayStart; // 06:00:00

            // Calcular diferencia hasta las 06:00
            TimeSpan diff = rolloverTime - timeOfDay;
            if (diff < TimeSpan.Zero)
                diff += TimeSpan.FromDays(1);

            double minutesRemaining = diff.TotalMinutes;

            // Avisos previos (15, 5 y 1 minuto)
            if (minutesRemaining <= 15 && minutesRemaining > 14 && !warned15)
            {
                warned15 = true;
                BroadcastHUDMessage("Aviso: El Rollover diario se ejecutará en 15 minutos (06:00). Guarda tus tareas o ve a la cama.", HUDMessage.newQuest_type);
            }
            else if (minutesRemaining <= 5 && minutesRemaining > 4 && !warned5)
            {
                warned5 = true;
                BroadcastHUDMessage("Aviso: El Rollover diario se ejecutará en 5 minutos.", HUDMessage.error_type);
            }
            else if (minutesRemaining <= 1 && minutesRemaining > 0.08 && !warned1)
            {
                warned1 = true;
                BroadcastHUDMessage("Atención: Rollover en menos de 1 minuto. La pantalla de guardado comenzará en breve.", HUDMessage.error_type);
            }

            // Ejecución del Rollover exacto a las 06:00 (en los primeros 3 segundos de las 06:00)
            if (timeOfDay.Hours == rolloverTime.Hours && timeOfDay.Minutes == rolloverTime.Minutes && timeOfDay.Seconds <= 2)
            {
                // Para evitar disparos repetidos en el mismo minuto
                if (lastProcessedDay == Game1.Date.TotalDays)
                {
                    context.Monitor.Log("[DailyRolloverModule] Hora 06:00 alcanzada. Disparando Rollover nativo...", LogLevel.Info);
                    ExecuteRollover();
                }
            }
        }

        private void OnForceRollover(string command, string[] args)
        {
            context.Monitor.Log("[DailyRolloverModule] Ejecutando Rollover forzado vía consola...", LogLevel.Info);
            TriggerRolloverManual();
        }

        public static void TriggerRolloverManual()
        {
            if (context == null) return;
            BroadcastHUDMessage("Atención: Un administrador ha activado el Rollover diario inmediato.", HUDMessage.error_type);
            try
            {
                Game1.PassOutNewDay();
            }
            catch (Exception ex)
            {
                context.Monitor.Log($"[DailyRolloverModule] Error al iniciar NewDay nativo: {ex}", LogLevel.Error);
            }
        }

        private void ExecuteRollover()
        {
            try
            {
                // Disparo de la transición nativa de cambio de día
                Game1.PassOutNewDay();
            }
            catch (Exception ex)
            {
                context.Monitor.Log($"[DailyRolloverModule] Error al iniciar NewDay nativo: {ex}", LogLevel.Error);
            }
        }

        private static void BroadcastHUDMessage(string message, int type)
        {
            Game1.addHUDMessage(new HUDMessage(message, type));
            context?.Monitor.Log($"[HUD] {message}", LogLevel.Info);
        }

        private void FreezeFriendshipDecayForOfflinePlayers()
        {
            try
            {
                int today = Game1.Date.TotalDays;
                var onlineIds = Game1.getOnlineFarmers().Select(f => f.UniqueMultiplayerID).ToHashSet();

                foreach (var kvp in context.WorldState.Players)
                {
                    long uid = kvp.Key;
                    if (!onlineIds.Contains(uid))
                    {
                        Farmer? farmer = Game1.GetPlayer(uid);
                        if (farmer != null)
                        {
                            // En Stardew Valley, la amistad baja -2 puntos cada día que no hablas con un NPC.
                            // Para jugadores offline, sumamos +2 a todos sus vínculos para anular la caída diaria.
                            foreach (var friendship in farmer.friendshipData.Values)
                            {
                                if (friendship.Points > 0 && friendship.Points < 2500)
                                {
                                    friendship.Points = Math.Min(2500, friendship.Points + 2);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                context.Monitor.Log($"[DailyRolloverModule] Error al pausar amistad offline: {ex}", LogLevel.Warn);
            }
        }

        private void CheckAndPlanCatchUp()
        {
            var cfg = context.Config;
            var calendar = new ServerCalendar(cfg.ParsedCalendarEpoch, cfg.ParsedDayStart);
            DateTime now = ServerTime.LocalNow(cfg.TimeZoneId);

            int savedDays = Game1.Date.TotalDays;
            CatchUpPlan plan = calendar.PlanCatchUp(savedDays, now, cfg.MaxCatchUpRollovers);

            if (plan.IsAhead)
            {
                context.Monitor.Log($"[DailyRolloverModule] AVISO: La partida guardada (día {savedDays}) va por delante de la fecha real esperada ({calendar.GetDate(now)}). Revisa CalendarEpoch o el reloj del sistema.", LogLevel.Warn);
            }
            else if (plan.RolloversToRun > 0)
            {
                context.Monitor.Log($"[DailyRolloverModule] Desfase detectado con el calendario real. Plan de recuperación: {plan.RolloversToRun} rollovers pendientes.", LogLevel.Info);
            }
        }

        private void CreateSaveBackup()
        {
            try
            {
                string? savesPath = Constants.CurrentSavePath;
                if (!string.IsNullOrEmpty(savesPath) && Directory.Exists(savesPath))
                {
                    string backupDir = Path.Combine(Constants.DataPath, "Pelican4Ever_Backups");
                    Directory.CreateDirectory(backupDir);

                    string dest = Path.Combine(backupDir, $"{Constants.SaveFolderName}_{DateTime.Now:yyyyMMdd_HHmmss}");
                    Directory.CreateDirectory(dest);

                    foreach (string file in Directory.GetFiles(savesPath))
                    {
                        File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
                    }

                    // Limitar a los 7 backups más recientes
                    var oldBackups = new DirectoryInfo(backupDir).GetDirectories()
                        .OrderByDescending(d => d.CreationTime)
                        .Skip(7);

                    foreach (var old in oldBackups)
                    {
                        old.Delete(recursive: true);
                    }

                    context.Monitor.Log($"[DailyRolloverModule] Backup de seguridad creado en: {dest}", LogLevel.Trace);
                }
            }
            catch (Exception ex)
            {
                context.Monitor.Log($"[DailyRolloverModule] Error al crear backup de seguridad: {ex}", LogLevel.Warn);
            }
        }

        /// <summary>
        /// Ajusta de forma segura los fondos de un granjero respetando carteras individuales en Stardew Valley 1.6.
        /// </summary>
        public static void SetFarmerMoney(Farmer farmer, int newMoney)
        {
            if (Game1.player?.team != null && Game1.player.team.useSeparateWallets.Value && farmer != Game1.player)
            {
                Game1.player.team.SetIndividualMoney(farmer, newMoney);
            }
            else
            {
                farmer.Money = newMoney;
            }
        }
    }
}
