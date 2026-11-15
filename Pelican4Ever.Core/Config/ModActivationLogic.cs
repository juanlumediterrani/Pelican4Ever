using System;
using Pelican4Ever.Core.Security;

namespace Pelican4Ever.Core.Config
{
    /// <summary>Decisión previa al cargar una partida guardada desde el menú.</summary>
    public enum SaveLoadPromptDecision
    {
        ProceedNormally,
        PromptToDisableBeforeLoad
    }

    /// <summary>Acción elegida por el jugador en el aviso de carga de partida.</summary>
    public enum SaveLoadPromptAction
    {
        DisableAndLoad,
        KeepEnabledAndLoad,
        Cancel
    }

    /// <summary>Decisión previa o durante la conexión a un servidor.</summary>
    public enum ServerConnectPromptDecision
    {
        ProceedNormally,
        PromptToEnableBeforeConnect
    }

    /// <summary>Acción elegida por el jugador en el aviso de conexión a servidor.</summary>
    public enum ServerConnectPromptAction
    {
        EnableAndConnect,
        Cancel
    }

    /// <summary>
    /// Lógica pura de activación/desactivación del mod en el cliente y decisiones de avisos al cargar partida o conectar a un servidor.
    /// </summary>
    public static class ModActivationLogic
    {
        /// <summary>
        /// Devuelve si Pelican 4Ever está efectivamente activo: en modo servidor automático (<see cref="ServerConfig.AutoHostSaveName"/>)
        /// siempre permanece activo con independencia de <see cref="ServerConfig.Enabled"/>.
        /// </summary>
        public static bool IsEffectivelyEnabled(bool configEnabled, string? autoHostSaveName)
        {
            if (!string.IsNullOrEmpty(autoHostSaveName))
                return true;

            return configEnabled;
        }

        /// <summary>
        /// Determina si al pulsar una ranura de partida guardada debe mostrarse el aviso para desactivar Pelican 4Ever.
        /// </summary>
        public static SaveLoadPromptDecision DecideOnSaveLoad(bool isModEnabled, string? autoHostSaveName, bool isFarmhandSlot)
        {
            if (!isModEnabled || !string.IsNullOrEmpty(autoHostSaveName) || isFarmhandSlot)
                return SaveLoadPromptDecision.ProceedNormally;

            return SaveLoadPromptDecision.PromptToDisableBeforeLoad;
        }

        /// <summary>
        /// Determina si al conectarse a un servidor debe mostrarse el aviso para activar Pelican 4Ever.
        /// </summary>
        public static ServerConnectPromptDecision DecideOnServerConnect(bool isModEnabled, bool serverHasPelican)
        {
            if (serverHasPelican && !isModEnabled)
                return ServerConnectPromptDecision.PromptToEnableBeforeConnect;

            return ServerConnectPromptDecision.ProceedNormally;
        }

        /// <summary>
        /// Comprueba si los bytes restantes de un paquete de handshake contienen el desafío de descubrimiento de Pelican 4Ever.
        /// </summary>
        public static bool HasPelicanDiscoveryChallenge(byte[]? remainingBytes)
        {
            if (remainingBytes == null || remainingBytes.Length != DeviceAuthWire.DiscoveryChallengePayloadLength)
                return false;

            return DeviceAuthWire.TryParseDiscoveryChallenge(remainingBytes.AsSpan(), string.Empty, out _);
        }
    }
}
