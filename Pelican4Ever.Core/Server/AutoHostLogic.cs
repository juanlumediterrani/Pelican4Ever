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

    /// <summary>Qué hacer en la pantalla de título según esté libre o no el puerto UDP de juego.</summary>
    public enum PortGateDecision
    {
        /// <summary>El puerto está libre: cargar la partida como anfitrión.</summary>
        Load,

        /// <summary>Puerto ocupado y dentro del margen de espera: volver a comprobar en el siguiente tick.</summary>
        KeepWaiting,

        /// <summary>Puerto ocupado tras agotar el margen: no cargar (evita el bucle de errores en el título).</summary>
        GiveUp
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

        /// <summary>Segundos máximos esperando a que el puerto UDP de juego se libere antes de rendirse.</summary>
        public const int PortWaitSeconds = 60;

        /// <summary>
        /// El juego fija el puerto UDP 24642 al alojar: cargar la partida con el puerto ocupado (otra
        /// instancia de Stardew o el propio proceso tras un <c>stopServer</c> fallido) acaba en
        /// «Address already in use» y en un bucle de <c>NullReferenceException</c> al volver al título.
        /// Con el puerto libre se carga; ocupado se espera hasta <c>PortWaitSeconds</c> y, si no se
        /// libera, no se intenta: el supervisor verá la host API caída y alertará.
        /// </summary>
        public static PortGateDecision DecidePortGate(bool portAvailable, int waitedSeconds, int maxWaitSeconds = PortWaitSeconds)
        {
            if (portAvailable)
                return PortGateDecision.Load;
            return waitedSeconds < maxWaitSeconds ? PortGateDecision.KeepWaiting : PortGateDecision.GiveUp;
        }
    }
}
