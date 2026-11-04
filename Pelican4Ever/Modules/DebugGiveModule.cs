using System;
using System.Collections.Generic;
using System.Linq;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Comandos de testeo solo para el host: <c>p4e_give_gold</c> y <c>p4e_give_item</c> (consola SMAPI sin barra,
    /// chat del juego con barra). Sin jugador indicado afectan al host.
    /// </summary>
    /// <remarks>
    /// Cada cliente es el dueño de su propio <see cref="Farmer"/>: si el host le cambia el inventario o el dinero,
    /// el cliente lo sobrescribe. Por eso, para un jugador conectado, el host le envía un mensaje de mod y es el
    /// propio cliente quien se aplica el cambio y responde con el resultado. Un farmhand desconectado sí se edita
    /// directamente en el host, que es quien guarda su estado mientras no está.
    /// </remarks>
    public sealed class DebugGiveModule : IModule
    {
        private const string MsgGive = "DebugGive.Give";
        private const string MsgResult = "DebugGive.Result";

        private ModContext context = null!;
        private ChatBox? replyChat;

        public string Name => "DebugGive";

        public ModuleSide Side => ModuleSide.Both;

        public void Register(ModContext ctx)
        {
            context = ctx;
            ctx.Helper.ConsoleCommands.Add(
                "p4e_give_gold",
                "Da (o quita, si es negativo) oro: p4e_give_gold <cantidad> [jugador_nombre_o_id]",
                (cmd, args) => Run(() => OnGiveGold(args)));
            ctx.Helper.ConsoleCommands.Add(
                "p4e_give_item",
                "Da un item: p4e_give_item <id_o_nombre> [cantidad=1] [jugador_nombre_o_id]  (id p.ej. 74 o (O)74; nombre con _ por espacios, p.ej. Prismatic_Shard)",
                (cmd, args) => Run(() => OnGiveItem(args)));

            ctx.Helper.Events.GameLoop.SaveLoaded += (_, _) => RegisterChatCommands();
            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
        }

        // ---------- Registro y salida ----------

        private void RegisterChatCommands()
        {
            try
            {
                if (!ChatCommands.Exists("p4e_give_gold"))
                    ChatCommands.Register("p4e_give_gold", (cmd, chat) => RunFromChat(chat, () => OnGiveGold(cmd.Skip(1).ToArray())), _ => "Da oro: /p4e_give_gold <cantidad> [jugador]", null, mainOnly: true, multiplayerOnly: false, cheatsOnly: false);
                if (!ChatCommands.Exists("p4e_give_item"))
                    ChatCommands.Register("p4e_give_item", (cmd, chat) => RunFromChat(chat, () => OnGiveItem(cmd.Skip(1).ToArray())), _ => "Da un item: /p4e_give_item <id_o_nombre> [cantidad] [jugador]", null, mainOnly: true, multiplayerOnly: false, cheatsOnly: false);
            }
            catch (Exception ex)
            {
                context.Monitor.Log($"[DebugGive] Error registrando comandos de chat: {ex.Message}", LogLevel.Warn);
            }
        }

        private void RunFromChat(ChatBox chat, Action action)
        {
            replyChat = chat;
            try { Run(action); }
            finally { replyChat = null; }
        }

        private void Run(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Say($"[DebugGive] Error: {ex.Message}", LogLevel.Error);
                context.Monitor.Log(ex.ToString(), LogLevel.Trace);
            }
        }

        private void Say(string message, LogLevel level)
        {
            context.Monitor.Log(message, level);
            replyChat?.addInfoMessage(message);
        }

        // ---------- Comandos (host) ----------

        private void OnGiveGold(string[] args)
        {
            if (!CheckHostWorld())
                return;

            if (args.Length == 0 || !int.TryParse(args[0], out int amount))
            {
                Say("Uso: p4e_give_gold <cantidad> [jugador_nombre_o_id]", LogLevel.Warn);
                return;
            }

            Farmer? target = ResolveTarget(args.Skip(1).ToArray());
            if (target == null)
                return;

            var payload = new GivePayload { Gold = amount };
            if (IsRemoteOnline(target))
            {
                SendToClient(target, payload);
                return;
            }

            Say(ApplyGold(target, amount), LogLevel.Info);
        }

        private void OnGiveItem(string[] args)
        {
            if (!CheckHostWorld())
                return;

            if (args.Length == 0)
            {
                Say("Uso: p4e_give_item <id_o_nombre> [cantidad=1] [jugador_nombre_o_id]", LogLevel.Warn);
                return;
            }

            int count = 1;
            int rest = 1;
            if (args.Length > 1 && int.TryParse(args[1], out int parsed))
            {
                count = parsed;
                rest = 2;
            }

            if (count < 1)
            {
                Say("La cantidad debe ser >= 1.", LogLevel.Warn);
                return;
            }

            string? itemId = ResolveItemId(args[0]);
            if (itemId == null)
            {
                Say($"Item no encontrado: '{args[0]}'.", LogLevel.Error);
                return;
            }

            Farmer? target = ResolveTarget(args.Skip(rest).ToArray());
            if (target == null)
                return;

            var payload = new GivePayload { ItemId = itemId, Count = count };
            if (IsRemoteOnline(target))
            {
                SendToClient(target, payload);
                return;
            }

            // Host local: lo que no quepa cae a sus pies. Farmhand desconectado: solo lo que quepa.
            Say(ApplyItem(target, itemId, count, dropOverflow: target.IsLocalPlayer), LogLevel.Info);
        }

        private void SendToClient(Farmer target, GivePayload payload)
        {
            context.Helper.Multiplayer.SendMessage(
                payload,
                MsgGive,
                new[] { context.Manifest.UniqueID },
                new[] { target.UniqueMultiplayerID });
            Say($"Orden enviada al cliente de {target.Name}; esperando confirmación...", LogLevel.Info);
        }

        // ---------- Mensajes ----------

        private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (e.FromModID != context.Manifest.UniqueID)
                return;

            switch (e.Type)
            {
                // Cliente: solo acepta órdenes del host.
                case MsgGive when !Context.IsMainPlayer && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID:
                {
                    GivePayload p = e.ReadAs<GivePayload>();
                    string result;
                    try
                    {
                        result = p.ItemId != null
                            ? ApplyItem(Game1.player, p.ItemId, p.Count, dropOverflow: true)
                            : ApplyGold(Game1.player, p.Gold);
                    }
                    catch (Exception ex)
                    {
                        result = $"Error en el cliente de {Game1.player.Name}: {ex.Message}";
                    }

                    context.Monitor.Log($"[DebugGive] {result}", LogLevel.Info);
                    Game1.addHUDMessage(new HUDMessage(result, HUDMessage.newQuest_type));
                    context.Helper.Multiplayer.SendMessage(
                        new GiveResultPayload { Message = result },
                        MsgResult,
                        new[] { context.Manifest.UniqueID },
                        new[] { Game1.MasterPlayer.UniqueMultiplayerID });
                    break;
                }

                // Host: confirmación del cliente.
                case MsgResult when Context.IsMainPlayer:
                {
                    string message = e.ReadAs<GiveResultPayload>().Message;
                    context.Monitor.Log($"[DebugGive] {message}", LogLevel.Info);
                    Game1.chatBox?.addInfoMessage(message);
                    break;
                }
            }
        }

        // ---------- Aplicación (en quien sea dueño del Farmer) ----------

        private static string ApplyGold(Farmer target, int amount)
        {
            int newValue = Math.Max(0, target.Money + amount);
            target.Money = newValue;
            return $"{target.Name}: {(amount >= 0 ? "+" : string.Empty)}{amount}g -> ahora {target.Money}g.";
        }

        private static string ApplyItem(Farmer target, string itemId, int count, bool dropOverflow)
        {
            Item sample = ItemRegistry.Create(itemId, 1);
            string label = sample.DisplayName;
            int maxStack = Math.Max(1, sample.maximumStackSize());

            int remaining = count;
            int dropped = 0;
            while (remaining > 0)
            {
                int chunk = Math.Min(remaining, maxStack);
                Item item = ItemRegistry.Create(itemId, chunk);
                int left = AddToInventory(target, item);
                remaining -= chunk - left;

                if (left > 0)
                {
                    if (!dropOverflow)
                        break;

                    Game1.createItemDebris(item, target.getStandingPosition(), -1, target.currentLocation);
                    dropped += left;
                    remaining -= left;
                }
            }

            int inInventory = count - remaining - dropped;
            string text = $"{target.Name} recibe {label} x{count - remaining} ({itemId})";
            if (dropped > 0)
                text += $"; inventario lleno, {dropped} en el suelo";
            if (remaining > 0)
                text += $"; inventario lleno, {remaining} sin entregar";
            return text + (inInventory != count ? "." : " en el inventario.");
        }

        /// <summary>Apila sobre stacks existentes y luego rellena huecos. Devuelve las unidades que no cupieron.</summary>
        private static int AddToInventory(Farmer target, Item item)
        {
            var items = target.Items;
            int max = Math.Min(items.Count, target.MaxItems);

            for (int i = 0; i < max && item.Stack > 0; i++)
            {
                Item? slot = items[i];
                if (slot != null && slot.canStackWith(item))
                {
                    int move = Math.Min(slot.maximumStackSize() - slot.Stack, item.Stack);
                    if (move > 0)
                    {
                        slot.Stack += move;
                        item.Stack -= move;
                    }
                }
            }

            for (int i = 0; i < max && item.Stack > 0; i++)
            {
                if (items[i] == null)
                {
                    items[i] = item;
                    return 0;
                }
            }

            return item.Stack;
        }

        // ---------- Utilidades ----------

        private bool CheckHostWorld()
        {
            if (!Context.IsWorldReady)
            {
                Say("Partida no cargada.", LogLevel.Warn);
                return false;
            }

            if (!Context.IsMainPlayer)
            {
                Say("Este comando solo puede ejecutarse en el Host.", LogLevel.Warn);
                return false;
            }

            return true;
        }

        private static bool IsRemoteOnline(Farmer target)
        {
            return !target.IsLocalPlayer
                && Game1.getOnlineFarmers().Any(f => f.UniqueMultiplayerID == target.UniqueMultiplayerID);
        }

        /// <summary>Sin argumentos devuelve el host; con argumentos busca por id o nombre (conectados primero).</summary>
        private Farmer? ResolveTarget(string[] args)
        {
            if (args.Length == 0)
                return Game1.player;

            string text = string.Join(" ", args).Trim();

            // Conectados primero: así se usa la instancia viva del farmer y no la copia guardada.
            List<Farmer> all = Game1.getOnlineFarmers()
                .Concat(Game1.getAllFarmers())
                .GroupBy(f => f.UniqueMultiplayerID)
                .Select(g => g.First())
                .ToList();

            Farmer? found = null;
            if (long.TryParse(text, out long id))
                found = all.FirstOrDefault(f => f.UniqueMultiplayerID == id);
            found ??= all.FirstOrDefault(f => string.Equals(f.Name, text, StringComparison.OrdinalIgnoreCase));
            found ??= all.FirstOrDefault(f => f.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase));

            if (found == null)
                Say($"Jugador no encontrado: '{text}'. Disponibles: {string.Join(", ", all.Select(f => $"{f.Name} ({f.UniqueMultiplayerID})"))}", LogLevel.Error);
            return found;
        }

        private static string? ResolveItemId(string input)
        {
            string text = input.Replace('_', ' ').Trim();

            // Id cualificado "(O)74" o id simple "74".
            if (text.StartsWith("(", StringComparison.Ordinal) && ItemRegistry.GetData(text) != null)
                return text;
            if (ItemRegistry.GetData("(O)" + text) != null)
                return "(O)" + text;

            // Por nombre (interno o traducido) entre los objetos.
            var match = ItemRegistry.GetObjectTypeDefinition().GetAllIds()
                .Select(i => ItemRegistry.GetData("(O)" + i))
                .FirstOrDefault(d => d != null
                    && (string.Equals(d.InternalName, text, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(d.DisplayName, text, StringComparison.OrdinalIgnoreCase)));
            return match?.QualifiedItemId;
        }

        /// <summary>Orden del host al cliente: oro si <see cref="ItemId"/> es null; si no, item.</summary>
        public sealed class GivePayload
        {
            public int Gold { get; set; }
            public string? ItemId { get; set; }
            public int Count { get; set; }
        }

        public sealed class GiveResultPayload
        {
            public string Message { get; set; } = string.Empty;
        }
    }
}
