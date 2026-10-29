using System;
using System.Collections.Generic;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Pathfinding;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Gestiona el micro-deambular (Micro-Wandering) de los NPCs para dar vida continua al pueblo
    /// y la reprogramación/adaptación horaria de festivales a prime-time (TASK-401..403).
    /// </summary>
    public sealed class WorldLifeModule : IModule
    {
        private static ModContext? context;

        // Anclas de destino de los aldeanos: key = Nombre del NPC, value = (Tile base, timestamp del próximo paso)
        private static readonly Dictionary<string, (Vector2 Anchor, DateTime NextStep)> NpcAnchors = new();
        private static readonly Random Rnd = new();

        public string Name => "WorldLife";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;

            // Eventos SMAPI
            ctx.Helper.Events.Content.AssetRequested += OnAssetRequested;

            if (Context.IsMainPlayer)
            {
                ctx.Helper.Events.GameLoop.OneSecondUpdateTicked += OnOneSecondUpdateTicked;
                ctx.Helper.Events.GameLoop.DayStarted += OnDayStarted;

                // Comandos de administración para festivales
                ctx.Helper.ConsoleCommands.Add("p4e_festival_move", "Reprograma un festival: p4e_festival_move <season> <oldDay> <newDay>", OnFestivalMoveCommand);
            }
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            NpcAnchors.Clear();
        }

        private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
        {
            // 1. Modificar fechas de festivales si fueron reprogramadas en ServerWorldState
            if (e.NameWithoutLocale.IsEquivalentTo("Data/Festivals/FestivalDates"))
            {
                e.Edit(asset =>
                {
                    var data = asset.AsDictionary<string, string>().Data;
                    if (context != null)
                    {
                        foreach (var kvp in context.WorldState.RescheduledFestivals)
                        {
                            string oldKey = kvp.Key;   // ej: "spring13"
                            string newKey = kvp.Value; // ej: "spring16"
                            if (data.TryGetValue(oldKey, out string? festivalName))
                            {
                                data.Remove(oldKey);
                                data[newKey] = festivalName;
                            }
                        }
                    }
                });
            }

            // 2. Adaptar la ventana horaria de los festivales a prime-time de tarde/noche (18:00 - 23:50)
            if (e.NameWithoutLocale.StartsWith("Data/Festivals/"))
            {
                e.Edit(asset =>
                {
                    var dict = asset.AsDictionary<string, string>().Data;
                    if (dict.TryGetValue("conditions", out string? conditions))
                    {
                        string[] parts = conditions.Split('/');
                        if (parts.Length >= 2)
                        {
                            string[] timeParts = parts[1].Split(' ');
                            if (timeParts.Length >= 2)
                            {
                                int winStart = context?.Config.FestivalWindowStart ?? 1800;
                                int winEnd = context?.Config.FestivalWindowEnd ?? 2350;

                                parts[1] = $"{winStart} {winEnd}";
                                dict["conditions"] = string.Join("/", parts);
                            }
                        }
                    }
                });
            }
        }

        private void OnOneSecondUpdateTicked(object? sender, OneSecondUpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady || !Context.IsMainPlayer || context == null)
                return;

            DateTime now = DateTime.UtcNow;

            // Iterar por todos los NPCs del mundo para aplicar Micro-Wandering
            Utility.ForEachCharacter(character =>
            {
                if (character is NPC npc && !npc.IsMonster)
                {
                    // Solo si no está en medio de un diálogo, evento o controlador activo de horario
                    if (npc.controller == null && !npc.isMoving() && npc.currentLocation != null)
                    {
                        string name = npc.Name;

                        if (!NpcAnchors.TryGetValue(name, out var anchorData))
                        {
                            // Fijar ancla en la posición actual
                            NpcAnchors[name] = (npc.Tile, now.AddSeconds(Rnd.Next(30, 60)));
                        }
                        else
                        {
                            // Si llegó el momento del siguiente micro-paso
                            if (now >= anchorData.NextStep)
                            {
                                Vector2 baseTile = anchorData.Anchor;

                                // Elegir una casilla libre dentro de un radio de +/- 2 casillas
                                int offsetX = Rnd.Next(-2, 3);
                                int offsetY = Rnd.Next(-2, 3);
                                Point targetPoint = new Point((int)baseTile.X + offsetX, (int)baseTile.Y + offsetY);
                                Vector2 targetVec = new Vector2(targetPoint.X, targetPoint.Y);

                                GameLocation loc = npc.currentLocation;

                                // Validar que la casilla sea transitable y no contenga un Warp
                                if (loc.isTilePassable(targetVec) && !loc.isObjectAtTile(targetPoint.X, targetPoint.Y) && loc.isCollidingWithWarpOrDoor(new Rectangle(targetPoint.X * 64, targetPoint.Y * 64, 64, 64), npc) == null)
                                {
                                    // Asignar controlador de camino corto con velocidad reducida (paso de paseo)
                                    npc.speed = 1;
                                    npc.controller = new PathFindController(npc, loc, targetPoint, Rnd.Next(0, 4));
                                }

                                // Programar el próximo paso entre 45 y 90 segundos
                                NpcAnchors[name] = (baseTile, now.AddSeconds(Rnd.Next(45, 90)));
                            }
                        }
                    }
                    else if (npc.controller != null && npc.isMoving())
                    {
                        // Si está siguiendo un horario programado por el juego, retirar el ancla para que se reubique al llegar
                        NpcAnchors.Remove(npc.Name);
                    }
                }
                return true;
            });
        }

        private void OnFestivalMoveCommand(string command, string[] args)
        {
            if (context == null) return;

            if (args.Length < 3)
            {
                context.Monitor.Log("Uso: p4e_festival_move <season> <oldDay> <newDay> (ej: p4e_festival_move spring 13 16)", LogLevel.Info);
                return;
            }

            string season = args[0].ToLowerInvariant();
            string oldKey = $"{season}{args[1]}";
            string newKey = $"{season}{args[2]}";

            context.WorldState.RescheduledFestivals[oldKey] = newKey;
            context.Helper.GameContent.InvalidateCache("Data/Festivals/FestivalDates");

            context.Monitor.Log($"[WorldLifeModule] Festival reprogramado: {oldKey} -> {newKey}. Caché invalidada con éxito.", LogLevel.Info);
        }
    }
}
