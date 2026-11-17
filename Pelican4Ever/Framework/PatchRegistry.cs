using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Pelican4Ever.Core.Admin;
using Pelican4Ever.Core.Config;
using StardewModdingAPI;

namespace Pelican4Ever.Framework
{
    /// <summary>
    /// Registro centralizado de todos los parches Harmony aplicados por <c>Pelican4Ever</c> (ADR-0010, TASK-2501).
    /// Ningún parche crítico (<see cref="PatchCriticality.Security"/> o <see cref="PatchCriticality.Integrity"/>)
    /// puede fallar en silencio.
    /// </summary>
    public sealed class PatchRegistry
    {
        private readonly object syncRoot = new();
        private readonly List<PatchRegistrationEntry> entries = new();
        private readonly List<string> moduleFailures = new();

        public bool Patch(
            Harmony harmony,
            IMonitor monitor,
            string name,
            string moduleName,
            PatchCriticality criticality,
            MethodBase? original,
            HarmonyMethod? prefix = null,
            HarmonyMethod? postfix = null,
            HarmonyMethod? transpiler = null,
            HarmonyMethod? finalizer = null,
            bool optional = false)
        {
            if (original == null)
            {
                if (optional)
                {
                    Record(new PatchRegistrationEntry(name, moduleName, criticality, PatchRegistrationStatus.SkippedOptional, "Destino opcional ausente."));
                    monitor.Log($"[{moduleName}] Parche opcional '{name}' omitido: destino no presente.", LogLevel.Trace);
                    return false;
                }

                string reason = "Método/constructor destino no encontrado.";
                Record(new PatchRegistrationEntry(name, moduleName, criticality, PatchRegistrationStatus.Failed, reason));
                monitor.Log($"[{moduleName}] Error al registrar parche '{name}' ({criticality}): {reason}", LogLevel.Error);
                return false;
            }

            try
            {
                harmony.Patch(original, prefix, postfix, transpiler, finalizer);
                Record(new PatchRegistrationEntry(name, moduleName, criticality, PatchRegistrationStatus.Applied));
                return true;
            }
            catch (Exception ex)
            {
                string reason = $"{ex.GetType().Name}: {ex.Message}";
                Record(new PatchRegistrationEntry(name, moduleName, criticality, PatchRegistrationStatus.Failed, reason));
                monitor.Log($"[{moduleName}] Error al aplicar parche '{name}' ({criticality}): {ex}", LogLevel.Error);
                return false;
            }
        }

        public bool TryExecute(
            IMonitor monitor,
            string name,
            string moduleName,
            PatchCriticality criticality,
            Action apply,
            bool optional = false)
        {
            try
            {
                apply();
                Record(new PatchRegistrationEntry(name, moduleName, criticality, PatchRegistrationStatus.Applied));
                return true;
            }
            catch (Exception ex)
            {
                if (optional)
                {
                    Record(new PatchRegistrationEntry(name, moduleName, criticality, PatchRegistrationStatus.SkippedOptional, ex.Message));
                    monitor.Log($"[{moduleName}] Parche opcional '{name}' omitido por excepción: {ex.Message}", LogLevel.Trace);
                    return false;
                }

                string reason = $"{ex.GetType().Name}: {ex.Message}";
                Record(new PatchRegistrationEntry(name, moduleName, criticality, PatchRegistrationStatus.Failed, reason));
                monitor.Log($"[{moduleName}] Error al aplicar parche '{name}' ({criticality}): {ex}", LogLevel.Error);
                return false;
            }
        }

        public void RecordSkippedOptional(string name, string moduleName, PatchCriticality criticality, string reason)
        {
            Record(new PatchRegistrationEntry(name, moduleName, criticality, PatchRegistrationStatus.SkippedOptional, reason));
        }

        public void RecordModuleFailure(string moduleName, Exception ex)
        {
            lock (syncRoot)
            {
                if (!moduleFailures.Contains(moduleName))
                    moduleFailures.Add(moduleName);
            }
            _ = ex;
        }

        public PatchHealthDecision Evaluate(ServerConfig config, string? gameVersion, string? smapiVersion)
        {
            List<PatchRegistrationEntry> patchSnapshot;
            List<string> moduleFailureSnapshot;
            lock (syncRoot)
            {
                patchSnapshot = new List<PatchRegistrationEntry>(entries);
                moduleFailureSnapshot = new List<string>(moduleFailures);
            }

            return PatchHealthPolicy.Decide(
                patchSnapshot,
                gameVersion,
                config.VerifiedGameVersions,
                config.AllowUnverifiedGameVersion,
                smapiVersion,
                config.MinimumSmapiVersion,
                config.LockdownOverride,
                moduleFailureSnapshot);
        }

        public IReadOnlyList<PatchRegistrationEntry> Snapshot()
        {
            lock (syncRoot)
            {
                return entries.ToArray();
            }
        }

        private void Record(PatchRegistrationEntry entry)
        {
            lock (syncRoot)
            {
                entries.Add(entry);
            }
        }
    }
}
