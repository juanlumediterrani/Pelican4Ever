using System;
using System.IO;
using System.Linq;

namespace Pelican4Ever.Core.Server
{
    /// <summary>Qué hacer en la pantalla de título cuando el servidor tiene configurada una partida de arranque.</summary>
    public enum AutoHostDecision
    {
        /// <summary>Sin <c>AutoHostSaveName</c>: el anfitrión carga la partida a mano, como siempre.</summary>
        Disabled,

        /// <summary>El nombre no es una carpeta de partida (vacío tras recortar, rutas o caracteres no válidos).</summary>
        InvalidName,

        /// <summary>No existe una carpeta de partida con ese nombre exacto; no se carga otra en su lugar.</summary>
        SaveMissing,

        /// <summary>Cargar la partida como anfitrión de cooperativo.</summary>
        Load
    }

    /// <summary>
    /// Arranque desatendido del servidor persistente: al llegar a la pantalla de título, el host carga como
    /// anfitrión de cooperativo la partida configurada, por su nombre exacto y nunca «la más reciente».
    /// </summary>
    public static class AutoHostLogic
    {
        /// <summary>Segundos en la pantalla de título antes de cargar, para que el menú termine de inicializarse.</summary>
        public const int TitleSettleSeconds = 3;

        /// <summary>Un nombre de partida es el de una carpeta de <c>Saves/</c>: sin rutas, puntos de escape ni caracteres no válidos.</summary>
        public static bool IsValidSaveName(string? saveName)
        {
            if (string.IsNullOrWhiteSpace(saveName) || saveName != saveName.Trim())
                return false;
            if (saveName is "." or ".." || saveName.Contains('/') || saveName.Contains('\\'))
                return false;
            return saveName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !saveName.Any(char.IsControl);
        }

        public static AutoHostDecision Decide(string? saveName, Func<string, bool> saveExists)
        {
            if (string.IsNullOrEmpty(saveName))
                return AutoHostDecision.Disabled;
            if (!IsValidSaveName(saveName))
                return AutoHostDecision.InvalidName;
            return saveExists(saveName) ? AutoHostDecision.Load : AutoHostDecision.SaveMissing;
        }

        /// <summary>
        /// Si el reinicio controlado debe cerrar el proceso (para que el supervisor lo relance con el mod actualizado)
        /// o solo volver al título (y que la carga automática reabra la partida con el mismo mod).
        /// </summary>
        public static bool ShouldExitProcessOnRestart(bool restartExitsProcess, string? autoHostSaveName)
        {
            return restartExitsProcess && !string.IsNullOrEmpty(autoHostSaveName);
        }
    }
}
