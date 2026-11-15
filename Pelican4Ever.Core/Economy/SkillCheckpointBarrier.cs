using System.Collections.Generic;

namespace Pelican4Ever.Core.Economy
{
    /// <summary>Comprueba que están todos los participantes y que sus puntos replicados coinciden con sus confirmaciones.</summary>
    public static class SkillCheckpointBarrier
    {
        public static bool IsReady(IReadOnlyDictionary<long, int[]?> acknowledgements, IReadOnlyDictionary<long, int[]> replicated)
        {
            if (acknowledgements.Count != replicated.Count) return false;
            foreach (var pair in acknowledgements)
            {
                if (pair.Value == null || !replicated.TryGetValue(pair.Key, out int[]? current) || current.Length != pair.Value.Length)
                    return false;
                for (int i = 0; i < current.Length; i++)
                    if (current[i] != pair.Value[i]) return false;
            }
            return true;
        }
    }
}
