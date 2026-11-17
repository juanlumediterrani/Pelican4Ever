using System;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.World;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using SObject = StardewValley.Object;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Impide a los visitantes modificar una residencia ajena si su dueño no lo permite
    /// (<see cref="DoorAssignment.VisitorsCanEdit"/>): herramientas de trabajo, guadaña, colocar o sembrar,
    /// cosechar a mano, árboles frutales, cofres y máquinas. Armas, honda y caña siguen permitidas.
    /// Corre en cada juego porque son acciones locales del jugador; el permiso llega en la
    /// sincronización de puertas (<c>SyncDoorsState</c>).
    /// </summary>
    public sealed class VisitorGuardModule : IModule
    {
        private static string DeniedMessage => I18n.Get("visitor.denied");

        private static ModContext? context;
        private static int lastMessageTick = -1000;

        public string Name => "VisitorGuard";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;
            Patch("Farmer.BeginUsingTool", AccessTools.Method(typeof(Farmer), nameof(Farmer.BeginUsingTool)), nameof(Prefix_BeginUsingTool));
            Patch("Utility.tryToPlaceItem", AccessTools.Method(typeof(Utility), nameof(Utility.tryToPlaceItem)), nameof(Prefix_TryToPlaceItem));
            Patch("HoeDirt.performUseAction", AccessTools.Method(typeof(HoeDirt), nameof(HoeDirt.performUseAction)), nameof(Prefix_TerrainUseAction));
            Patch("FruitTree.performUseAction", AccessTools.Method(typeof(FruitTree), nameof(FruitTree.performUseAction)), nameof(Prefix_TerrainUseAction));
            Patch("Chest.checkForAction", AccessTools.Method(typeof(Chest), nameof(Chest.checkForAction)), nameof(Prefix_ObjectCheckForAction));
            Patch("Object.checkForAction", AccessTools.Method(typeof(SObject), nameof(SObject.checkForAction)), nameof(Prefix_ObjectCheckForAction));
            Patch("GameLocation.checkAction", AccessTools.Method(typeof(GameLocation), nameof(GameLocation.checkAction),
                new[] { typeof(xTile.Dimensions.Location), typeof(xTile.Dimensions.Rectangle), typeof(Farmer) }), nameof(Prefix_LocationCheckAction));
            Patch("Object.performObjectDropInAction", AccessTools.Method(typeof(SObject), nameof(SObject.performObjectDropInAction),
                new[] { typeof(Item), typeof(bool), typeof(Farmer), typeof(bool) }), nameof(Prefix_DropIn));
        }

        private void Patch(string label, System.Reflection.MethodBase? original, string prefix)
        {
            context!.Patch($"VisitorGuard.{label}", Name, PatchCriticality.Security, original, prefix: new HarmonyMethod(typeof(VisitorGuardModule), prefix));
        }

        /// <summary>Indica si el jugador local no puede modificar esa ubicación.</summary>
        public static bool IsRestricted(GameLocation? location)
        {
            if (context == null || location == null || Game1.player == null)
                return false;

            if (ClientSecurityModule.IsLocalClientAwaitingPairing)
                return true;

            if (!Context.IsMainPlayer && context.Config.ProtectPublicHubs && VisitorPermissionLogic.IsProtectedPublicHub(location.NameOrUniqueName))
                return true;

            string? slotId = VisitorPermissionLogic.ResidenceSlotOf(location.NameOrUniqueName)
                ?? VisitorPermissionLogic.ResidenceSlotOf(location.GetRootLocation()?.NameOrUniqueName);
            if (slotId == null)
                return false;

            DoorAssignment? assignment = context.WorldState.Doors.Values
                .FirstOrDefault(d => string.Equals(d.FarmSlotId, slotId, StringComparison.OrdinalIgnoreCase));
            return !VisitorPermissionLogic.CanEdit(assignment, Game1.player.UniqueMultiplayerID);
        }

        private static int lastAlertSentTick = -1000;

        private static bool Deny()
        {
            if (Game1.ticks - lastMessageTick > 120)
            {
                lastMessageTick = Game1.ticks;
                Game1.addHUDMessage(new HUDMessage(DeniedMessage, HUDMessage.error_type));
            }
            if (!Context.IsMainPlayer && context != null && Context.IsWorldReady && Game1.MasterPlayer != null && Game1.ticks - lastAlertSentTick > 120)
            {
                lastAlertSentTick = Game1.ticks;
                try
                {
                    context.Helper.Multiplayer.SendMessage(
                        new VisitorDeniedAlertMessage
                        {
                            LocationName = Game1.currentLocation?.NameOrUniqueName ?? "desconocida",
                            Summary = "Intento de acción restringida en ubicación protegida"
                        },
                        ProbationTelemetryModule.VisitorDeniedMessageType,
                        new[] { context.Manifest.UniqueID },
                        new[] { Game1.MasterPlayer.UniqueMultiplayerID });
                }
                catch
                {
                    // No interrumpir el flujo de guardia si falla la telemetría
                }
            }
            return false;
        }

        private static bool IsAllowedTool(Tool? tool)
        {
            if (tool == null)
                return true;
            if (tool is MeleeWeapon weapon)
                return !weapon.isScythe();
            if (tool is Slingshot || tool is FishingRod || tool is Wand)
                return true;
            return VisitorPermissionLogic.IsAllowedVisitorTool(tool.GetType().FullName, tool.ItemId);
        }

        private static long lastGuardErrorMs;

        public static bool Prefix_BeginUsingTool(Farmer __instance)
        {
            try
            {
                if (__instance is null || !__instance.IsLocalPlayer || IsAllowedTool(__instance.CurrentTool) || !IsRestricted(__instance.currentLocation))
                    return true;
                return Deny();
            }
            catch (Exception ex)
            {
                // Fail-open: ante cualquier fallo inesperado (despliegue a medias, rarezas del runtime)
                // se conserva el comportamiento vanilla en lugar de romper el bucle del juego.
                LogGuardFailureOnce(ex);
                return true;
            }
        }

        private static void LogGuardFailureOnce(Exception ex)
        {
            try
            {
                long now = Environment.TickCount64;
                if (now - lastGuardErrorMs < 10_000)
                    return;
                lastGuardErrorMs = now;
                context?.Monitor.Log($"[VisitorGuard] Fallo inesperado en guardia de herramientas (fail-open): {ex.GetType().Name}: {ex.Message}", LogLevel.Warn);
            }
            catch
            {
                // Nunca romper el juego desde el propio manejador de errores.
            }
        }

        public static bool Prefix_TryToPlaceItem(GameLocation location, ref bool __result)
        {
            if (!IsRestricted(location))
                return true;
            __result = false;
            return Deny();
        }

        public static bool Prefix_TerrainUseAction(TerrainFeature __instance, ref bool __result)
        {
            if (!IsRestricted(__instance.Location))
                return true;
            __result = false;
            return Deny();
        }

        public static bool Prefix_LocationCheckAction(GameLocation __instance, xTile.Dimensions.Location tileLocation, Farmer who, ref bool __result)
        {
            try
            {
                if (who == null || !who.IsLocalPlayer || __instance?.objects == null)
                    return true;

                if (!__instance.objects.TryGetValue(new Vector2(tileLocation.X, tileLocation.Y), out SObject? obj) || obj == null)
                    return true;

                if (obj.bigCraftable.Value || obj is Chest)
                    return true;

                if (!obj.IsSpawnedObject && !obj.isForage())
                    return true;

                if (!IsRestricted(__instance))
                    return true;

                __result = false;
                return Deny();
            }
            catch (Exception ex)
            {
                LogGuardFailureOnce(ex);
                return true;
            }
        }

        public static bool Prefix_ObjectCheckForAction(SObject __instance, Farmer who, bool justCheckingForActivity, ref bool __result)
        {
            if (justCheckingForActivity || who == null || !who.IsLocalPlayer)
                return true;
            // Solo cofres y máquinas grandes aquí; el forraje y objetos aparecidos en suelo se bloquean en Prefix_LocationCheckAction.
            if (!(__instance is Chest) && !__instance.bigCraftable.Value)
                return true;
            if (!IsRestricted(__instance.Location))
                return true;
            __result = false;
            return Deny();
        }

        public static bool Prefix_DropIn(SObject __instance, bool probe, Farmer who, ref bool __result)
        {
            if (who == null || !who.IsLocalPlayer || !__instance.bigCraftable.Value || !IsRestricted(__instance.Location))
                return true;
            __result = false;
            if (!probe)
                Deny();
            return false;
        }
    }
}
