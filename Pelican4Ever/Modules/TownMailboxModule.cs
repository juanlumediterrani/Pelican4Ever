using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Pelican4Ever.Core.World;
using Pelican4Ever.Framework;
using Pelican4Ever.Models;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Gestiona los buzones interactivos de las residencias en Pelican Town (TASK-501).
    /// Permite leer cartas sin viajar hasta la granja y muestra la banderita / animación de correo pendiente.
    /// </summary>
    public sealed class TownMailboxModule : IModule
    {
        private ModContext? context;

        public string Name => "TownMailbox";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;

            ctx.Helper.Events.Input.ButtonPressed += OnButtonPressed;
            ctx.Helper.Events.Display.RenderedWorld += OnRenderedWorld;
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady || context == null)
                return;

            if (!e.Button.IsActionButton())
                return;

            if (Game1.currentLocation == null)
                return;

            string currentLoc = Game1.currentLocation.NameOrUniqueName;
            Vector2 targetTile = e.Cursor.GrabTile;
            int targetX = (int)targetTile.X;
            int targetY = (int)targetTile.Y;

            foreach (DoorModel door in FarmSlotModule.RegisteredDoors)
            {
                string doorLoc = string.IsNullOrWhiteSpace(door.LocationName) ? HostPlazaLayout.HostFarm : door.LocationName;
                if (!string.Equals(currentLoc, doorLoc, StringComparison.OrdinalIgnoreCase))
                    continue;

                var (mbX, mbY) = TownMailboxLogic.GetMailboxCoords(door.TownTileX, door.TownTileY, door.MailboxTileX, door.MailboxTileY);
                if (TownMailboxLogic.IsMailboxTarget(targetX, targetY, mbX, mbY, door.TownTileX, door.TownTileY) && !FarmSlotModule.IsInOpeningOrBorder(door, targetX, targetY))
                {
                    HandleMailboxInteraction(door, mbX, mbY);
                    context.Helper.Input.Suppress(e.Button);
                    break;
                }
            }
        }

        private void HandleMailboxInteraction(DoorModel door, int mbX, int mbY)
        {
            if (context == null) return;

            context.WorldState.Doors.TryGetValue(door.DoorId, out DoorAssignment? assignment);
            long? ownerId = assignment?.OwnerId;
            string? ownerName = null;

            if (ownerId.HasValue)
            {
                if (context.WorldState.Players.TryGetValue(ownerId.Value, out var rec) && !string.IsNullOrWhiteSpace(rec.PlayerName))
                {
                    ownerName = rec.PlayerName;
                }
                else
                {
                    Farmer? f = Game1.GetPlayer(ownerId.Value);
                    if (f != null && !string.IsNullOrWhiteSpace(f.Name))
                        ownerName = f.Name;
                }
            }

            var eval = TownMailboxLogic.EvaluateAccess(
                Game1.player.UniqueMultiplayerID,
                ownerId,
                ownerName,
                Game1.mailbox.Count
            );

            if (eval.Status == MailboxAccessStatus.Owner)
            {
                Response[] choices = new[]
                {
                    new Response("Enter", "🏠 Entrar a mi granja"),
                    new Response("Mailbox", "📬 Revisar correo"),
                    new Response("Policy", $"🔒 Configurar acceso (Actual: {assignment?.Policy})"),
                    new Response("Cancel", "Cancelar")
                };

                QuestionDialog.Ask(
                    $"Tu buzón / poste residencial ({door.Description}).",
                    choices,
                    (who, whichAnswer) =>
                    {
                        if (whichAnswer == "Enter")
                        {
                            FarmSlotModule.WarpToFarm(door.DefaultSlotId);
                        }
                        else if (whichAnswer == "Mailbox")
                        {
                            Game1.currentLocation.mailbox();
                        }
                        else if (whichAnswer == "Policy")
                        {
                            FarmSlotModule.ShowPolicySelectionDialog(door.DefaultSlotId);
                        }
                    }
                );
            }
            else if (eval.Status == MailboxAccessStatus.Unassigned)
            {
                FarmSlotModule.InteractWithResidentialDoor(door);
            }
            else
            {
                if (assignment != null && assignment.Policy != AccessPolicy.Locked)
                {
                    Response[] choices = new[]
                    {
                        new Response("Visit", $"🏠 Visitar granja de {ownerName}"),
                        new Response("Cancel", "Cancelar")
                    };

                    QuestionDialog.Ask(
                        $"{door.Description}.\nPropiedad de {ownerName}. Estado: {assignment.Policy}.",
                        choices,
                        (who, whichAnswer) =>
                        {
                            if (whichAnswer == "Visit")
                            {
                                FarmSlotModule.WarpToFarm(door.DefaultSlotId);
                            }
                        }
                    );
                }
                else
                {
                    Game1.drawObjectDialogue($"Esta residencia pertenece a {ownerName}. La entrada está restringida y el buzón es privado.");
                }
            }
        }

        private void OnRenderedWorld(object? sender, RenderedWorldEventArgs e)
        {
            if (!Context.IsWorldReady || context == null)
                return;

            if (Game1.currentLocation == null)
                return;

            string currentLoc = Game1.currentLocation.NameOrUniqueName;
            long myId = Game1.player.UniqueMultiplayerID;
            if (Game1.mailbox.Count == 0)
                return;

            foreach (DoorModel door in FarmSlotModule.RegisteredDoors)
            {
                string doorLoc = string.IsNullOrWhiteSpace(door.LocationName) ? HostPlazaLayout.HostFarm : door.LocationName;
                if (!string.Equals(currentLoc, doorLoc, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (context.WorldState.Doors.TryGetValue(door.DoorId, out DoorAssignment? assignment) && assignment.OwnerId == myId)
                {
                    var (mbX, mbY) = TownMailboxLogic.GetMailboxCoords(door.TownTileX, door.TownTileY, door.MailboxTileX, door.MailboxTileY);
                    DrawMailboxNotice(e.SpriteBatch, mbX, mbY);
                }
            }
        }

        private void DrawMailboxNotice(SpriteBatch b, int mbX, int mbY)
        {
            float bounce = 4f * (float)Math.Round(Math.Sin(Game1.currentGameTime.TotalGameTime.TotalMilliseconds / 250.0), 2);
            float depth = (float)((mbX + 1) * 64) / 10000f + (float)(mbY * 64) / 10000f;

            // Indicador de carta de GameLocation.draw / Farm.draw nativo
            b.Draw(
                Game1.mouseCursors,
                Game1.GlobalToLocal(Game1.viewport, new Vector2(mbX * 64, (float)(mbY * 64 - 96 - 48) + bounce)),
                new Rectangle(141, 465, 20, 24),
                Color.White * 0.75f,
                0f,
                Vector2.Zero,
                4f,
                SpriteEffects.None,
                depth + 1E-06f
            );

            b.Draw(
                Game1.mouseCursors,
                Game1.GlobalToLocal(Game1.viewport, new Vector2(mbX * 64 + 32 + 4, (float)(mbY * 64 - 64 - 24 - 8) + bounce)),
                new Rectangle(189, 423, 15, 13),
                Color.White,
                0f,
                new Vector2(7f, 6f),
                4f,
                SpriteEffects.None,
                depth + 1E-05f
            );
        }
    }
}
