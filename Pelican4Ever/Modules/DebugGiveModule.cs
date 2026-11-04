using System;
using System.Collections.Generic;
using System.Linq;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Comandos de consola de testeo solo para el host: <c>p4e_give_gold</c> y <c>p4e_give_item</c>.
    /// Sin jugador indicado afectan al host; con jugador (nombre o id) afectan a ese farmer.
    /// </summary>
    public sealed class DebugGiveModule : IModule
    {
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
                OnGiveGold);
            ctx.Helper.ConsoleCommands.Add(
                "p4e_give_item",
                "Da un item: p4e_give_item <id_o_nombre> [cantidad=1] [jugador_nombre_o_id]  (id p.ej. 74 o (O)74; nombre con _ por espacios, p.ej. Prismatic_Shard)",
                OnGiveItem);

            ctx.Helper.Events.GameLoop.SaveLoaded += (_, _) => RegisterChatCommands();
        }

        private void RegisterChatCommands()
        {
            try
            {
                if (!ChatCommands.Exists("p4e_give_gold"))
                    ChatCommands.Register("p4e_give_gold", (cmd, chat) => RunFromChat(chat, cmd, OnGiveGold), _ => "Da oro: /p4e_give_gold <cantidad> [jugador]", null, mainOnly: true, multiplayerOnly: false, cheatsOnly: false);
                if (!ChatCommands.Exists("p4e_give_item"))
                    ChatCommands.Register("p4e_give_item", (cmd, chat) => RunFromChat(chat, cmd, OnGiveItem), _ => "Da un item: /p4e_give_item <id_o_nombre> [cantidad] [jugador]", null, mainOnly: true, multiplayerOnly: false, cheatsOnly: false);
            }
            catch (Exception ex)
            {
                context.Monitor.Log($"[DebugGive] Error registrando comandos de chat: {ex.Message}", LogLevel.Warn);
            }
        }

        private void RunFromChat(ChatBox chat, string[] cmd, Action<string, string[]> handler)
        {
            replyChat = chat;
            try { handler(cmd[0], cmd.Skip(1).ToArray()); }
            finally { replyChat = null; }
        }

        private void Say(string message, LogLevel level)
        {
            context.Monitor.Log(message, level);
            replyChat?.addInfoMessage(message);
        }

        private void OnGiveGold(string command, string[] args)
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

            target.Money = Math.Max(0, target.Money + amount);
            Say($"{target.Name}: {(amount >= 0 ? "+" : string.Empty)}{amount}g -> ahora {target.Money}g.", LogLevel.Info);
        }

        private void OnGiveItem(string command, string[] args)
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

            Item sample = ItemRegistry.Create(itemId, 1);
            string label = sample.DisplayName;
            int remaining = count;
            while (remaining > 0)
            {
                int chunk = Math.Min(remaining, Math.Max(1, sample.maximumStackSize()));
                Item item = ItemRegistry.Create(itemId, chunk);
                int before = item.Stack;
                int left = AddToInventory(target, item);
                remaining -= before - left;
                if (left > 0)
                    break;
            }

            int given = count - remaining;
            if (remaining > 0)
                Say($"Inventario de {target.Name} lleno: entregado {label} x{given} de {count}.", LogLevel.Warn);
            else
                Say($"{target.Name} recibe {label} x{given} ({itemId}).", LogLevel.Info);
        }

        /// <summary>Inserta en el inventario de cualquier farmer (local o remoto). Devuelve las unidades que no cupieron.</summary>
        private static int AddToInventory(Farmer target, Item item)
        {
            var items = target.Items;
            int max = Math.Min(items.Count, target.MaxItems);

            for (int i = 0; i < max && item.Stack > 0; i++)
            {
                Item? slot = items[i];
                if (slot != null && slot.canStackWith(item))
                {
                    int space = slot.maximumStackSize() - slot.Stack;
                    int move = Math.Min(space, item.Stack);
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

        /// <summary>Sin argumentos devuelve el host; con argumentos busca por id o nombre (conectado o no).</summary>
        private Farmer? ResolveTarget(string[] args)
        {
            if (args.Length == 0)
                return Game1.player;

            string text = string.Join(" ", args).Trim();
            List<Farmer> all = Game1.getAllFarmers().ToList();

            Farmer? found = null;
            if (long.TryParse(text, out long id))
                found = all.FirstOrDefault(f => f.UniqueMultiplayerID == id);
            found ??= all.FirstOrDefault(f => string.Equals(f.Name, text, StringComparison.OrdinalIgnoreCase));

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
    }
}
