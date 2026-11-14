using System;
using System.Linq;
using Pelican4Ever.Core.World;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Garantiza que el desbloqueo vanilla del Horno (evento de Clint) llegue a cada jugador de forma
    /// independiente. La bandera <c>copperFound</c> se marca en el propio granjero local en cuanto
    /// obtiene mineral de cobre, así que el evento se juega con el <c>Game1.player</c> de cada cliente
    /// sin depender del anfitrión. Si el jugador no llega a entrar en la granja dentro de la ventana del
    /// evento, se entrega la receta como red de seguridad a partir de las 12:00.
    /// </summary>
    public sealed class FurnaceUnlockModule : IModule
    {
        private ModContext? context;

        public string Name => "FurnaceUnlock";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;

            ctx.Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            ctx.Helper.Events.GameLoop.DayStarted += OnDayStarted;
            ctx.Helper.Events.GameLoop.TimeChanged += OnTimeChanged;
            ctx.Helper.Events.Player.InventoryChanged += OnInventoryChanged;
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            EnsureCopperFoundAndFallback("carga de partida");
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            EnsureCopperFoundAndFallback("inicio del día");
        }

        private void OnTimeChanged(object? sender, TimeChangedEventArgs e)
        {
            // Comprobación periódica: cubre el caso de que el inventario cambie por sincronización de red
            // sin que se dispare InventoryChanged, y aplica la red de seguridad a partir de las 12:00.
            EnsureCopperFoundAndFallback("hora del juego");
        }

        private void OnInventoryChanged(object? sender, InventoryChangedEventArgs e)
        {
            if (!e.IsLocalPlayer)
                return;

            bool addedCopper = e.Added.Any(item => item?.QualifiedItemId == FurnaceUnlockLogic.CopperOreQualifiedId);
            if (!addedCopper)
                return;

            MarkCopperFoundFlag("inventario");
        }

        private void EnsureCopperFoundAndFallback(string origin)
        {
            if (!Context.IsWorldReady || Game1.player == null)
                return;

            if (HasCopperOre())
                MarkCopperFoundFlag(origin);

            GrantFallbackIfDue(origin);
        }

        private static bool HasCopperOre()
        {
            if (Game1.player.stats.CopperFound > 0)
                return true;

            return Game1.player.Items.Any(item => item?.QualifiedItemId == FurnaceUnlockLogic.CopperOreQualifiedId);
        }

        /// <summary>
        /// Marca <c>copperFound</c> directamente en el granjero local. Es una bandera sin carta, así que
        /// <c>mailReceived</c> es su destino natural y el evento de Clint puede comprobarla sin esperar al
        /// siguiente rollover ni depender de que el anfitrión procese el correo del cliente.
        /// </summary>
        private void MarkCopperFoundFlag(string origin)
        {
            if (!Context.IsWorldReady || Game1.player == null)
                return;

            bool alreadyQueued = Game1.player.hasOrWillReceiveMail(FurnaceUnlockLogic.CopperFoundMailId);
            if (!FurnaceUnlockLogic.ShouldSetCopperFound(alreadyQueued, HasCopperOre()))
                return;

            // Limpieza defensiva: si una versión anterior la dejó en la cola diaria, se retira para no
            // duplicar la entrega cuando el anfitrión procese el correo pendiente.
            Game1.player.mailForTomorrow.Remove(FurnaceUnlockLogic.CopperFoundMailId);
            Game1.player.mailForTomorrow.Remove(FurnaceUnlockLogic.CopperFoundMailId + FarmhandMailDeliveryLogic.NoLetterSuffix);

            if (Game1.player.mailReceived.Add(FurnaceUnlockLogic.CopperFoundMailId))
            {
                context?.Monitor.Log(
                    $"[FurnaceUnlock] {Game1.player.Name}: bandera '{FurnaceUnlockLogic.CopperFoundMailId}' marcada; evento de Clint disponible ({origin}).",
                    LogLevel.Trace);
            }
        }

        /// <summary>
        /// Red de seguridad: si la bandera ya está en poder del jugador (recibida o pendiente para el próximo
        /// día), la ventana del evento de Clint pasó y la receta sigue sin aprenderse, se entrega directamente.
        /// Así ningún farmhand queda bloqueado aunque nunca visite la granja del anfitrión a la hora del evento
        /// o aunque el anfitrión todavía no haya procesado su correo.
        /// </summary>
        private void GrantFallbackIfDue(string origin)
        {
            if (!Context.IsWorldReady || Game1.player == null)
                return;

            bool hasMail = Game1.player.hasOrWillReceiveMail(FurnaceUnlockLogic.CopperFoundMailId);
            bool knowsRecipe = Game1.player.craftingRecipes.ContainsKey(FurnaceUnlockLogic.FurnaceRecipeKey);
            if (!FurnaceUnlockLogic.ShouldGrantFallback(hasMail, knowsRecipe, Game1.timeOfDay))
                return;

            Game1.player.craftingRecipes.TryAdd(FurnaceUnlockLogic.FurnaceRecipeKey, 0);
            Game1.addHUDMessage(new HUDMessage(I18n.Get("furnace_unlock.learned"), HUDMessage.newQuest_type));
            context?.Monitor.Log(
                $"[FurnaceUnlock] {Game1.player.Name}: receta '{FurnaceUnlockLogic.FurnaceRecipeKey}' entregada como red de seguridad ({origin}, hora {Game1.timeOfDay}).",
                LogLevel.Info);
        }
    }
}
