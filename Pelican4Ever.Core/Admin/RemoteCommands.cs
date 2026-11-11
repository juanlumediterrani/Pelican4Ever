using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Pelican4Ever.Core.Players;

namespace Pelican4Ever.Core.Admin
{
    /// <summary>Qué puede hacer un comando de consola ejecutado a distancia (<c>POST /api/v1/host/command</c>).</summary>
    public enum RemoteCommandRisk
    {
        /// <summary>Solo informa; no cambia nada.</summary>
        Read,

        /// <summary>Cambia algo, pero sin poder perder progreso ni afectar a un jugador (p. ej. un guardado).</summary>
        Safe,

        /// <summary>Cambia el mundo o a un jugador. Quien lo invoque debería tener la aprobación del dueño.</summary>
        Sensitive
    }

    /// <param name="Name">Nombre de la consola de SMAPI.</param>
    /// <param name="Risk">Clase de riesgo.</param>
    /// <param name="MinArgs">Argumentos mínimos.</param>
    /// <param name="MaxArgs">Argumentos máximos.</param>
    /// <param name="Usage">Uso, para que quien lo invoque sepa los argumentos.</param>
    /// <param name="Check">Validación específica de los argumentos; devuelve el motivo del rechazo o null.</param>
    public sealed record RemoteCommandSpec(
        string Name,
        RemoteCommandRisk Risk,
        int MinArgs,
        int MaxArgs,
        string Usage,
        Func<IReadOnlyList<string>, string?>? Check = null);

    /// <summary>
    /// Lista cerrada de comandos de consola de Pelican 4Ever que se pueden ejecutar por la API de anfitrión. Es una
    /// lista de permitidos: lo que no está aquí no se puede invocar, aunque exista como comando. Quedan fuera a propósito:
    /// <c>p4e_rollover_now</c> (adelanta el día), <c>p4e_safe_restart</c> (tiene su ruta con avisos), <c>p4e_time</c>
    /// (desajusta el calendario real), <c>p4e_migrate_save</c>, <c>p4e_give_*</c>, los comandos de prueba
    /// (<c>EnableTestCommands</c>) y los de depuración de puntos de guardado.
    /// </summary>
    public static class RemoteCommandCatalog
    {
        public const int MaxArgLength = 40;
        public const int MaxOutputLines = 40;
        public const int MaxOutputLineLength = 300;

        private static readonly Regex ArgPattern = new("^[A-Za-z0-9_.@:-]{1,40}$", RegexOptions.Compiled);
        private static readonly string[] Seasons = { "spring", "summer", "fall", "winter" };

        public static readonly IReadOnlyList<RemoteCommandSpec> Commands = new List<RemoteCommandSpec>
        {
            new("p4e_status", RemoteCommandRisk.Read, 0, 0, "p4e_status"),
            new("p4e_doors", RemoteCommandRisk.Read, 0, 0, "p4e_doors"),
            new("p4e_claim_list", RemoteCommandRisk.Read, 0, 0, "p4e_claim_list"),
            new("p4e_pair_list", RemoteCommandRisk.Read, 0, 0, "p4e_pair_list"),
            new("p4e_checkpoint_now", RemoteCommandRisk.Safe, 0, 0, "p4e_checkpoint_now"),
            new("p4e_pair_allow", RemoteCommandRisk.Safe, 2, 2, "p4e_pair_allow <usuario> <ip>", CheckPairAllow),
            new("p4e_claim_reset", RemoteCommandRisk.Sensitive, 1, 1, "p4e_claim_reset <nombre_o_id>"),
            new("p4e_pair_revoke", RemoteCommandRisk.Sensitive, 1, 1, "p4e_pair_revoke <usuario_o_ip_o_id>"),
            new("p4e_forage_wave", RemoteCommandRisk.Sensitive, 0, 0, "p4e_forage_wave"),
            new("p4e_festival_move", RemoteCommandRisk.Sensitive, 3, 3, "p4e_festival_move <spring|summer|fall|winter> <díaActual> <díaNuevo>", CheckFestivalMove),
        };

        public static RemoteCommandSpec? Find(string? name) =>
            Commands.FirstOrDefault(c => string.Equals(c.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>Devuelve el motivo del rechazo o null si el comando y sus argumentos son válidos.</summary>
        public static string? Validate(string? name, IReadOnlyList<string>? args, out RemoteCommandSpec? spec)
        {
            spec = Find(name);
            if (spec == null)
                return $"'{name}' no es un comando remoto permitido.";

            args ??= Array.Empty<string>();
            if (args.Count < spec.MinArgs || args.Count > spec.MaxArgs)
                return $"Uso: {spec.Usage}";

            foreach (string arg in args)
            {
                if (!ArgPattern.IsMatch(arg ?? string.Empty))
                    return $"Argumento no válido '{arg}': solo letras, cifras y . _ - @ : (máx. {MaxArgLength}).";
            }

            return spec.Check?.Invoke(args);
        }

        private static string? CheckFestivalMove(IReadOnlyList<string> args)
        {
            if (!Seasons.Contains(args[0], StringComparer.OrdinalIgnoreCase))
                return "La estación debe ser spring, summer, fall o winter.";
            if (!int.TryParse(args[1], out int from) || !int.TryParse(args[2], out int to) || from is < 1 or > 28 || to is < 1 or > 28)
                return "Los días deben ser números entre 1 y 28.";
            return null;
        }

        private static string? CheckPairAllow(IReadOnlyList<string> args)
        {
            if (!ClientPairingLogic.IsValidInviteIp(args[1], out _, out string? error))
                return error;
            return null;
        }
    }

    /// <summary>
    /// Recoge lo que un comando escribe en el registro mientras se ejecuta por la API, solo del hilo que lo ejecuta
    /// (otros hilos y otros mods no se mezclan). Los mensajes que un comando emita más tarde, en otros ticks, no entran.
    /// </summary>
    public static class RemoteCommandOutput
    {
        [ThreadStatic]
        private static List<string>? buffer;

        public static bool IsCapturing => buffer != null;

        /// <summary>Empieza a capturar en este hilo. Devuelve un objeto que termina la captura al desecharse.</summary>
        public static IDisposable Begin()
        {
            buffer = new List<string>();
            return new Scope();
        }

        /// <summary>Añade una línea a la captura de este hilo, si la hay.</summary>
        public static void Append(string? message)
        {
            if (buffer == null || string.IsNullOrWhiteSpace(message))
                return;
            foreach (string raw in message.Replace("\r", string.Empty).Split('\n'))
            {
                string line = raw.TrimEnd();
                if (line.Length == 0)
                    continue;
                if (buffer.Count < MaxLinesHard)
                    buffer.Add(line);
            }
        }

        /// <summary>Líneas capturadas hasta ahora, acotadas en número y longitud.</summary>
        public static IReadOnlyList<string> Snapshot()
        {
            if (buffer == null)
                return Array.Empty<string>();
            return buffer
                .Take(RemoteCommandCatalog.MaxOutputLines)
                .Select(l => l.Length <= RemoteCommandCatalog.MaxOutputLineLength ? l : l.Substring(0, RemoteCommandCatalog.MaxOutputLineLength) + "…")
                .ToList();
        }

        private const int MaxLinesHard = 200;

        private sealed class Scope : IDisposable
        {
            public void Dispose() => buffer = null;
        }
    }
}
