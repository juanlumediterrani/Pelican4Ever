using System;
using HarmonyLib;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Framework;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Projectiles;
using StardewValley.Tools;
using SObject = StardewValley.Object;

namespace Pelican4Ever.Modules
{
    public sealed class BombRestrictionsModule : IModule
    {
        private const string ExplosiveAmmoId = "(O)441";

        public string Name => "BombRestrictions";
        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext context)
        {
            context.Patch("BombRestrictions.Object.placementAction", Name, PatchCriticality.Security,
                AccessTools.Method(typeof(SObject), nameof(SObject.placementAction)),
                prefix: new HarmonyMethod(typeof(BombRestrictionsModule), nameof(BeforePlacement)));
            context.Patch("BombRestrictions.Slingshot.PerformFire", Name, PatchCriticality.Security,
                AccessTools.Method(typeof(Slingshot), nameof(Slingshot.PerformFire)),
                prefix: new HarmonyMethod(typeof(BombRestrictionsModule), nameof(BeforeFire)));
            context.Patch("BombRestrictions.BasicProjectile.explodeOnImpact", Name, PatchCriticality.Security,
                AccessTools.Method(typeof(BasicProjectile), nameof(BasicProjectile.explodeOnImpact)),
                prefix: new HarmonyMethod(typeof(BombRestrictionsModule), nameof(AllowsBombs)));
            context.Patch("BombRestrictions.Multiplayer.readSprites", Name, PatchCriticality.Security,
                AccessTools.Method(typeof(Multiplayer), nameof(Multiplayer.readSprites)),
                prefix: new HarmonyMethod(typeof(BombRestrictionsModule), nameof(BeforeReadSprites)));
        }

        private static bool AllowsBombs(GameLocation location) => location is MineShaft or VolcanoDungeon;

        private static bool BeforePlacement(SObject __instance, GameLocation location, Farmer? who, ref bool __result)
        {
            if (AllowsBombs(location) || __instance.QualifiedItemId is not ("(O)286" or "(O)287" or "(O)288"))
                return true;

            __result = false;
            ShowDeniedMessage(who ?? Game1.player);
            return false;
        }

        private static bool BeforeFire(Slingshot __instance, GameLocation location, Farmer who)
        {
            if (AllowsBombs(location) || __instance.attachments.Length == 0 ||
                __instance.attachments[0]?.QualifiedItemId != ExplosiveAmmoId)
                return true;

            ShowDeniedMessage(who);
            return false;
        }

        private static void BeforeReadSprites(GameLocation location, ref Action<TemporaryAnimatedSprite> assignSprite)
        {
            if (AllowsBombs(location))
                return;

            // El anfitrión detona las bombas que recibe como sprites de otros jugadores.
            Action<TemporaryAnimatedSprite> assign = assignSprite;
            assignSprite = sprite =>
            {
                if (sprite.bombRadius <= 0)
                    assign(sprite);
            };
        }

        private static void ShowDeniedMessage(Farmer? who)
        {
            if (who?.IsLocalPlayer == true)
                Game1.addHUDMessage(new HUDMessage(I18n.Get("bombs.mines_only"), HUDMessage.error_type));
        }
    }
}
