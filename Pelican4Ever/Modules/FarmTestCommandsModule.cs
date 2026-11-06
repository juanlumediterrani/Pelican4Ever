using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.World;
using Pelican4Ever.Framework;
using Pelican4Ever.Models;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Monsters;
using StardewValley.Network;
using StardewValley.Objects;

namespace Pelican4Ever.Modules
{
    /// <summary>
    /// Comandos de prueba de residencias para el host (TASK-607): fundar con un tipo, teletransportar a sus entradas,
    /// inspeccionar, forzar apariciones, rayos y clima, y fijar niveles de habilidad. Solo funcionan con
    /// <c>EnableTestCommands</c> activado; cada uso queda en la consola y en el registro de sucesos.
    /// </summary>
    /// <remarks>
    /// Como en <see cref="DebugGiveModule"/>, cada cliente es dueño de su <see cref="Farmer"/>: el teletransporte y el
    /// nivel de un jugador conectado se envían a su cliente, que solo acepta órdenes del host.
    /// </remarks>
    public sealed class FarmTestCommandsModule : IModule
    {
        private const string MsgWarp = "FarmTest.Warp";
        private const string MsgSkill = "FarmTest.Skill";
        private const string MsgResult = "FarmTest.Result";

        private ModContext context = null!;
        private ChatBox? replyChat;

        public string Name => "FarmTestCommands";

        public ModuleSide Side => ModuleSide.Both;

        private static readonly (string Name, string Help, Action<FarmTestCommandsModule, string[]> Handler)[] Commands =
        {
            ("p4e_farm_found", "Funda o refunda una residencia: p4e_farm_found <hueco> <tipo> [jugador]. Tipos: Standard, Fishing, Foraging, Mining, Combat, FourCorners, Ranching, Beach. Regenera el mapa: borra lo que hubiera en el hueco.", (m, a) => m.OnFound(a)),
            ("p4e_farm_tp", "Teletransporta a una residencia o anexo: p4e_farm_tp <hueco|hueco/Lado|host> [jugador] [house|north|south|east|cave]", (m, a) => m.OnTeleport(a)),
            ("p4e_farm_inspect", "Informe de una residencia, anexo o la granja principal: p4e_farm_inspect <hueco|hueco/Lado|host>", (m, a) => m.OnInspect(a)),
            ("p4e_farm_spawn", "Fuerza apariciones: p4e_farm_spawn <hueco|hueco/Lado|host> <forage|ores|crate|monsters> [n=1]", (m, a) => m.OnSpawn(a)),
            ("p4e_lightning", "Hace caer n rayos seguros (sin tope diario): p4e_lightning <hueco|hueco/Lado|host> [n=1]", (m, a) => m.OnLightning(a)),
            ("p4e_weather", "Fija el clima del valle: p4e_weather <sun|rain|storm> [today|tomorrow]", (m, a) => m.OnWeather(a)),
            ("p4e_skill", "Fija un nivel de habilidad: p4e_skill <jugador> <farming|fishing|foraging|mining|combat|luck> <0-10>", (m, a) => m.OnSkill(a)),
        };

        public void Register(ModContext ctx)
        {
            context = ctx;
            foreach (var command in Commands)
                ctx.Helper.ConsoleCommands.Add(command.Name, command.Help, (_, args) => Run(command.Name, args, command.Handler));

            ctx.Helper.Events.GameLoop.SaveLoaded += (_, _) => RegisterChatCommands();
            ctx.Helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
        }

        // ---------- Registro y salida ----------

        private void RegisterChatCommands()
        {
            try
            {
                foreach (var command in Commands)
                {
                    if (!ChatCommands.Exists(command.Name))
                    {
                        var c = command;
                        ChatCommands.Register(c.Name, (cmd, chat) => RunFromChat(chat, c.Name, cmd.Skip(1).ToArray(), c.Handler), _ => c.Help, null, mainOnly: true, multiplayerOnly: false, cheatsOnly: false);
                    }
                }
            }
            catch (Exception ex)
            {
                context.Monitor.Log($"[FarmTest] Error registrando comandos de chat: {ex.Message}", LogLevel.Warn);
            }
        }

        private void RunFromChat(ChatBox chat, string name, string[] args, Action<FarmTestCommandsModule, string[]> handler)
        {
            replyChat = chat;
            try { Run(name, args, handler); }
            finally { replyChat = null; }
        }

        private void Run(string name, string[] args, Action<FarmTestCommandsModule, string[]> handler)
        {
            if (!Context.IsWorldReady)
            {
                Say("Partida no cargada.", LogLevel.Warn);
                return;
            }
            if (!Context.IsMainPlayer)
            {
                Say("Este comando solo puede ejecutarse en el Host.", LogLevel.Warn);
                return;
            }
            if (!context.Config.EnableTestCommands)
            {
                Say($"{name} está desactivado. Pon \"EnableTestCommands\": true en config.json (solo en partidas de prueba) y reinicia.", LogLevel.Warn);
                return;
            }

            try
            {
                handler(this, args);
            }
            catch (Exception ex)
            {
                Say($"[FarmTest] Error en {name}: {ex.Message}", LogLevel.Error);
                context.Monitor.Log(ex.ToString(), LogLevel.Trace);
            }
        }

        private void Say(string message, LogLevel level)
        {
            context.Monitor.Log(message, level);
            replyChat?.addInfoMessage(message);
        }

        /// <summary>Respuesta de un comando que cambia el mundo: además de mostrarla, queda en el registro de sucesos.</summary>
        private void Done(string command, string summary)
        {
            Say(summary, LogLevel.Info);
            AdminApiModule.EventLog.Append(HostEventTypes.TestCommand, Game1.Date.TotalDays, Game1.timeOfDay, summary,
                new Dictionary<string, object?> { ["command"] = command });
        }

        // ---------- Comandos ----------

        private void OnFound(string[] args)
        {
            if (args.Length < 2 || !FarmTestCommandLogic.TryParseTarget(args[0], out FarmTestTarget target) || target.IsHostFarm || target.AnnexSide != null)
            {
                Say("Uso: p4e_farm_found <hueco> <tipo> [jugador]  (hueco p. ej. 03)", LogLevel.Warn);
                return;
            }
            if (!FarmTestCommandLogic.TryParseFarmType(args[1], out string farmType))
            {
                Say($"Tipo desconocido '{args[1]}'. Tipos: {string.Join(", ", FarmTemplateCatalog.Templates.Keys)}.", LogLevel.Warn);
                return;
            }

            Farmer? player = ResolvePlayer(args.Skip(2).ToArray());
            if (player == null)
                return;

            Done("p4e_farm_found", FarmSlotModule.AdminFoundResidence(target.ResidenceSlotId!, farmType, player.UniqueMultiplayerID));
        }

        private void OnTeleport(string[] args)
        {
            if (args.Length < 1 || !FarmTestCommandLogic.TryParseTarget(args[0], out FarmTestTarget target))
            {
                Say("Uso: p4e_farm_tp <hueco|hueco/Lado|host> [jugador] [house|north|south|east|cave]", LogLevel.Warn);
                return;
            }

            var rest = args.Skip(1).ToList();
            FarmTestEntry entry = FarmTestEntry.House;
            if (rest.Count > 0 && FarmTestCommandLogic.TryParseEntry(rest[^1], out FarmTestEntry parsedEntry))
            {
                entry = parsedEntry;
                rest.RemoveAt(rest.Count - 1);
            }

            Farmer? player = ResolvePlayer(rest.ToArray());
            if (player == null)
                return;

            if (!TryGetArrival(target, entry, out string location, out Point tile, out int facing, out string? error))
            {
                Say(error!, LogLevel.Warn);
                return;
            }

            var payload = new WarpPayload { Location = location, X = tile.X, Y = tile.Y, Facing = facing };
            if (IsRemoteOnline(player))
            {
                Send(player, MsgWarp, payload);
                Say($"Orden de teletransporte enviada a {player.Name}: {location} ({tile.X}, {tile.Y}).", LogLevel.Info);
                return;
            }
            if (!player.IsLocalPlayer)
            {
                Say($"{player.Name} no está conectado.", LogLevel.Warn);
                return;
            }

            Game1.warpFarmer(location, tile.X, tile.Y, facing);
            Say($"{player.Name} -> {location} ({tile.X}, {tile.Y}).", LogLevel.Info);
        }

        private void OnInspect(string[] args)
        {
            if (args.Length < 1 || !FarmTestCommandLogic.TryParseTarget(args[0], out FarmTestTarget target))
            {
                Say("Uso: p4e_farm_inspect <hueco|hueco/Lado|host>", LogLevel.Warn);
                return;
            }
            if (!TryGetFarm(target, out Farm? farm, out string? error))
            {
                Say(error!, LogLevel.Warn);
                return;
            }

            foreach (string line in Inspect(target, farm!))
                Say(line, LogLevel.Info);
        }

        private void OnSpawn(string[] args)
        {
            if (args.Length < 2
                || !FarmTestCommandLogic.TryParseTarget(args[0], out FarmTestTarget target)
                || !FarmTestCommandLogic.TryParseSpawn(args[1], out FarmTestSpawn kind)
                || !FarmTestCommandLogic.TryParseCount(args.ElementAtOrDefault(2), 1, 1, 50, out int count))
            {
                Say("Uso: p4e_farm_spawn <hueco|hueco/Lado|host> <forage|ores|crate|monsters> [n=1, máx. 50]", LogLevel.Warn);
                return;
            }
            if (!TryGetFarm(target, out Farm? farm, out string? error))
            {
                Say(error!, LogLevel.Warn);
                return;
            }

            int objectsBefore = farm!.objects.Length;
            int monstersBefore = farm.characters.Count(c => c is Monster);
            string note = string.Empty;

            switch (kind)
            {
                case FarmTestSpawn.Forage:
                    // Forraje de Data/Locations; el de playa y bosque del tipo de granja sale en Farm.DayUpdate (p4e_rollover_now)
                    for (int i = 0; i < count; i++)
                        farm.spawnObjects();
                    note = " El forraje propio de playa y bosque lo genera el cambio de día (p4e_rollover_now).";
                    break;

                case FarmTestSpawn.Ores:
                    if (!farm.ShouldSpawnMountainOres() && !target.IsHostFarm)
                        note = " Esta ubicación no tiene zona de cantera.";
                    for (int i = 0; i < count; i++)
                        farm.doDailyMountainFarmUpdate();
                    break;

                case FarmTestSpawn.Crate:
                    int placed = SpawnSupplyCrates(farm, count);
                    note = placed < count ? $" Solo había sitio para {placed} (necesita casillas BeachSpawn libres)." : string.Empty;
                    break;

                case FarmTestSpawn.Monsters:
                    for (int i = 0; i < count; i++)
                        farm.spawnGroundMonsterOffScreen();
                    break;
            }

            int newObjects = farm.objects.Length - objectsBefore;
            int newMonsters = farm.characters.Count(c => c is Monster) - monstersBefore;
            Done("p4e_farm_spawn", $"{farm.NameOrUniqueName}: {kind} x{count} -> {newObjects} objetos y {newMonsters} monstruos nuevos.{note}");
        }

        private void OnLightning(string[] args)
        {
            if (args.Length < 1
                || !FarmTestCommandLogic.TryParseTarget(args[0], out FarmTestTarget target)
                || !FarmTestCommandLogic.TryParseCount(args.ElementAtOrDefault(1), 1, 1, 20, out int count))
            {
                Say("Uso: p4e_lightning <hueco|hueco/Lado|host> [n=1, máx. 20]", LogLevel.Warn);
                return;
            }
            if (!TryGetFarm(target, out Farm? farm, out string? error))
            {
                Say(error!, LogLevel.Warn);
                return;
            }

            int chargedBefore = CountChargedRods(farm!);
            for (int i = 0; i < count; i++)
                WorldLifeModule.PerformLightningStrike(farm!, Game1.timeOfDay, guaranteed: true);

            int rods = farm!.objects.Values.Count(o => o.QualifiedItemId == "(BC)9");
            Done("p4e_lightning", $"{farm.NameOrUniqueName}: {count} rayos. Pararrayos cargados {chargedBefore} -> {CountChargedRods(farm)} de {rods}.");
        }

        private void OnWeather(string[] args)
        {
            if (args.Length < 1 || !FarmTestCommandLogic.TryParseWeather(args[0], out FarmTestWeather weather))
            {
                Say("Uso: p4e_weather <sun|rain|storm> [today|tomorrow]", LogLevel.Warn);
                return;
            }

            string when = args.ElementAtOrDefault(1)?.ToLowerInvariant() ?? "today";
            if (when != "today" && when != "tomorrow")
            {
                Say("El segundo argumento debe ser today o tomorrow.", LogLevel.Warn);
                return;
            }

            string weatherId = FarmTestCommandLogic.VanillaWeatherId(weather);
            LocationWeather valley = Game1.netWorldState.Value.GetWeatherForLocation("Default");
            if (when == "tomorrow")
            {
                valley.WeatherForTomorrow = weatherId;
                Game1.weatherForTomorrow = weatherId;
            }
            else
            {
                bool raining = weather != FarmTestWeather.Sun;
                bool lightning = weather == FarmTestWeather.Storm;
                valley.Weather = weatherId;
                valley.IsRaining = raining;
                valley.IsLightning = lightning;
                valley.IsDebrisWeather = false;
                valley.IsSnowing = false;
                valley.IsGreenRain = false;
                Game1.isRaining = raining;
                Game1.isLightning = lightning;
                Game1.isDebrisWeather = false;
                Game1.isSnowing = false;
                Game1.isGreenRain = false;
            }

            Done("p4e_weather", $"Clima del valle ({(when == "today" ? "hoy" : "mañana")}): {weatherId}.");
        }

        private void OnSkill(string[] args)
        {
            if (args.Length < 3
                || !FarmTestCommandLogic.TryParseSkill(args[^2], out int skill)
                || !int.TryParse(args[^1], out int level) || level < 0 || level > 10)
            {
                Say("Uso: p4e_skill <jugador> <farming|fishing|foraging|mining|combat|luck> <0-10>", LogLevel.Warn);
                return;
            }

            Farmer? player = ResolvePlayer(args.Take(args.Length - 2).ToArray());
            if (player == null)
                return;

            var payload = new SkillPayload { Skill = skill, Level = level };
            if (IsRemoteOnline(player))
            {
                Send(player, MsgSkill, payload);
                Say($"Orden enviada al cliente de {player.Name}; esperando confirmación...", LogLevel.Info);
                return;
            }

            Done("p4e_skill", ApplySkill(player, skill, level));
        }

        // ---------- Mensajes ----------

        private void Send(Farmer player, string type, object payload)
        {
            context.Helper.Multiplayer.SendMessage(payload, type, new[] { context.Manifest.UniqueID }, new[] { player.UniqueMultiplayerID });
        }

        private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (e.FromModID != context.Manifest.UniqueID)
                return;

            bool fromHost = !Context.IsMainPlayer && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID;
            switch (e.Type)
            {
                case MsgWarp when fromHost:
                {
                    WarpPayload p = e.ReadAs<WarpPayload>();
                    Game1.warpFarmer(p.Location, p.X, p.Y, p.Facing);
                    Reply($"{Game1.player.Name} -> {p.Location} ({p.X}, {p.Y}).");
                    break;
                }

                case MsgSkill when fromHost:
                {
                    SkillPayload p = e.ReadAs<SkillPayload>();
                    string result;
                    try { result = ApplySkill(Game1.player, p.Skill, p.Level); }
                    catch (Exception ex) { result = $"Error en el cliente de {Game1.player.Name}: {ex.Message}"; }
                    Game1.addHUDMessage(new HUDMessage(result, HUDMessage.newQuest_type));
                    Reply(result);
                    break;
                }

                case MsgResult when Context.IsMainPlayer:
                {
                    string message = e.ReadAs<ResultPayload>().Message;
                    context.Monitor.Log($"[FarmTest] {message}", LogLevel.Info);
                    Game1.chatBox?.addInfoMessage(message);
                    AdminApiModule.EventLog.Append(HostEventTypes.TestCommand, Game1.Date.TotalDays, Game1.timeOfDay, message,
                        new Dictionary<string, object?> { ["from"] = e.FromPlayerID });
                    break;
                }
            }
        }

        private void Reply(string message)
        {
            context.Monitor.Log($"[FarmTest] {message}", LogLevel.Info);
            context.Helper.Multiplayer.SendMessage(new ResultPayload { Message = message }, MsgResult,
                new[] { context.Manifest.UniqueID }, new[] { Game1.MasterPlayer.UniqueMultiplayerID });
        }

        // ---------- Aplicación ----------

        private static string ApplySkill(Farmer player, int skill, int level)
        {
            switch (skill)
            {
                case 0: player.farmingLevel.Value = level; break;
                case 1: player.fishingLevel.Value = level; break;
                case 2: player.foragingLevel.Value = level; break;
                case 3: player.miningLevel.Value = level; break;
                case 4: player.combatLevel.Value = level; break;
                default: player.luckLevel.Value = level; break;
            }
            player.experiencePoints[skill] = FarmTestCommandLogic.ExperienceForLevel(level);
            return $"{player.Name}: {FarmTestCommandLogic.SkillNames[skill]} = {level} ({player.experiencePoints[skill]} XP; sin profesiones nuevas).";
        }

        /// <summary>Cajas de suministros como las crea Farm.DayUpdate en la playa: (O)922-924 con fragilidad 2 sobre casillas BeachSpawn.</summary>
        private static int SpawnSupplyCrates(Farm farm, int count)
        {
            var back = farm.map?.GetLayer("Back");
            if (back == null)
                return 0;

            var tiles = new List<Vector2>();
            for (int x = 0; x < back.LayerWidth; x++)
            {
                for (int y = 0; y < back.LayerHeight; y++)
                {
                    var tile = new Vector2(x, y);
                    if (farm.doesTileHavePropertyNoNull(x, y, "BeachSpawn", "Back") != "" && farm.CanItemBePlacedHere(tile))
                        tiles.Add(tile);
                }
            }

            int placed = 0;
            while (placed < count && tiles.Count > 0)
            {
                int index = Game1.random.Next(tiles.Count);
                Vector2 tile = tiles[index];
                tiles.RemoveAt(index);
                farm.objects.Add(tile, new StardewValley.Object(Game1.random.Next(922, 925).ToString(), 1) { Fragility = 2, MinutesUntilReady = 3 });
                placed++;
            }
            return placed;
        }

        private static int CountChargedRods(Farm farm) =>
            farm.objects.Values.Count(o => o.QualifiedItemId == "(BC)9" && o.heldObject.Value != null);

        private IEnumerable<string> Inspect(FarmTestTarget target, Farm farm)
        {
            string name = farm.NameOrUniqueName;
            DoorAssignment? door = context.WorldState.Doors.Values.FirstOrDefault(d => d.FarmSlotId == name);
            AnnexAssignment? annex = context.WorldState.FindAnnexById(name);
            string type = target.IsHostFarm ? $"granja principal (whichFarm {Game1.whichFarm})" : door?.FarmType ?? annex?.TemplateId ?? "sin reclamar";
            long? ownerId = door?.OwnerId ?? annex?.OwnerId;
            Farmer? owner = ownerId is > 0 ? Game1.GetPlayer(ownerId.Value) : null;

            yield return $"=== {name} ({type}) ===";
            yield return $"Mapa: {farm.mapPath.Value} ({farm.map?.Layers[0].LayerWidth}×{farm.map?.Layers[0].LayerHeight}).";
            yield return owner != null
                ? $"Dueño: {owner.Name} ({owner.UniqueMultiplayerID}), Combate {owner.CombatLevel}, Minería {owner.MiningLevel}."
                : $"Dueño: {(ownerId is > 0 ? ownerId.ToString() : "ninguno")}.";

            string[] props = { "SpawnBeachFarmForage", "SpawnForestFarmForage", "SpawnMountainFarmOreRect", "FarmFishLocationOverride", "FarmHouseStarterGift" };
            yield return "Propiedades: " + string.Join(", ", props.Select(p => $"{p}={farm.getMapProperty(p) ?? "-"}"));

            LocationWeather weather = farm.GetWeather();
            yield return $"Clima: {weather.Weather} (lluvia {weather.IsRaining}, tormenta {weather.IsLightning}); mañana {weather.WeatherForTomorrow}.";

            var objects = farm.objects.Values.ToList();
            int crates = objects.Count(o => o.QualifiedItemId is "(O)922" or "(O)923" or "(O)924");
            int forage = objects.Count(o => o.IsSpawnedObject);
            string[] nodeIds = { "(O)668", "(O)670", "(O)75", "(O)76", "(O)77", "(O)751", "(O)290", "(O)764", "(O)765", "(O)590" };
            var nodes = objects.Where(o => nodeIds.Contains(o.QualifiedItemId)).GroupBy(o => o.Name).Select(g => $"{g.Key} {g.Count()}");
            int rods = objects.Count(o => o.QualifiedItemId == "(BC)9");
            yield return $"Objetos: {objects.Count}; cajas {crates}; forraje {forage}; pararrayos {rods} ({CountChargedRods(farm)} con pila).";
            yield return "Cantera: " + (nodes.Any() ? string.Join(", ", nodes) : "sin nodos");

            var monsters = farm.characters.OfType<Monster>().GroupBy(m => m.Name).Select(g => $"{g.Key} {g.Count()}").ToList();
            yield return "Monstruos: " + (monsters.Count > 0 ? string.Join(", ", monsters) : "ninguno");
            yield return $"Grandes recursos (tocones, troncos, rocas, cultivos gigantes): {farm.resourceClumps.Count}.";

            if (farm.GetMainFarmHouse()?.GetIndoors() is GameLocation house)
            {
                if (house is DecoratableLocation deco)
                {
                    string wallpaper = deco.appliedWallpaper.Values.FirstOrDefault() ?? "-";
                    string floor = deco.appliedFloor.Values.FirstOrDefault() ?? "-";
                    yield return $"Casa {house.NameOrUniqueName}: papel {wallpaper}, suelo {floor}.";
                }

                Chest? gift = house.objects.Values.OfType<Chest>().FirstOrDefault(c => c.giftbox.Value && c.giftboxIsStarterGift.Value);
                yield return gift != null
                    ? "Regalo de bienvenida: " + string.Join(", ", gift.Items.Where(i => i != null).Select(i => $"{i!.QualifiedItemId} x{i.Stack}"))
                    : "Regalo de bienvenida: ya abierto o inexistente.";
            }
        }

        // ---------- Utilidades ----------

        private bool TryGetFarm(FarmTestTarget target, out Farm? farm, out string? error)
        {
            error = null;
            farm = null;
            if (target.IsHostFarm)
            {
                farm = Game1.getFarm();
            }
            else if (target.AnnexSide != null)
            {
                AnnexAssignment? annex = context.WorldState.FindAnnexById(target.LocationName);
                if (annex == null)
                {
                    error = $"{target.LocationName} no está comprado.";
                    return false;
                }
                farm = FarmSlotModule.EnsureAnnexLocation(annex, regenerate: false);
            }
            else
            {
                farm = FarmSlotModule.EnsureFarmSlotExists(target.ResidenceSlotId!);
            }

            if (farm == null)
            {
                error ??= $"No existe la ubicación {target.LocationName}.";
                return false;
            }
            return true;
        }

        private bool TryGetArrival(FarmTestTarget target, FarmTestEntry entry, out string location, out Point tile, out int facing, out string? error)
        {
            location = target.LocationName;
            tile = Point.Zero;
            facing = 2;
            error = null;

            if (!TryGetFarm(target, out Farm? farm, out error))
                return false;

            if (target.IsHostFarm)
            {
                Point entryPoint = farm!.GetMainFarmHouseEntry();
                tile = new Point(entryPoint.X, entryPoint.Y + 1);
                return true;
            }

            if (target.AnnexSide != null)
            {
                // Una casilla hacia dentro desde el warp que vuelve a la residencia
                Warp? back = farm!.warps.FirstOrDefault(w => w.TargetName == target.ResidenceSlotId) ?? farm.warps.FirstOrDefault();
                int width = farm.map.Layers[0].LayerWidth;
                int height = farm.map.Layers[0].LayerHeight;
                tile = back != null
                    ? new Point(Math.Clamp(back.X, 2, width - 3), Math.Clamp(back.Y, 2, height - 3))
                    : new Point(width / 2, height / 2);
                return true;
            }

            DoorAssignment? assignment = context.WorldState.Doors.Values.FirstOrDefault(d => d.FarmSlotId == target.ResidenceSlotId);
            FarmTemplate template = FarmTemplateCatalog.GetTemplate(assignment?.FarmType);
            switch (entry)
            {
                case FarmTestEntry.North:
                    tile = new Point(template.NorthArrivalTileX, template.NorthArrivalTileY);
                    facing = 2;
                    break;
                case FarmTestEntry.South:
                    tile = new Point(template.SouthArrivalTileX, template.SouthArrivalTileY);
                    facing = 0;
                    break;
                case FarmTestEntry.East:
                    tile = new Point(template.EastArrivalTileX, template.EastArrivalTileY);
                    facing = 3;
                    break;
                case FarmTestEntry.Cave:
                    location = $"{target.ResidenceSlotId}_Cave";
                    if (Game1.getLocationFromName(location) == null)
                    {
                        error = $"La residencia no tiene cueva ({location}).";
                        return false;
                    }
                    tile = new Point(8, 11);
                    facing = 0;
                    break;
                default:
                    DoorModel? door = FarmSlotModule.RegisteredDoors.FirstOrDefault(d => d.DefaultSlotId == target.ResidenceSlotId);
                    if (door != null)
                    {
                        FarmSlotModule.GetResidenceArrivalCoordinates(door, assignment?.FarmType, out int x, out int y, out facing);
                        tile = new Point(x, y);
                    }
                    else
                    {
                        tile = new Point(template.CabinTileX, template.CabinTileY + 1);
                    }
                    break;
            }
            return true;
        }

        private static bool IsRemoteOnline(Farmer target) =>
            !target.IsLocalPlayer && Game1.getOnlineFarmers().Any(f => f.UniqueMultiplayerID == target.UniqueMultiplayerID);

        /// <summary>Sin argumentos devuelve el host; con argumentos busca por id o nombre (conectados primero).</summary>
        private Farmer? ResolvePlayer(string[] args)
        {
            if (args.Length == 0)
                return Game1.player;

            string text = string.Join(" ", args).Trim();
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

        public sealed class WarpPayload
        {
            public string Location { get; set; } = string.Empty;
            public int X { get; set; }
            public int Y { get; set; }
            public int Facing { get; set; } = 2;
        }

        public sealed class SkillPayload
        {
            public int Skill { get; set; }
            public int Level { get; set; }
        }

        public sealed class ResultPayload
        {
            public string Message { get; set; } = string.Empty;
        }
    }
}
