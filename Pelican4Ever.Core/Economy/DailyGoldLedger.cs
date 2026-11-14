using System;
using System.Collections.Generic;

namespace Pelican4Ever.Core.Economy
{
    /// <summary>Créditos brutos, separados por origen y por ámbito de cartera; no presupone que un crédito sea una venta.</summary>
    public sealed class DailyGoldLedger
    {
        public Dictionary<int, DailyGoldEntry> Days { get; set; } = new();

        public void Add(int day, long amount, bool sharedWallet, bool hostApplied)
        {
            if (day < 0 || amount <= 0) return;
            Days ??= new();
            if (!Days.TryGetValue(day, out DailyGoldEntry? entry) || entry == null)
                Days[day] = entry = new DailyGoldEntry();
            if (sharedWallet)
            {
                if (hostApplied) entry.HostShared = checked(entry.HostShared + amount);
                else entry.ClientShared = checked(entry.ClientShared + amount);
            }
            else if (hostApplied) entry.HostIndividual = checked(entry.HostIndividual + amount);
            else entry.ClientIndividual = checked(entry.ClientIndividual + amount);
        }

        /// <summary>Incorpora exclusivamente los contadores del cliente, sin borrar créditos registrados por el host.</summary>
        public void MergeClient(DailyGoldLedger report, int currentDay)
        {
            Days ??= new();
            if (report?.Days == null) return;
            foreach (var pair in report.Days)
            {
                if (pair.Key < 0 || pair.Key > currentDay || pair.Value == null
                    || pair.Value.ClientIndividual < 0 || pair.Value.ClientShared < 0) continue;
                if (!Days.TryGetValue(pair.Key, out DailyGoldEntry? entry) || entry == null)
                    Days[pair.Key] = entry = new DailyGoldEntry();
                entry.ClientIndividual = Math.Max(entry.ClientIndividual, pair.Value.ClientIndividual);
                entry.ClientShared = Math.Max(entry.ClientShared, pair.Value.ClientShared);
            }
        }
    }

    public sealed class DailyGoldEntry
    {
        public long ClientIndividual { get; set; }
        public long ClientShared { get; set; }
        public long HostIndividual { get; set; }
        public long HostShared { get; set; }
    }
}
