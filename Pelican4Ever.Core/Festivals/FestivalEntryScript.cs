using System;
using System.Collections.Generic;
using System.Linq;

namespace Pelican4Ever.Core.Festivals
{
    /// <summary>
    /// Reescritura pura del guion <c>set-up</c> de un festival (<c>Data/Festivals/&lt;fecha&gt;</c>) para que el jugador
    /// entre sin salto de cámara: vanilla coloca la cámara en un punto fijo, teletransporta al jugador a una casilla
    /// fija del guion, oculta la vista (<c>viewport -1000 -1000</c>) y la devuelve con un fundido desde negro.
    /// </summary>
    public static class FestivalEntryScript
    {
        /// <summary>
        /// Devuelve el guion con la cámara siguiendo al jugador, el jugador en <paramref name="tileX"/>,<paramref name="tileY"/>
        /// mirando a <paramref name="facing"/>, y sin las órdenes que ocultan la vista o fuerzan el fundido desde negro.
        /// </summary>
        /// <param name="commands">Guion ya separado por '/'. [0] música, [1] cámara, [2] posiciones iniciales, [3..] órdenes.</param>
        public static string[] KeepPlayerInPlace(IReadOnlyList<string> commands, int tileX, int tileY, int facing)
        {
            if (commands.Count < 3)
                return commands.ToArray();

            var result = new List<string>(commands.Count)
            {
                commands[0],
                "follow",
                ReplaceFarmerPosition(commands[2], tileX, tileY, facing)
            };

            for (int i = 3; i < commands.Count; i++)
            {
                if (!IsHiddenViewport(commands[i]) && !IsGlobalFadeToClear(commands[i]))
                    result.Add(commands[i]);
            }

            return result.ToArray();
        }

        /// <summary>Mapa temporal del festival (<c>changeToTemporaryMap &lt;mapa&gt;</c>), o null si el guion no lo cambia.</summary>
        public static string? TemporaryMapName(IEnumerable<string> commands)
        {
            foreach (string command in commands)
            {
                string[] tokens = Tokens(command);
                if (tokens.Length >= 2 && tokens[0] == "changeToTemporaryMap")
                    return tokens[1];
            }

            return null;
        }

        /// <summary><c>viewport X Y</c> con alguna coordenada negativa: aparta la cámara para ocultar el montaje.</summary>
        public static bool IsHiddenViewport(string command)
        {
            string[] tokens = Tokens(command);
            return tokens.Length >= 3 && tokens[0] == "viewport"
                && int.TryParse(tokens[1], out int x) && int.TryParse(tokens[2], out int y)
                && (x < 0 || y < 0);
        }

        private static bool IsGlobalFadeToClear(string command)
        {
            string[] tokens = Tokens(command);
            return tokens.Length >= 1 && tokens[0] == "globalFadeToClear";
        }

        /// <summary>Las posiciones iniciales van en grupos de cuatro: nombre, X, Y, dirección.</summary>
        private static string ReplaceFarmerPosition(string positions, int tileX, int tileY, int facing)
        {
            string[] tokens = Tokens(positions);
            for (int i = 0; i + 3 < tokens.Length; i += 4)
            {
                if (tokens[i] == "farmer")
                {
                    tokens[i + 1] = tileX.ToString();
                    tokens[i + 2] = tileY.ToString();
                    tokens[i + 3] = facing.ToString();
                }
            }

            return string.Join(" ", tokens);
        }

        private static string[] Tokens(string command)
        {
            return command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
