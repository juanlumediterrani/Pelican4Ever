using System;
using System.Collections.Generic;
using Pelican4Ever.Core.World;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Redes de seguridad para recetas que en vanilla solo se obtienen con un evento. Si el jugador cumple
    /// el requisito de fondo (amistad, progreso personal o carta/evento ya visto) y la receta sigue sin
    /// aprenderse después de la ventana del evento, se concede directamente.
    ///
    /// No arranca eventos, no envía correo y no vuelve a conceder nada si la receta ya existe. La receta
    /// aprendida es la clave de idempotencia, así que este módulo no puede entrar en bucles.
    /// </summary>
    public sealed class RecipeFallbackModule : IModule
    {
        private sealed class FallbackRule
        {
            public FallbackRule(string recipeKey, bool isCooking, Func<Farmer, bool> isEligible, string source)
            {
                RecipeKey = recipeKey;
                IsCooking = isCooking;
                IsEligible = isEligible;
                Source = source;
            }

            public string RecipeKey { get; }

            public bool IsCooking { get; }

            public Func<Farmer, bool> IsEligible { get; }

            public string Source { get; }
        }

        private static readonly FallbackRule[] Rules =
        {
            new FallbackRule("Garden Pot", false, IsOwnGreenhouseReady, "evento Farm:900553"),
            new FallbackRule("Mini-Jukebox", false, player => FriendshipPoints(player, "Gus") >= 1250, "evento Farm:980558"),
            new FallbackRule("Wild Bait", false, player => FriendshipPoints(player, "Linus") >= 1000, "evento Mountain:26"),
            new FallbackRule("Flute Block", false, player => FriendshipPoints(player, "Robin") >= 1500, "evento ScienceHouse:33"),
            new FallbackRule("Drum Block", false, player => FriendshipPoints(player, "Robin") >= 1500, "evento ScienceHouse:33"),
            new FallbackRule("Cookies", true, player => FriendshipPoints(player, "Evelyn") >= 1000, "evento JoshHouse:19"),
            new FallbackRule("Quality Bobber", false, player => HasMailOrEvent(player, "willyBugWadCutscene", "3131209"), "evento Beach:3131209"),
            new FallbackRule("Tea Sapling", false, player => HasMailOrEvent(player, "CarolineTea", "719926"), "evento Sunroom:719926"),
            new FallbackRule("Fairy Dust", false, player => player.mailReceived.Contains("birdieQuestFinished") || player.eventsSeen.Contains("-666777"), "evento Birdie:-666777"),
            new FallbackRule("Ostrich Incubator", false, player => player.hasOrWillReceiveMail("fieldOfficeFinale"), "evento FieldOfficeFinale")
        };

        private static readonly HashSet<string> ReportedErrors = new(StringComparer.Ordinal);

        private ModContext? context;

        public string Name => "RecipeFallback";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;

            ctx.Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            ctx.Helper.Events.GameLoop.DayStarted += OnDayStarted;
            ctx.Helper.Events.GameLoop.TimeChanged += OnTimeChanged;
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            EvaluateAll("carga de partida");
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            EvaluateAll("inicio del día");
        }

        private void OnTimeChanged(object? sender, TimeChangedEventArgs e)
        {
            EvaluateAll("hora del juego");
        }

        private void EvaluateAll(string origin)
        {
            if (!Context.IsWorldReady || Game1.player == null)
                return;

            foreach (FallbackRule rule in Rules)
            {
                try
                {
                    TryGrant(rule, origin);
                }
                catch (Exception ex)
                {
                    if (ReportedErrors.Add(rule.RecipeKey + "|" + ex.GetType().Name))
                    {
                        context?.Monitor.Log(
                            $"[RecipeFallback] Error evaluando '{rule.RecipeKey}' ({origin}): {ex}",
                            LogLevel.Error);
                    }
                }
            }
        }

        private void TryGrant(FallbackRule rule, string origin)
        {
            Farmer player = Game1.player;
            bool alreadyKnown = rule.IsCooking
                ? player.cookingRecipes.ContainsKey(rule.RecipeKey)
                : player.craftingRecipes.ContainsKey(rule.RecipeKey);

            if (!RecipeFallbackLogic.ShouldGrant(rule.IsEligible(player), alreadyKnown, Game1.timeOfDay))
                return;

            bool added = rule.IsCooking
                ? player.cookingRecipes.TryAdd(rule.RecipeKey, 0)
                : player.craftingRecipes.TryAdd(rule.RecipeKey, 0);
            if (!added)
                return;

            string displayName = GetDisplayName(rule);
            Game1.addHUDMessage(new HUDMessage(
                I18n.Get("recipe_fallback.learned", new { recipe = displayName }),
                HUDMessage.newQuest_type));
            context?.Monitor.Log(
                $"[RecipeFallback] {player.Name}: receta '{rule.RecipeKey}' entregada como red de seguridad ({rule.Source}, {origin}, hora {Game1.timeOfDay}).",
                LogLevel.Info);
        }

        private static string GetDisplayName(FallbackRule rule)
        {
            try
            {
                return new CraftingRecipe(rule.RecipeKey, rule.IsCooking).DisplayName;
            }
            catch
            {
                return rule.RecipeKey;
            }
        }

        private static int FriendshipPoints(Farmer player, string npcName)
        {
            return player.friendshipData.TryGetValue(npcName, out Friendship? friendship)
                ? friendship.Points
                : 0;
        }

        private static bool HasMailOrEvent(Farmer player, string mailId, string eventId)
        {
            return RecipeFallbackLogic.HasMailOrEvent(
                player.hasOrWillReceiveMail(mailId),
                player.eventsSeen.Contains(eventId));
        }

        /// <summary>
        /// El evento vanilla de Garden Pot exige el pantry del host. La red de seguridad usa el progreso
        /// propio: el invernadero de la residencia del jugador o su propio pantry/CC.
        /// </summary>
        private static bool IsOwnGreenhouseReady(Farmer player)
        {
            bool ownPantryMail = player.mailReceived.Contains("ccPantry")
                || player.mailReceived.Contains("jojaPantry")
                || player.hasCompletedCommunityCenter();
            bool ownGreenhouse = GetOwnFarm(player)?.greenhouseUnlocked.Value == true;

            return RecipeFallbackLogic.CanUseOwnGreenhouseFallback(ownGreenhouse, ownPantryMail, false);
        }

        private static Farm? GetOwnFarm(Farmer player)
        {
            string? home = player.homeLocation.Value;
            string? residenceSlot = VisitorPermissionLogic.ResidenceSlotOf(home);
            if (!string.IsNullOrEmpty(residenceSlot) && Game1.getLocationFromName(residenceSlot) is Farm residenceFarm)
                return residenceFarm;

            if (!string.IsNullOrEmpty(home) && Game1.getLocationFromName(home) is Farm homeFarm)
                return homeFarm;

            if (player.currentLocation is Farm currentFarm
                && (residenceSlot == null || string.Equals(currentFarm.NameOrUniqueName, residenceSlot, StringComparison.OrdinalIgnoreCase)))
            {
                return currentFarm;
            }

            return player.IsMainPlayer ? Game1.getFarm() : null;
        }
    }
}
