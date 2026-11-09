using System;
using System.Linq;
using Pelican4Ever.Modules;

namespace Pelican4Ever
{
    /// <summary>
    /// API de mod de Pelican 4Ever (<c>Helper.ModRegistry.GetApi&lt;IPelicanApi&gt;("Pelican4Ever.SVRTMP")</c>). El mod que la
    /// consuma declara su propia interfaz con los métodos que use; SMAPI hace el puente.
    ///
    /// Pensada para Stardew Voyager: si necesita que el host dibuje (por ejemplo, para ver la pantalla al manejar menús), lo
    /// pide con <see cref="RequestRendering"/> y lo retira con <see cref="ReleaseRendering"/>. Sus capturas
    /// (<c>takeMapScreenshot</c>) no lo necesitan.
    /// </summary>
    public sealed class PelicanApi
    {
        /// <summary>Pide que el host dibuje durante <paramref name="minutes"/> minutos (1-120). Devuelve la caducidad (UTC) o null si no se concede.</summary>
        public DateTime? RequestRendering(string requester, int minutes)
        {
            if (minutes <= 0)
                return null;
            var status = HeadlessRenderModule.RequestRendering(requester, minutes);
            return status.Leases.FirstOrDefault(l => string.Equals(l.Requester, requester, StringComparison.OrdinalIgnoreCase))?.ExpiresAtUtc;
        }

        /// <summary>Retira el préstamo de dibujado de <paramref name="requester"/>.</summary>
        public void ReleaseRendering(string requester) => HeadlessRenderModule.RequestRendering(requester, 0);

        /// <summary>Si el host está dibujando ahora.</summary>
        public bool IsHostDrawing() => HeadlessRenderModule.GetStatus().Drawing;
    }
}
