using System;

namespace Pelican4Ever.Core.Economy
{
    /// <summary>Coste de los pisos recorridos, sin depender del motor ni de cómo se haya descendido.</summary>
    public static class SkullCavernDescent
    {
        public static double Cost(int previousFloor, int destinationFloor, double baseCost, double depthDivisor)
        {
            if (previousFloor < 1) throw new ArgumentOutOfRangeException(nameof(previousFloor));
            if (destinationFloor < 1) throw new ArgumentOutOfRangeException(nameof(destinationFloor));
            if (!double.IsFinite(baseCost) || baseCost < 0) throw new ArgumentOutOfRangeException(nameof(baseCost));
            if (!double.IsFinite(depthDivisor) || depthDivisor < 0) throw new ArgumentOutOfRangeException(nameof(depthDivisor));
            if (destinationFloor <= previousFloor) return 0;
            double count = (double)destinationFloor - previousFloor;
            double depthSum = count * ((double)previousFloor + 1 + destinationFloor) / 2;
            return count * baseCost + (depthDivisor == 0 ? 0 : depthSum / depthDivisor);
        }
    }
}
