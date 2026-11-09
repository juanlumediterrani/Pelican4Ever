using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Pelican4Ever.Core.Admin
{
    /// <summary>
    /// Por qué el host dibuja o no en este fotograma. Lo expone <c>GET /api/v1/host/render</c>.
    /// </summary>
    public enum HostRenderReason
    {
        /// <summary><c>HeadlessRendering</c> está desactivado en config.json.</summary>
        Disabled,

        /// <summary>No es un servidor desatendido (sin <c>AutoHostSaveName</c>): hay una persona mirando la pantalla.</summary>
        Attended,

        /// <summary>Pantalla de título o carga: se dibuja hasta que el mundo esté listo.</summary>
        WorldNotReady,

        /// <summary>
        /// Hay abierto un menú que el juego no completa sin dibujarse: <c>SaveGameMenu</c> solo empieza a guardar
        /// cuando su <c>draw()</c> ha puesto <c>hasDrawn = true</c>. Sin esto el guardado nocturno no arrancaría.
        /// </summary>
        DrawRequiredMenu,

        /// <summary>Alguien (Voyager, Putre, la consola) ha pedido dibujar durante un tiempo.</summary>
        Lease,

        /// <summary>No se dibuja: la simulación sigue a 60 Hz y la red funciona igual.</summary>
        Suppressed
    }

    /// <summary>Decisión pura de si el host debe dibujar. Sin dependencias del juego para poder probarla.</summary>
    public static class HostRenderPolicy
    {
        public static HostRenderReason Decide(
            bool headlessEnabled,
            bool unattended,
            bool worldReady,
            bool drawRequiredMenuOpen,
            bool hasActiveLease)
        {
            if (!headlessEnabled)
                return HostRenderReason.Disabled;
            if (!unattended)
                return HostRenderReason.Attended;
            if (!worldReady)
                return HostRenderReason.WorldNotReady;
            if (drawRequiredMenuOpen)
                return HostRenderReason.DrawRequiredMenu;
            if (hasActiveLease)
                return HostRenderReason.Lease;
            return HostRenderReason.Suppressed;
        }

        public static bool ShouldDraw(HostRenderReason reason) => reason != HostRenderReason.Suppressed;
    }

    /// <summary>Préstamo de dibujado: quién lo pidió y hasta cuándo.</summary>
    public sealed record HostRenderLease(string Requester, DateTime ExpiresAtUtc);

    /// <summary>
    /// Préstamos de dibujado con caducidad. Cada solicitante tiene como mucho uno (pedirlo de nuevo lo renueva) y
    /// ninguno dura más de <see cref="MaxMinutes"/>: si quien lo pidió se cae, el host vuelve solo a no dibujar.
    /// Es seguro entre hilos: lo usan la API HTTP, la consola y el hilo del juego.
    /// </summary>
    public sealed class HostRenderLeaseRegistry
    {
        public const int MaxMinutes = 120;

        private static readonly Regex RequesterPattern = new("^[A-Za-z0-9._-]{1,40}$", RegexOptions.Compiled);
        private readonly Dictionary<string, DateTime> leases = new(StringComparer.OrdinalIgnoreCase);
        private readonly object gate = new();

        public static bool IsValidRequester(string? requester) =>
            requester != null && RequesterPattern.IsMatch(requester);

        /// <summary>Concede o renueva el préstamo de <paramref name="requester"/>. Minutos fuera de 1..120 se recortan.</summary>
        public HostRenderLease Grant(string requester, int minutes, DateTime nowUtc)
        {
            if (!IsValidRequester(requester))
                throw new ArgumentException("El solicitante debe tener 1-40 caracteres [A-Za-z0-9._-].", nameof(requester));

            int clamped = Math.Clamp(minutes, 1, MaxMinutes);
            DateTime expires = nowUtc.AddMinutes(clamped);
            lock (gate)
            {
                leases[requester] = expires;
                return new HostRenderLease(requester, expires);
            }
        }

        /// <summary>Retira el préstamo de <paramref name="requester"/>. Devuelve si existía.</summary>
        public bool Release(string requester)
        {
            lock (gate)
                return leases.Remove(requester);
        }

        /// <summary>Préstamos vigentes, ordenados por caducidad. Purga los caducados.</summary>
        public IReadOnlyList<HostRenderLease> Active(DateTime nowUtc)
        {
            lock (gate)
            {
                foreach (string expired in leases.Where(kv => kv.Value <= nowUtc).Select(kv => kv.Key).ToList())
                    leases.Remove(expired);
                return leases
                    .Select(kv => new HostRenderLease(kv.Key, kv.Value))
                    .OrderBy(l => l.ExpiresAtUtc)
                    .ToList();
            }
        }

        public bool HasActive(DateTime nowUtc) => Active(nowUtc).Count > 0;
    }
}
