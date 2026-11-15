using System;
using System.IO;
using StardewModdingAPI;

namespace Pelican4Ever.Framework
{
    /// <summary>
    /// Log de diagnóstico propio del mod, separado de SMAPI y con el rol de cada línea (H = anfitrión,
    /// C = cliente). SMAPI escribe un único <c>SMAPI-latest.txt</c> por máquina, así que al abrir el
    /// cliente se perdía el log del servidor persistente; este archivo conserva ambos.
    /// </summary>
    public static class Diagnostics
    {
        /// <summary>Tamaño a partir del cual se rota el archivo, para que no crezca sin límite.</summary>
        private const long MaxBytes = 2 * 1024 * 1024;

        private static readonly object Gate = new();
        private static string? path;

        /// <summary>Activa el archivo de diagnóstico para esta instancia.</summary>
        public static void Configure(string modDirectoryPath)
        {
            try
            {
                Directory.CreateDirectory(modDirectoryPath);
                path = Path.Combine(modDirectoryPath, "diagnostics.log");
                Write("=== Inicio de instancia ===");
            }
            catch
            {
                path = null;
            }
        }

        /// <summary>Escribe una línea con marca de tiempo y rol. Silencioso ante cualquier error de E/S.</summary>
        public static void Write(string message)
        {
            if (path == null)
                return;

            try
            {
                string role = Context.IsMainPlayer ? "H" : "C";
                lock (Gate)
                {
                    var info = new FileInfo(path);
                    if (info.Exists && info.Length > MaxBytes)
                        File.Move(path, path + ".old", overwrite: true);

                    File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss}] [{role}] {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // El diagnóstico nunca debe afectar a la partida.
            }
        }
    }
}
