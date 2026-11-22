using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.World;
using Pelican4Ever.Framework;
using Pelican4Ever.Models;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buildings;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Gestiona los buzones interactivos de las residencias en la Plaza Central y los Puentes (TASK-501 / TASK-1301).
    /// Permite leer cartas sin viajar hasta la granja y muestra la banderita / animación de correo pendiente.
    /// </summary>
    public sealed class TownMailboxModule : IModule
    {
        private static ModContext? context;

        public string Name => "TownMailbox";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;

            ctx.Helper.Events.Input.ButtonPressed += OnButtonPressed;
            ctx.Helper.Events.Display.RenderedWorld += OnRenderedWorld;

            ctx.Patch(
                "Farm.draw.MailboxIndicator",
                Name,
                PatchCriticality.Experience,
                AccessTools.Method(typeof(Farm), nameof(Farm.draw), new[] { typeof(SpriteBatch) }),
                transpiler: new HarmonyMethod(typeof(TownMailboxModule), nameof(Transpiler_FarmDrawMailboxIndicator)));
        }

        /// <summary>Casilla fuera de cualquier mapa: el indicador se dibuja fuera de pantalla.</summary>
        private static readonly Point HiddenMailboxPosition = new(-100, -100);

        /// <summary>
        /// <c>Farm.draw</c> pinta la carta de correo pendiente en <c>Game1.player.getMailboxPosition()</c> en TODAS las
        /// ubicaciones de tipo <see cref="Farm"/>. Las residencias y anexos también son <see cref="Farm"/>, y como el
        /// jugador no tiene cabaña en la Granja Host, esa posición cae en el buzón del host: el bocadillo aparecía en
        /// mitad de la residencia. Se sustituye la llamada por <see cref="GetMailboxIndicatorPosition"/>.
        /// </summary>
        public static IEnumerable<CodeInstruction> Transpiler_FarmDrawMailboxIndicator(IEnumerable<CodeInstruction> instructions)
        {
            var original = AccessTools.Method(typeof(Farmer), nameof(Farmer.getMailboxPosition));
            var replacement = AccessTools.Method(typeof(TownMailboxModule), nameof(GetMailboxIndicatorPosition));
            bool patched = false;

            foreach (CodeInstruction instruction in instructions)
            {
                if (!patched && instruction.Calls(original))
                {
                    // Pila: [farmer] → [farmer, this] → GetMailboxIndicatorPosition(farmer, farm)
                    var loadThis = new CodeInstruction(OpCodes.Ldarg_0);
                    loadThis.labels.AddRange(instruction.labels);
                    instruction.labels.Clear();
                    yield return loadThis;
                    yield return new CodeInstruction(OpCodes.Call, replacement) { blocks = instruction.blocks };
                    patched = true;
                    continue;
                }
                yield return instruction;
            }

            if (!patched)
                ModContext.Current?.Monitor.Log("[TownMailbox] Farm.draw no contiene getMailboxPosition; el indicador de correo vanilla queda sin corregir.", LogLevel.Warn);
        }

        /// <summary>
        /// Dónde dibujar el indicador de correo pendiente de <paramref name="who"/> en <paramref name="farm"/>:
        /// en su propia residencia, sobre el buzón de su casa; en la Granja Host, el comportamiento vanilla solo si
        /// es el host o tiene una cabaña allí (el buzón de su residencia ya lo pinta <see cref="OnRenderedWorld"/>);
        /// en cualquier otra granja (residencias ajenas, anexos), oculto.
        /// </summary>
        public static Point GetMailboxIndicatorPosition(Farmer who, Farm farm)
        {
            try
            {
                if (farm == Game1.getFarm())
                {
                    if (who.IsMainPlayer)
                        return who.getMailboxPosition();

                    foreach (Building building in farm.buildings)
                    {
                        if (building.isCabin && building.HasIndoorsName(who.homeLocation.Value))
                            return building.getMailboxPosition();
                    }
                    return HiddenMailboxPosition;
                }

                string farmName = farm.NameOrUniqueName;
                // Su casa ({slot}_Home) es el interior del Farmhouse de la residencia; el estado de puertas cubre
                // el caso de que el interior aún no esté enlazado en este cliente.
                bool ownResidence = farm.buildings.Any(b => b.HasIndoorsName(who.homeLocation.Value))
                    || ModContext.Current?.WorldState.Doors.Values.Any(d =>
                        d.IsResident(who.UniqueMultiplayerID)
                        && string.Equals(d.FarmSlotId, farmName, StringComparison.OrdinalIgnoreCase)) == true;

                return ownResidence ? farm.GetMainMailboxPosition() : HiddenMailboxPosition;
            }
            catch
            {
                return HiddenMailboxPosition;
            }
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady || context == null)
                return;

            if (!e.Button.IsActionButton())
                return;

            if (Game1.currentLocation == null)
                return;

            // Con un menú abierto (p. ej. el propio diálogo del buzón) el botón de acción solo responde a ese menú.
            // Sin esta guarda, la pulsación que elige "Entrar a mi granja" también reabría el menú del buzón
            // (QuestionDialog.Ask lo difiere hasta cerrar el diálogo), mostrando un segundo menú tras el warp.
            if (Game1.activeClickableMenu != null || Game1.dialogueUp || Game1.eventUp || Game1.player.UsingTool)
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
                if (!FarmSlotModule.IsDoorActiveForCurrentConfig(door))
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

            long myId = Game1.player.UniqueMultiplayerID;
            string doorDesc = FarmSlotModule.GetDoorDescription(door);
            string resolvedOwner = !string.IsNullOrWhiteSpace(ownerName) ? ownerName : I18n.Get("common.neighbor");
            var eval = TownMailboxLogic.EvaluateAccess(
                myId,
                ownerId,
                assignment?.TenantId,
                ownerName,
                Game1.mailbox.Count
            );

            if (eval.Status == MailboxAccessStatus.Owner && assignment != null)
            {
                int pendingCount = MailOrderModule.CountPendingOrdersFor(myId);
                string tenancyLabel = BuildTenancyMenuLabel(assignment);

                Response[] choices = new[]
                {
                    new Response("Enter", I18n.Get("mailbox.menu.enter")),
                    new Response("Mailbox", Game1.mailbox.Count > 0
                        ? I18n.Get("mailbox.menu.check_mail", new { count = Game1.mailbox.Count })
                        : I18n.Get("mailbox.menu.check_mail_empty")),
                    new Response("ShopOrders", pendingCount > 0
                        ? I18n.Get("mailbox.menu.shop_orders_pending", new { count = pendingCount })
                        : I18n.Get("mailbox.menu.shop_orders")),
                    new Response("PendingStatus", I18n.Get("mailbox.menu.view_pending", new { count = pendingCount })),
                    new Response("Policy", I18n.Get("mailbox.menu.policy", new { status = VisitorPermissionLogic.Describe(assignment.Policy, assignment.VisitorsCanEdit) })),
                    new Response("Tenancy", tenancyLabel),
                    new Response("Cancel", I18n.Get("dialog.cancel"))
                };

                QuestionDialog.Ask(
                    I18n.Get("mailbox.owner_prompt", new { description = doorDesc }),
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
                        else if (whichAnswer == "ShopOrders")
                        {
                            MailOrderModule.OpenShopCatalogMenu();
                        }
                        else if (whichAnswer == "PendingStatus")
                        {
                            MailOrderModule.ShowPendingOrdersDialog();
                        }
                        else if (whichAnswer == "Policy")
                        {
                            FarmSlotModule.ShowPolicySelectionDialog(door.DefaultSlotId);
                        }
                        else if (whichAnswer == "Tenancy")
                        {
                            ShowTenancyManagementDialog(door, assignment);
                        }
                    }
                );
            }
            else if (eval.Status == MailboxAccessStatus.Tenant && assignment != null)
            {
                int pendingCount = MailOrderModule.CountPendingOrdersFor(myId);
                Response[] choices = new[]
                {
                    new Response("Enter", I18n.Get("mailbox.menu.enter")),
                    new Response("Mailbox", Game1.mailbox.Count > 0
                        ? I18n.Get("mailbox.menu.check_mail", new { count = Game1.mailbox.Count })
                        : I18n.Get("mailbox.menu.check_mail_empty")),
                    new Response("ShopOrders", pendingCount > 0
                        ? I18n.Get("mailbox.menu.shop_orders_pending", new { count = pendingCount })
                        : I18n.Get("mailbox.menu.shop_orders")),
                    new Response("PendingStatus", I18n.Get("mailbox.menu.view_pending", new { count = pendingCount })),
                    new Response("LeaveTenancy", I18n.Get("mailbox.menu.leave_tenancy")),
                    new Response("Cancel", I18n.Get("dialog.cancel"))
                };

                QuestionDialog.Ask(
                    I18n.Get("mailbox.tenant_prompt", new { description = doorDesc, owner = resolvedOwner }),
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
                        else if (whichAnswer == "ShopOrders")
                        {
                            MailOrderModule.OpenShopCatalogMenu();
                        }
                        else if (whichAnswer == "PendingStatus")
                        {
                            MailOrderModule.ShowPendingOrdersDialog();
                        }
                        else if (whichAnswer == "LeaveTenancy")
                        {
                            ConfirmLeaveTenancy(door);
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
                if (assignment != null)
                {
                    bool canVisit = VisitorPermissionLogic.CanEnter(assignment, myId);
                    bool canRequestTenancy = (Game1.MasterPlayer == null || myId != Game1.MasterPlayer.UniqueMultiplayerID)
                        && (!assignment.TenantId.HasValue || assignment.TenantId.Value == 0)
                        && context.WorldState.FindResidenceOf(myId) == null;
                    bool alreadyRequested = assignment.PendingTenantRequests?.Contains(myId) == true;

                    var choices = new List<Response>();
                    if (canVisit)
                        choices.Add(new Response("Visit", I18n.Get("mailbox.menu.visit", new { owner = resolvedOwner })));

                    if (canRequestTenancy)
                    {
                        choices.Add(alreadyRequested
                            ? new Response("CancelTenancy", I18n.Get("mailbox.menu.cancel_tenancy_request"))
                            : new Response("RequestTenancy", I18n.Get("mailbox.menu.request_tenancy")));
                    }

                    if (choices.Count > 0)
                    {
                        choices.Add(new Response("Cancel", I18n.Get("dialog.cancel")));
                        string prompt = canVisit
                            ? I18n.Get("mailbox.visit_prompt", new { description = doorDesc, owner = resolvedOwner, access = VisitorPermissionLogic.Describe(assignment.Policy, assignment.VisitorsCanEdit) })
                            : I18n.Get("mailbox.tenancy_visitor_prompt", new { description = doorDesc, owner = resolvedOwner });

                        QuestionDialog.Ask(
                            prompt,
                            choices.ToArray(),
                            (who, whichAnswer) =>
                            {
                                if (whichAnswer == "Visit")
                                {
                                    FarmSlotModule.WarpToFarm(door.DefaultSlotId);
                                }
                                else if (whichAnswer == "RequestTenancy")
                                {
                                    SendRequestTenancy(door.DoorId, cancel: false);
                                    Game1.addHUDMessage(new HUDMessage(I18n.Get("mailbox.tenancy.request_sent", new { owner = resolvedOwner }), HUDMessage.newQuest_type));
                                }
                                else if (whichAnswer == "CancelTenancy")
                                {
                                    SendRequestTenancy(door.DoorId, cancel: true);
                                    Game1.addHUDMessage(new HUDMessage(I18n.Get("mailbox.tenancy.request_cancelled"), HUDMessage.newQuest_type));
                                }
                            }
                        );
                        return;
                    }
                }

                Game1.drawObjectDialogue(I18n.Get("mailbox.restricted", new { owner = resolvedOwner }));
            }
        }

        public static string BuildTenancyMenuLabel(DoorAssignment assignment)
        {
            if (assignment.TenantId.HasValue && assignment.TenantId.Value != 0)
                return I18n.Get("mailbox.menu.tenancy_active", new { tenant = ResolvePlayerName(assignment.TenantId.Value) });

            int reqCount = assignment.PendingTenantRequests?.Count ?? 0;
            return reqCount > 0
                ? I18n.Get("mailbox.menu.tenancy_requests", new { count = reqCount })
                : I18n.Get("mailbox.menu.tenancy");
        }

        public static void ShowTenancyManagementDialog(DoorModel door, DoorAssignment assignment)
        {
            if (context == null)
                return;

            if (context.WorldState.Doors.TryGetValue(door.DoorId, out DoorAssignment? latest))
                assignment = latest;

            if (assignment.TenantId.HasValue && assignment.TenantId.Value != 0)
            {
                long activeTenantId = assignment.TenantId.Value;
                string tenantName = ResolvePlayerName(activeTenantId);
                Response[] activeChoices = new[]
                {
                    new Response("Evict", I18n.Get("mailbox.tenancy.evict", new { tenant = tenantName })),
                    new Response("Cancel", I18n.Get("dialog.cancel"))
                };

                QuestionDialog.Ask(
                    I18n.Get("mailbox.tenancy.active_prompt", new { tenant = tenantName }),
                    activeChoices,
                    (who, answer) =>
                    {
                        if (answer == "Evict")
                        {
                            QuestionDialog.Ask(
                                I18n.Get("mailbox.tenancy.evict_confirm", new { tenant = tenantName }),
                                new[]
                                {
                                    new Response("Confirm", I18n.Get("common.yes")),
                                    new Response("Cancel", I18n.Get("common.no"))
                                },
                                (_, confirm) =>
                                {
                                    if (confirm == "Confirm")
                                    {
                                        SendManageTenant(door.DoorId, TenantManageAction.Evict, activeTenantId);
                                        Game1.addHUDMessage(new HUDMessage(I18n.Get("mailbox.tenancy.evicted", new { tenant = tenantName }), HUDMessage.newQuest_type));
                                    }
                                }
                            );
                        }
                    }
                );
                return;
            }

            long? masterId = Game1.MasterPlayer?.UniqueMultiplayerID;
            var candidateIds = new HashSet<long>();
            var entries = new List<KeyValuePair<string, string>>();

            if (assignment.PendingTenantRequests != null)
            {
                foreach (long reqId in assignment.PendingTenantRequests)
                {
                    if (reqId != 0
                        && reqId != assignment.OwnerId
                        && (!masterId.HasValue || reqId != masterId.Value)
                        && context.WorldState.FindResidenceOf(reqId) == null)
                    {
                        candidateIds.Add(reqId);
                        entries.Add(new KeyValuePair<string, string>(
                            $"Applicant_{reqId}",
                            I18n.Get("mailbox.tenancy.applicant_entry", new { name = ResolvePlayerName(reqId) })
                        ));
                    }
                }
            }

            foreach (Farmer online in Game1.getOnlineFarmers())
            {
                long id = online.UniqueMultiplayerID;
                if (id != 0
                    && id != assignment.OwnerId
                    && (!masterId.HasValue || id != masterId.Value)
                    && !candidateIds.Contains(id)
                    && context.WorldState.FindResidenceOf(id) == null)
                {
                    entries.Add(new KeyValuePair<string, string>(
                        $"Invite_{id}",
                        I18n.Get("mailbox.tenancy.invite_online", new { name = online.Name })
                    ));
                }
            }

            if (entries.Count == 0)
            {
                Game1.drawObjectDialogue(I18n.Get("mailbox.tenancy.none_available"));
                return;
            }

            string managePrompt = I18n.Get("mailbox.tenancy.manage_prompt");
            if (entries.Count <= 8)
            {
                var choices = entries.Select(e => new Response(e.Key, e.Value)).ToList();
                choices.Add(new Response("Cancel", I18n.Get("dialog.cancel")));
                QuestionDialog.Ask(
                    managePrompt,
                    choices.ToArray(),
                    (_, answer) => HandleTenancyCandidateSelection(door, answer)
                );
                return;
            }

            ShowPagedResponsesDeferred(managePrompt, entries, answer => HandleTenancyCandidateSelection(door, answer));
        }

        private static void ShowPagedResponsesDeferred(string prompt, List<KeyValuePair<string, string>> options, Action<string> onSelect)
        {
            void Open() => Game1.currentLocation?.ShowPagedResponses(prompt, options, onSelect, auto_select_single_choice: false);

            if (Game1.activeClickableMenu is not StardewValley.Menus.DialogueBox)
            {
                Open();
                return;
            }

            Game1.afterFadeFunction? previous = Game1.afterDialogues;
            Game1.afterDialogues = () =>
            {
                previous?.Invoke();
                Open();
            };
        }

        private static void HandleTenancyCandidateSelection(DoorModel door, string answer)
        {
            if (string.IsNullOrEmpty(answer))
                return;

            if (answer.StartsWith("Applicant_", StringComparison.Ordinal) && long.TryParse(answer.Substring("Applicant_".Length), out long applicantId))
            {
                string applicantName = ResolvePlayerName(applicantId);
                QuestionDialog.Ask(
                    I18n.Get("mailbox.tenancy.applicant_confirm", new { name = applicantName }),
                    new[]
                    {
                        new Response("Accept", I18n.Get("mailbox.tenancy.accept")),
                        new Response("Reject", I18n.Get("mailbox.tenancy.reject")),
                        new Response("Cancel", I18n.Get("dialog.cancel"))
                    },
                    (_, decision) =>
                    {
                        if (decision == "Accept")
                        {
                            SendManageTenant(door.DoorId, TenantManageAction.Accept, applicantId);
                            Game1.addHUDMessage(new HUDMessage(I18n.Get("mailbox.tenancy.accepted", new { name = applicantName }), HUDMessage.newQuest_type));
                        }
                        else if (decision == "Reject")
                        {
                            SendManageTenant(door.DoorId, TenantManageAction.Reject, applicantId);
                            Game1.addHUDMessage(new HUDMessage(I18n.Get("mailbox.tenancy.rejected", new { name = applicantName }), HUDMessage.newQuest_type));
                        }
                    }
                );
            }
            else if (answer.StartsWith("Invite_", StringComparison.Ordinal) && long.TryParse(answer.Substring("Invite_".Length), out long inviteId))
            {
                string inviteName = ResolvePlayerName(inviteId);
                QuestionDialog.Ask(
                    I18n.Get("mailbox.tenancy.applicant_confirm", new { name = inviteName }),
                    new[]
                    {
                        new Response("Accept", I18n.Get("mailbox.tenancy.accept")),
                        new Response("Cancel", I18n.Get("dialog.cancel"))
                    },
                    (_, decision) =>
                    {
                        if (decision == "Accept")
                        {
                            SendManageTenant(door.DoorId, TenantManageAction.Accept, inviteId);
                            Game1.addHUDMessage(new HUDMessage(I18n.Get("mailbox.tenancy.accepted", new { name = inviteName }), HUDMessage.newQuest_type));
                        }
                    }
                );
            }
        }

        public static void ConfirmLeaveTenancy(DoorModel door)
        {
            QuestionDialog.Ask(
                I18n.Get("mailbox.tenancy.leave_confirm"),
                new[]
                {
                    new Response("Confirm", I18n.Get("common.yes")),
                    new Response("Cancel", I18n.Get("common.no"))
                },
                (who, answer) =>
                {
                    if (answer == "Confirm")
                    {
                        SendManageTenant(door.DoorId, TenantManageAction.Leave, Game1.player.UniqueMultiplayerID);
                        Game1.addHUDMessage(new HUDMessage(I18n.Get("mailbox.tenancy.left"), HUDMessage.newQuest_type));
                    }
                }
            );
        }

        private static void SendRequestTenancy(string doorId, bool cancel)
        {
            if (context == null)
                return;

            long myId = Game1.player.UniqueMultiplayerID;
            if (Context.IsMainPlayer)
            {
                FarmSlotModule.HandleRequestTenancy(myId, doorId, cancel);
            }
            else
            {
                context.Helper.Multiplayer.SendMessage(
                    new RequestTenancyPayload { DoorId = doorId, Cancel = cancel },
                    ModMessages.RequestTenancyRequest,
                    new[] { context.Manifest.UniqueID },
                    new[] { Game1.MasterPlayer.UniqueMultiplayerID }
                );
            }
        }

        private static void SendManageTenant(string doorId, TenantManageAction action, long targetPlayerId)
        {
            if (context == null)
                return;

            long myId = Game1.player.UniqueMultiplayerID;
            if (Context.IsMainPlayer)
            {
                FarmSlotModule.HandleManageTenant(myId, doorId, action, targetPlayerId);
            }
            else
            {
                context.Helper.Multiplayer.SendMessage(
                    new ManageTenantPayload { DoorId = doorId, Action = action, TargetPlayerId = targetPlayerId },
                    ModMessages.ManageTenantRequest,
                    new[] { context.Manifest.UniqueID },
                    new[] { Game1.MasterPlayer.UniqueMultiplayerID }
                );
            }
        }

        private static string ResolvePlayerName(long playerId)
        {
            if (context != null && context.WorldState.Players.TryGetValue(playerId, out var rec) && !string.IsNullOrWhiteSpace(rec.PlayerName))
                return rec.PlayerName;

            Farmer? f = Game1.GetPlayer(playerId);
            return !string.IsNullOrWhiteSpace(f?.Name) ? f.Name : playerId.ToString();
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

                if (context.WorldState.Doors.TryGetValue(door.DoorId, out DoorAssignment? assignment) && assignment.IsResident(myId))
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
