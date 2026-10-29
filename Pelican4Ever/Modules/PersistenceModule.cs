using Pelican4Ever.Core.World;
using StardewModdingAPI;
using StardewModdingAPI.Events;

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

            context.Helper.Data.WriteSaveData(ServerWorldState.SaveDataKey, context.WorldState);
            context.Monitor.Log("ServerWorldState guardado en la partida maestra.", LogLevel.Trace);
        }
    }
}
