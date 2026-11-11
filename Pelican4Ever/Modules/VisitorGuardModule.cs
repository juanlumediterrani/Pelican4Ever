using System;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
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
        private const string DeniedMessage = "El dueño no permite a las visitas modificar esta residencia.";

        private static ModContext? context;
        private static int lastMessageTick = -1000;

        public string Name => "VisitorGuard";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;
            try
            {
                Patch(AccessTools.Method(typeof(Farmer), nameof(Farmer.BeginUsingTool)), nameof(Prefix_BeginUsingTool));
                Patch(AccessTools.Method(typeof(Utility), nameof(Utility.tryToPlaceItem)), nameof(Prefix_TryToPlaceItem));
                Patch(AccessTools.Method(typeof(HoeDirt), nameof(HoeDirt.performUseAction)), nameof(Prefix_TerrainUseAction));
                Patch(AccessTools.Method(typeof(FruitTree), nameof(FruitTree.performUseAction)), nameof(Prefix_TerrainUseAction));
                Patch(AccessTools.Method(typeof(Chest), nameof(Chest.checkForAction)), nameof(Prefix_ObjectCheckForAction));
                Patch(AccessTools.Method(typeof(SObject), nameof(SObject.checkForAction)), nameof(Prefix_ObjectCheckForAction));
                Patch(AccessTools.Method(typeof(SObject), nameof(SObject.performObjectDropInAction),
                    new[] { typeof(Item), typeof(bool), typeof(Farmer), typeof(bool) }), nameof(Prefix_DropIn));
            }
            catch (Exception ex)
            {
                ctx.Monitor.Log($"[VisitorGuard] Error al aplicar parches Harmony: {ex}", LogLevel.Error);
            }
        }

        private static void Patch(System.Reflection.MethodBase original, string prefix)
        {
            context!.Harmony.Patch(original, prefix: new HarmonyMethod(typeof(VisitorGuardModule), prefix));
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

            string? slotId = VisitorPermissionLogic.ResidenceSlotOf(location.NameOrUniqueName);
            if (slotId == null)
                return false;

            DoorAssignment? assignment = context.WorldState.Doors.Values
                .FirstOrDefault(d => string.Equals(d.FarmSlotId, slotId, StringComparison.OrdinalIgnoreCase));
            return !VisitorPermissionLogic.CanEdit(assignment, Game1.player.UniqueMultiplayerID);
        }

        private static bool Deny()
        {
            if (Game1.ticks - lastMessageTick > 120)
            {
                lastMessageTick = Game1.ticks;
                Game1.addHUDMessage(new HUDMessage(DeniedMessage, HUDMessage.error_type));
            }
            return false;
        }

        private static bool IsAllowedTool(Tool? tool)
        {
            if (tool == null)
                return true;
            if (tool is MeleeWeapon weapon)
                return !weapon.isScythe();
            return tool is Slingshot || tool is FishingRod || tool is Wand;
        }

        public static bool Prefix_BeginUsingTool(Farmer __instance)
        {
            if (!__instance.IsLocalPlayer || IsAllowedTool(__instance.CurrentTool) || !IsRestricted(__instance.currentLocation))
                return true;
            return Deny();
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

        public static bool Prefix_ObjectCheckForAction(SObject __instance, Farmer who, bool justCheckingForActivity, ref bool __result)
        {
            if (justCheckingForActivity || who == null || !who.IsLocalPlayer)
                return true;
            // Solo cofres y objetos grandes (máquinas): el forraje y los objetos pequeños siguen su camino
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
