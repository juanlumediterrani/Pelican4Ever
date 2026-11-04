using System;
using System.Collections.Generic;
using System.Linq;
using Pelican4Ever.Framework;
using StardewModdingAPI;
using StardewValley;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Comandos de consola de testeo solo para el host: <c>p4e_give_gold</c> y <c>p4e_give_item</c>.
    /// Sin jugador indicado afectan al host; con jugador (nombre o id) afectan a ese farmer.
    /// </summary>
    public sealed class DebugGiveModule : IModule
    {
        private ModContext context = null!;

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
        }

        private void OnGiveGold(string command, string[] args)
        {
            if (!CheckHostWorld())
                return;

            if (args.Length == 0 || !int.TryParse(args[0], out int amount))
            {
                context.Monitor.Log("Uso: p4e_give_gold <cantidad> [jugador_nombre_o_id]", LogLevel.Warn);
                return;
            }

            Farmer? target = ResolveTarget(args.Skip(1).ToArray());
            if (target == null)
                return;

            target.Money = Math.Max(0, target.Money + amount);
            context.Monitor.Log($"{target.Name}: {(amount >= 0 ? "+" : string.Empty)}{amount}g -> ahora {target.Money}g.", LogLevel.Info);
        }

        private void OnGiveItem(string command, string[] args)
        {
            if (!CheckHostWorld())
                return;

            if (args.Length == 0)
            {
                context.Monitor.Log("Uso: p4e_give_item <id_o_nombre> [cantidad=1] [jugador_nombre_o_id]", LogLevel.Warn);
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
                context.Monitor.Log("La cantidad debe ser >= 1.", LogLevel.Warn);
                return;
            }

            string? itemId = ResolveItemId(args[0]);
            if (itemId == null)
            {
                context.Monitor.Log($"Item no encontrado: '{args[0]}'.", LogLevel.Error);
                return;
            }

            Farmer? target = ResolveTarget(args.Skip(rest).ToArray());
            if (target == null)
                return;

            Item item = ItemRegistry.Create(itemId, count);
            string label = item.DisplayName;
            if (!target.addItemToInventoryBool(item))
            {
                context.Monitor.Log($"Inventario de {target.Name} lleno; no se pudo dar {label} x{count}.", LogLevel.Warn);
                return;
            }

            context.Monitor.Log($"{target.Name} recibe {label} x{count} ({itemId}).", LogLevel.Info);
        }

        private bool CheckHostWorld()
        {
            if (!Context.IsWorldReady)
            {
                context.Monitor.Log("Partida no cargada.", LogLevel.Warn);
                return false;
            }

            if (!Context.IsMainPlayer)
            {
                context.Monitor.Log("Este comando solo puede ejecutarse en el Host.", LogLevel.Warn);
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
                context.Monitor.Log($"Jugador no encontrado: '{text}'. Disponibles: {string.Join(", ", all.Select(f => $"{f.Name} ({f.UniqueMultiplayerID})"))}", LogLevel.Error);
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
