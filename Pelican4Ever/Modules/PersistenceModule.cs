using System;
using Pelican4Ever.Core.World;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace Pelican4Ever.Modules
{
    /// <summary>Carga, migra y guarda <see cref="ServerWorldState"/> junto a la partida maestra (solo host).</summary>
    public sealed class PersistenceModule : Framework.IModule
    {
        private Framework.ModContext context = null!;

        public string Name => "Persistence";

        public Framework.ModuleSide Side => Framework.ModuleSide.Host;

        public void Register(Framework.ModContext context)
        {
            this.context = context;
            context.Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            context.Helper.Events.GameLoop.Saving += OnSaving;
            context.Helper.Events.GameLoop.ReturnedToTitle += (_, _) => context.WorldState = new ServerWorldState();
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            if (!Context.IsMainPlayer)
                return;

            ServerWorldState state = context.Helper.Data.ReadSaveData<ServerWorldState>(ServerWorldState.SaveDataKey) ?? new ServerWorldState();
            if (state.Migrate())
                context.Monitor.Log($"ServerWorldState migrado al esquema v{state.SchemaVersion}.", LogLevel.Info);

            context.WorldState = state;
            context.Monitor.Log($"ServerWorldState cargado: {state.Players.Count} jugadores, {state.Doors.Count} puertas asignadas, {state.RescheduledFestivals.Count} festivales reprogramados.", LogLevel.Info);
        }

        private void OnSaving(object? sender, SavingEventArgs e)
        {
            if (!Context.IsMainPlayer)
                return;

            FlushToSaveData(context, "Saving");
        }

        /// <summary>
        /// Vuelca <see cref="ServerWorldState"/> a los datos de la partida para que el siguiente guardado lo incluya.
        /// Hay que llamarlo antes de cada <c>SaveGame.Save()</c> que lance el mod: no consta que SMAPI dispare
        /// <c>GameLoop.Saving</c> en un guardado que no pasa por el menú de guardado nativo (TASK-1602, gap G1).
        /// </summary>
        /// <param name="context">Contexto del mod.</param>
        /// <param name="reason">Quién pide el volcado; solo para el log.</param>
        /// <returns><c>false</c> si no se pudo escribir; el guardado seguiría con el último estado volcado.</returns>
        public static bool FlushToSaveData(Framework.ModContext context, string reason)
        {
            if (!Context.IsMainPlayer)
                return false;

            try
            {
                if (Game1.player != null && context.WorldState.Players.TryGetValue(Game1.player.UniqueMultiplayerID, out var hostRecord))
                {
                    hostRecord.LastSeenUtc = DateTimeOffset.UtcNow;
                    if (hostRecord.OfflineStaminaAnchorUtc == DateTimeOffset.MinValue)
                    {
                        hostRecord.OfflineStaminaAnchorUtc = DateTimeOffset.UtcNow;
                    }
                }

                context.Helper.Data.WriteSaveData(ServerWorldState.SaveDataKey, context.WorldState);
                context.Monitor.Log($"ServerWorldState guardado en la partida maestra ({reason}).", LogLevel.Trace);
                return true;
            }
            catch (Exception ex)
            {
                context.Monitor.Log($"No se pudo volcar ServerWorldState antes del guardado ({reason}); se guardará el último estado volcado: {ex}", LogLevel.Error);
                return false;
            }
        }
    }
}
