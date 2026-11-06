using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Netcode;
using Pelican4Ever.Core.Time;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Network;
using StardewValley.TerrainFeatures;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Spike de TASK-1601 (Épica 11): lanza un guardado nativo a mitad de día, como haría un checkpoint, y deja
    /// un informe para cerrar los gaps G1–G5 y G10. Solo consola SMAPI del host: <c>p4e_checkpoint_spike [clonar] [forzar]</c>.
    /// </summary>
    /// <remarks>
    /// <para>Es una herramienta de medición, no el checkpoint definitivo (TASK-1604). Solo se habilita con CheckpointDebugMode para partidas de prueba.</para>
    /// <para>
    /// <c>SaveGame.Save()</c> guarda los farmhands desde <c>netWorldState.farmhandData</c>, que es una copia aparte que el
    /// juego solo refresca al desconectarse el jugador o al dormir (<c>Multiplayer.saveFarmhands</c>). Esa función no
    /// sirve a mitad de día: también llama a <c>ResetFarmhandState</c> sobre el jugador vivo, que puede mandarlo a la
    /// cama o quitarlo de <c>otherFarmers</c> si su casa no es una <c>Cabin</c>. Con <c>clonar</c>, el spike copia el
    /// estado vivo a <c>farmhandData</c> con <c>NetRoot.CloneInto</c>, que es solo la parte de copia.
    /// </para>
    /// </remarks>
    public sealed class CheckpointSpikeModule : IModule
    {
        private const int RecheckDelaySeconds = 30;
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private ModContext context = null!;
        private IEnumerator<int>? save;
        private SpikeReport? report;
        private readonly Stopwatch total = new();
        private int recheckCountdown = -1;
        private string? reportPath;

        public string Name => "CheckpointSpike";

        public ModuleSide Side => ModuleSide.Host;

        public void Register(ModContext ctx)
        {
            context = ctx;
            ctx.Helper.ConsoleCommands.Add(
                "p4e_checkpoint_spike",
                "Spike TASK-1601: guarda la partida a mitad de día y escribe un informe en Pelican4Ever_Backups/spike/.\n" +
                "Uso: p4e_checkpoint_spike [clonar] [forzar]\n" +
                "  clonar: copia antes el estado vivo de los farmhands conectados a farmhandData (prueba de G4).\n" +
                "  forzar: permite lanzarlo durante un festival o evento.",
                OnCommand);
            ctx.Helper.Events.GameLoop.Saving += (_, _) => { if (save != null && report != null) report.SavingEventFired = true; };
            ctx.Helper.Events.GameLoop.Saved += (_, _) => { if (report != null && (save != null || report.SaveFinished)) report.SavedEventFired = true; };
            ctx.Helper.Events.GameLoop.OneSecondUpdateTicked += OnOneSecondUpdateTicked;
        }

        /// <summary>Hay un guardado del spike en curso; el rollover no debe empezar mientras tanto.</summary>
        public static bool IsSaving { get; private set; }

        private void OnCommand(string command, string[] args)
        {
            if (!context.Config.CheckpointDebugMode)
            {
                context.Monitor.Log("[CheckpointSpike] Desactivado: requiere CheckpointDebugMode=true y una partida de prueba.", LogLevel.Warn);
                return;
            }

            bool clone = args.Any(a => a.Equals("clonar", StringComparison.OrdinalIgnoreCase));
            bool force = args.Any(a => a.Equals("forzar", StringComparison.OrdinalIgnoreCase));

            string? refusal = CheckPreconditions(force);
            if (refusal != null)
            {
                context.Monitor.Log($"[CheckpointSpike] No se lanza: {refusal}", LogLevel.Warn);
                return;
            }

            report = new SpikeReport
            {
                StartedUtc = DateTimeOffset.UtcNow.ToString("O"),
                GameDay = Game1.Date.TotalDays,
                GameDate = Game1.Date.ToString(),
                GameTime = Game1.timeOfDay,
                CloneRequested = clone,
                Forced = force,
                OnlineFarmhands = Game1.otherFarmers.Count,
                FarmhandsBefore = ProbeFarmhands(),
                WorldBefore = ProbeWorld()
            };

            try
            {
                if (clone)
                {
                    var sw = Stopwatch.StartNew();
                    report.ClonedFarmhands = CloneLiveFarmhandsIntoSaveData();
                    report.CloneMs = sw.Elapsed.TotalMilliseconds;
                    report.FarmhandsAfterClone = ProbeFarmhands();
                }

                report.FlushSucceeded = PersistenceModule.FlushToSaveData(context, "checkpoint spike");
                total.Restart();
                save = SaveGame.Save();
                IsSaving = true;
                context.Monitor.Log($"[CheckpointSpike] Guardado iniciado (clonar={clone}, farmhands conectados={report.OnlineFarmhands}).", LogLevel.Info);
                // Mismo tramo síncrono que el checkpoint: no procesar otro tick entre clonar y guardar.
                bool more;
                do
                {
                    more = save.MoveNext();
                    report.SaveTicks++;
                } while (more);
                report.LongestTickMs = total.Elapsed.TotalMilliseconds;
                Finish(Game1.gameMode != Game1.playingGameMode
                    ? new IOException($"El juego no pudo escribir el spike: {Game1.debugOutput}")
                    : null);
            }
            catch (Exception ex)
            {
                Finish(failure: ex);
            }
        }

        private string? CheckPreconditions(bool force)
        {
            if (!Context.IsWorldReady || !Context.IsMainPlayer)
                return "hace falta una partida cargada y ser el host.";
            if (save != null || SaveGame.IsProcessing || CheckpointModule.IsSaving)
                return "ya hay un guardado en curso.";
            if (DailyRolloverModule.IsBusy)
                return "el rollover diario está en marcha.";

            TimeSpan untilRollover = RolloverTiming.UntilNextBoundary(
                ServerTime.LocalNow(context.Config.TimeZoneId).TimeOfDay,
                context.Config.ParsedCalendarDayChangeTime);
            if (untilRollover <= TimeSpan.FromMinutes(10))
                return $"faltan {untilRollover.TotalMinutes:F0} minutos para el rollover.";

            if (!force && (Game1.isFestival() || Game1.eventUp || Game1.CurrentEvent != null))
                return "hay un festival o un evento en curso (usa 'forzar' para lanzarlo igualmente).";

            return null;
        }

        private void Finish(Exception? failure)
        {
            save?.Dispose();
            save = null;
            IsSaving = false;
            total.Stop();
            if (report == null)
                return;

            report.SaveFinished = failure == null;
            report.Failure = failure?.ToString();
            report.TotalMs = total.Elapsed.TotalMilliseconds;
            report.WorldAfter = ProbeWorld();
            report.FarmhandsAfter = ProbeFarmhands();
            report.OnlineFarmhandsAfter = Game1.otherFarmers.Count;

            if (failure == null)
            {
                try
                {
                    reportPath = CopySaveAndWriteReport(report);
                }
                catch (Exception ex)
                {
                    report.Failure = $"Guardado correcto, pero la copia o el informe fallaron: {ex}";
                }
            }

            context.Monitor.Log(
                $"[CheckpointSpike] {(failure == null ? "Guardado terminado" : "Guardado FALLIDO")}: " +
                $"{report.TotalMs:F0} ms en {report.SaveTicks} pasos del enumerador, tick más largo {report.LongestTickMs:F0} ms. " +
                $"Saving={(report.SavingEventFired ? "sí" : "no")}, Saved={(report.SavedEventFired ? "sí" : "no")}, " +
                $"farmhands conectados {report.OnlineFarmhands}→{report.OnlineFarmhandsAfter}. " +
                $"Informe: {reportPath ?? "no escrito"}",
                failure == null ? LogLevel.Alert : LogLevel.Error);
            if (failure != null)
                context.Monitor.Log($"[CheckpointSpike] {failure}", LogLevel.Error);

            LogStaleFarmhands(report);
            recheckCountdown = failure == null ? RecheckDelaySeconds : -1;
            if (failure != null)
                report = null;
        }

        private void OnOneSecondUpdateTicked(object? sender, OneSecondUpdateTickedEventArgs e)
        {
            if (recheckCountdown < 0 || report == null)
                return;
            if (--recheckCountdown > 0)
                return;

            recheckCountdown = -1;
            report.WorldAfterRecheck = ProbeWorld();
            report.RecheckDelaySeconds = RecheckDelaySeconds;
            try
            {
                if (reportPath != null)
                    File.WriteAllText(reportPath, JsonSerializer.Serialize(report, JsonOptions));
            }
            catch (Exception ex)
            {
                context.Monitor.Log($"[CheckpointSpike] No se pudo reescribir el informe: {ex.Message}", LogLevel.Warn);
            }

            WorldProbe before = report.WorldBefore, after = report.WorldAfter!, later = report.WorldAfterRecheck;
            context.Monitor.Log(
                $"[CheckpointSpike] Mundo antes → justo después → +{RecheckDelaySeconds}s: " +
                $"Junimos CC {before.CommunityCenterJunimos}→{after.CommunityCenterJunimos}→{later.CommunityCenterJunimos}, " +
                $"Junimos recolectores {before.JunimoHarvesters}→{after.JunimoHarvesters}→{later.JunimoHarvesters}, " +
                $"tiendas {before.Tents}→{after.Tents}→{later.Tents}, " +
                $"personajes WitchHut {before.WitchHutCharacters}→{after.WitchHutCharacters}→{later.WitchHutCharacters}, " +
                $"compañeros de trinkets {before.TrinketCompanions}→{after.TrinketCompanions}→{later.TrinketCompanions}.",
                LogLevel.Alert);
            report = null;
            reportPath = null;
        }

        private int CloneLiveFarmhandsIntoSaveData()
        {
            int cloned = 0;
            foreach (KeyValuePair<long, NetRoot<Farmer>> pair in Game1.otherFarmers.Roots.ToList())
            {
                if (pair.Value is not NetFarmerRoot root)
                    continue;
                if (!Game1.netWorldState.Value.farmhandData.FieldDict.TryGetValue(pair.Key, out NetRef<Farmer>? stored))
                {
                    context.Monitor.Log($"[CheckpointSpike] {root.Value?.Name} ({pair.Key}) no tiene entrada en farmhandData; no se clona.", LogLevel.Warn);
                    continue;
                }

                root.CloneInto(stored);
                cloned++;
            }
            return cloned;
        }

        private List<FarmhandProbe> ProbeFarmhands()
        {
            var result = new List<FarmhandProbe>();
            foreach (Farmer live in Game1.otherFarmers.Values)
            {
                Game1.netWorldState.Value.farmhandData.TryGetValue(live.UniqueMultiplayerID, out Farmer? stored);
                result.Add(new FarmhandProbe
                {
                    Id = live.UniqueMultiplayerID,
                    Name = live.Name,
                    SameInstance = ReferenceEquals(live, stored),
                    Live = FarmerState.From(live),
                    Stored = stored == null ? null : FarmerState.From(stored)
                });
            }
            return result;
        }

        private static WorldProbe ProbeWorld()
        {
            var probe = new WorldProbe();
            Utility.ForEachLocation(location =>
            {
                foreach (NPC npc in location.characters)
                {
                    if (npc is Junimo)
                        probe.CommunityCenterJunimos++;
                    else if (npc is JunimoHarvester)
                        probe.JunimoHarvesters++;
                }
                probe.Tents += location.largeTerrainFeatures.Count(f => f is Tent);
                if (location.Name == "WitchHut")
                    probe.WitchHutCharacters += location.characters.Count;
                return true;
            });

            foreach (Farmer farmer in Game1.getOnlineFarmers())
            {
                probe.FarmersWithTrinkets += farmer.trinketItems.Any(t => t != null) ? 1 : 0;
                probe.TrinketCompanions += farmer.companions.Count;
            }
            probe.GlobalInventories = Game1.player.team.globalInventories.Length;
            return probe;
        }

        private void LogStaleFarmhands(SpikeReport r)
        {
            foreach (FarmhandProbe probe in r.FarmhandsBefore)
            {
                if (probe.Stored == null)
                {
                    context.Monitor.Log($"[CheckpointSpike] G4: {probe.Name} no está en farmhandData.", LogLevel.Alert);
                    continue;
                }

                List<string> differences = probe.Live.DifferencesFrom(probe.Stored);
                context.Monitor.Log(
                    differences.Count == 0
                        ? $"[CheckpointSpike] G4: {probe.Name}: la copia que se guarda coincidía con el estado vivo antes de guardar."
                        : $"[CheckpointSpike] G4: {probe.Name}: la copia que se guarda iba RETRASADA antes de guardar ({string.Join("; ", differences)}).",
                    LogLevel.Alert);

                FarmhandProbe? afterClone = r.FarmhandsAfterClone?.FirstOrDefault(p => p.Id == probe.Id);
                if (afterClone?.Stored != null)
                {
                    List<string> left = afterClone.Live.DifferencesFrom(afterClone.Stored);
                    context.Monitor.Log(
                        $"[CheckpointSpike] G4: {probe.Name} tras clonar: {(left.Count == 0 ? "coincide" : string.Join("; ", left))}.",
                        LogLevel.Alert);
                }
            }
        }

        private string CopySaveAndWriteReport(SpikeReport r)
        {
            string savePath = Constants.CurrentSavePath ?? throw new IOException("No hay ruta de la partida actual.");
            string folder = Path.Combine(Constants.DataPath, "Pelican4Ever_Backups", "spike", $"{Constants.SaveFolderName}_{DateTime.UtcNow:yyyyMMdd_HHmmss}");
            string saveCopy = Path.Combine(folder, "save");
            Directory.CreateDirectory(saveCopy);
            foreach (string file in Directory.GetFiles(savePath, "*", SearchOption.TopDirectoryOnly))
                File.Copy(file, Path.Combine(saveCopy, Path.GetFileName(file)));

            r.SaveCopyPath = saveCopy;
            r.SaveFileBytes = new FileInfo(Path.Combine(savePath, Constants.SaveFolderName!)).Length;
            string path = Path.Combine(folder, "report.json");
            File.WriteAllText(path, JsonSerializer.Serialize(r, JsonOptions));
            return path;
        }

        private sealed class SpikeReport
        {
            public string StartedUtc { get; set; } = string.Empty;
            public int GameDay { get; set; }
            public string GameDate { get; set; } = string.Empty;
            public int GameTime { get; set; }
            public bool CloneRequested { get; set; }
            public bool Forced { get; set; }
            public int OnlineFarmhands { get; set; }
            public int OnlineFarmhandsAfter { get; set; }
            public bool FlushSucceeded { get; set; }
            public int ClonedFarmhands { get; set; }
            public double CloneMs { get; set; }
            public bool SavingEventFired { get; set; }
            public bool SavedEventFired { get; set; }
            public bool SaveFinished { get; set; }
            public string? Failure { get; set; }
            public int SaveTicks { get; set; }
            public double LongestTickMs { get; set; }
            public double TotalMs { get; set; }
            public long SaveFileBytes { get; set; }
            public string? SaveCopyPath { get; set; }
            public int RecheckDelaySeconds { get; set; }
            public List<FarmhandProbe> FarmhandsBefore { get; set; } = new();
            public List<FarmhandProbe>? FarmhandsAfterClone { get; set; }
            public List<FarmhandProbe>? FarmhandsAfter { get; set; }
            public WorldProbe WorldBefore { get; set; } = new();
            public WorldProbe? WorldAfter { get; set; }
            public WorldProbe? WorldAfterRecheck { get; set; }
        }

        private sealed class FarmhandProbe
        {
            public long Id { get; set; }
            public string Name { get; set; } = string.Empty;
            public bool SameInstance { get; set; }
            public FarmerState Live { get; set; } = new();
            public FarmerState? Stored { get; set; }
        }

        /// <summary>Lo que se compara entre el jugador vivo, la copia de <c>farmhandData</c> y el XML guardado.</summary>
        private sealed class FarmerState
        {
            public string Location { get; set; } = string.Empty;
            public int TileX { get; set; }
            public int TileY { get; set; }
            public float Stamina { get; set; }
            public int TotalExperience { get; set; }
            public int MailReceived { get; set; }
            public int FriendshipPoints { get; set; }
            public int ItemStacks { get; set; }
            public string Items { get; set; } = string.Empty;

            public static FarmerState From(Farmer farmer)
            {
                return new FarmerState
                {
                    Location = farmer.currentLocation?.NameOrUniqueName ?? string.Empty,
                    TileX = farmer.TilePoint.X,
                    TileY = farmer.TilePoint.Y,
                    Stamina = farmer.Stamina,
                    TotalExperience = farmer.experiencePoints.Sum(),
                    MailReceived = farmer.mailReceived.Count,
                    FriendshipPoints = farmer.friendshipData.Values.Sum(f => f.Points),
                    ItemStacks = farmer.Items.Where(i => i != null).Sum(i => i.Stack),
                    Items = string.Join(",", farmer.Items.Where(i => i != null).Select(i => $"{i.QualifiedItemId}x{i.Stack}"))
                };
            }

            public List<string> DifferencesFrom(FarmerState other)
            {
                var d = new List<string>();
                if (Items != other.Items)
                    d.Add($"inventario {other.ItemStacks}→{ItemStacks} unidades");
                if (Math.Abs(Stamina - other.Stamina) > 0.5f)
                    d.Add($"energía {other.Stamina:F0}→{Stamina:F0}");
                if (TotalExperience != other.TotalExperience)
                    d.Add($"experiencia {other.TotalExperience}→{TotalExperience}");
                if (MailReceived != other.MailReceived)
                    d.Add($"correo {other.MailReceived}→{MailReceived}");
                if (FriendshipPoints != other.FriendshipPoints)
                    d.Add($"amistad {other.FriendshipPoints}→{FriendshipPoints}");
                if (Location != other.Location || TileX != other.TileX || TileY != other.TileY)
                    d.Add($"posición {other.Location}({other.TileX},{other.TileY})→{Location}({TileX},{TileY})");
                return d;
            }
        }

        private sealed class WorldProbe
        {
            public int CommunityCenterJunimos { get; set; }
            public int JunimoHarvesters { get; set; }
            public int Tents { get; set; }
            public int WitchHutCharacters { get; set; }
            public int FarmersWithTrinkets { get; set; }
            public int TrinketCompanions { get; set; }
            public int GlobalInventories { get; set; }
        }
    }
}
