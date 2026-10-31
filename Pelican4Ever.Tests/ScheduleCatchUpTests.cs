using Pelican4Ever.Core.Time;
using Xunit;

namespace Pelican4Ever.Tests;

public class ScheduleCatchUpTests
{
    private static readonly int[] PierreLike = { 900, 1300, 1700, 2100 };

    [Theory]
    [InlineData(600, null)]    // Antes de la primera entrada: sigue en su sitio de la mañana
    [InlineData(900, 900)]     // Justo a la hora de una entrada
    [InlineData(1300, 1300)]   // Servidor arrancado a la 1 PM
    [InlineData(1450, 1300)]   // Entre dos entradas: la última que ya empezó
    [InlineData(2550, 2100)]   // Madrugada congelada: la última del día
    public void LatestEntryAtOrBefore_PicksLastStartedEntry(int timeOfDay, int? expected)
    {
        Assert.Equal(expected, ScheduleCatchUp.LatestEntryAtOrBefore(PierreLike, timeOfDay));
    }

    [Fact]
    public void LatestEntryAtOrBefore_IgnoresKeyOrder()
    {
        Assert.Equal(1700, ScheduleCatchUp.LatestEntryAtOrBefore(new[] { 2100, 900, 1700, 1300 }, 1800));
    }

    [Fact]
    public void LatestEntryAtOrBefore_EmptySchedule_ReturnsNull()
    {
        Assert.Null(ScheduleCatchUp.LatestEntryAtOrBefore(System.Array.Empty<int>(), 1300));
    }
}
