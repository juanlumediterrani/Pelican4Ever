using Pelican4Ever.Core.Admin;
using StardewModdingAPI;

namespace Pelican4Ever.Framework
{
    /// <summary>
    /// Envuelve el monitor de SMAPI del mod: todo sigue yendo al registro, y además, mientras un comando se ejecuta a
    /// distancia (<see cref="RemoteCommandOutput"/>), las líneas que escribe el hilo que lo ejecuta se recogen para
    /// devolverlas por la API. Fuera de una ejecución remota no hace nada más que delegar.
    /// </summary>
    public sealed class CapturingMonitor : IMonitor
    {
        private readonly IMonitor inner;

        public CapturingMonitor(IMonitor inner)
        {
            this.inner = inner;
        }

        public bool IsVerbose => inner.IsVerbose;

        public void Log(string message, LogLevel level = LogLevel.Trace)
        {
            RemoteCommandOutput.Append(message);
            inner.Log(message, level);
        }

        public void LogOnce(string message, LogLevel level = LogLevel.Trace)
        {
            RemoteCommandOutput.Append(message);
            inner.LogOnce(message, level);
        }

        public void VerboseLog(string message) => inner.VerboseLog(message);

        public void VerboseLog(ref StardewModdingAPI.Framework.Logging.VerboseLogStringHandler message) => inner.VerboseLog(ref message);
    }
}
