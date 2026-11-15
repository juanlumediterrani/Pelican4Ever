using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Pelican4Ever.Core.Server
{
    /// <summary>Proceso que tiene ocupado el puerto UDP de juego.</summary>
    public readonly struct PortOccupant
    {
        public PortOccupant(int pid, string processName)
        {
            Pid = pid;
            ProcessName = processName;
        }

        /// <summary>PID del proceso propietario del socket.</summary>
        public int Pid { get; }

        /// <summary>Nombre del proceso (de <c>/proc/&lt;pid&gt;/comm</c>).</summary>
        public string ProcessName { get; }

        public override string ToString() => $"PID {Pid} ({ProcessName})";
    }

    /// <summary>
    /// Comprobación del puerto UDP que el juego fija para alojar cooperativo (Stardew Valley 1.6.15:
    /// 24642, hardcodeado en <c>LidgrenServer.initialize</c>). Solo una instancia por máquina puede
    /// alojar: cargar la partida con el puerto ocupado acaba en «Address already in use» y, al volver
    /// al título, en el bucle de <c>NullReferenceException</c> de <c>stopServer</c>.
    /// </summary>
    public static class GamePortProbe
    {
        /// <summary>Puerto UDP que el juego ocupa para alojar (fijo en el motor desde 1.6.15).</summary>
        public const int GameHostPort = 24642;

        /// <summary>True si el puerto está libre ahora mismo (se podría alojar).</summary>
        public static bool IsAvailable(int port = GameHostPort)
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.Bind(new IPEndPoint(IPAddress.Any, port));
                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }

        /// <summary>
        /// Quién tiene el puerto, leyendo <c>/proc/net/udp</c> y <c>/proc/&lt;pid&gt;/fd</c> (solo Linux;
        /// en otros sistemas o sin permisos devuelve null). Best-effort: ante cualquier fallo, null.
        /// </summary>
        public static PortOccupant? FindOccupant(int port = GameHostPort)
        {
            if (!OperatingSystem.IsLinux())
                return null;

            try
            {
                string? inode = FindBoundSocketInode(port);
                if (inode == null)
                    return null;

                string expected = $"socket:[{inode}]";
                foreach (string procDir in Directory.EnumerateDirectories("/proc"))
                {
                    if (!int.TryParse(Path.GetFileName(procDir), out int pid))
                        continue;

                    try
                    {
                        foreach (string fd in Directory.EnumerateFiles(Path.Combine(procDir, "fd")))
                        {
                            string? target;
                            try
                            {
                                target = new FileInfo(fd).LinkTarget;
                            }
                            catch (IOException)
                            {
                                continue;
                            }
                            catch (UnauthorizedAccessException)
                            {
                                continue;
                            }

                            if (target != expected)
                                continue;

                            string comm = File.ReadAllText(Path.Combine(procDir, "comm")).Trim();
                            return new PortOccupant(pid, comm);
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Procesos de otros usuarios: no se pueden leer sus descriptores.
                    }
                    catch (IOException)
                    {
                        // El proceso terminó mientras se recorría.
                    }
                    catch (ArgumentException)
                    {
                    }
                }

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string? FindBoundSocketInode(int port)
        {
            string suffix = ":" + port.ToString("x4", CultureInfo.InvariantCulture);
            foreach (string table in new[] { "/proc/net/udp", "/proc/net/udp6" })
            {
                if (!File.Exists(table))
                    continue;

                foreach (string line in File.ReadLines(table))
                {
                    // sl local_address rem_address st tx_queue:rx_queue tr:tm->when retrnsmt uid timeout inode
                    string[] columns = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (columns.Length < 10 || !columns[1].EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        continue;
                    return columns[9];
                }
            }

            return null;
        }
    }
}
