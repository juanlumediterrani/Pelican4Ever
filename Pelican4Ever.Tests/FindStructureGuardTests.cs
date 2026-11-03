using System;
using System.IO;
using Xunit;

namespace Pelican4Ever.Tests;

/// <summary>
/// Guarda de regresión sobre el código fuente del mod (que no se puede cargar sin el juego).
/// Prefix_FindStructure nunca debe delegar en Game1.findStructure: Building.GetIndoors() de un interior
/// no instanciado llama a Game1.getLocationFromName, que vuelve a findStructure (StackOverflowException, 9b42a58).
/// Protege especialmente la resolución de conflictos al fusionar ramas que traen la versión anterior con «return true».
/// </summary>
public class FindStructureGuardTests
{
    private const string MethodSignature = "public static bool Prefix_FindStructure(";

    [Fact]
    public void PrefixFindStructureNeverRunsTheOriginal()
    {
        string body = ReadMethodBody(FindFarmSlotModuleSource());

        Assert.DoesNotContain("return true", body);
        Assert.EndsWith("return false;", body.TrimEnd().TrimEnd('}').TrimEnd());
    }

    private static string FindFarmSlotModuleSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "Pelican4Ever", "Modules", "FarmSlotModule.cs");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException("No se encontró Pelican4Ever/Modules/FarmSlotModule.cs subiendo desde " + AppContext.BaseDirectory);
    }

    /// <summary>Cuerpo del método entre su primera llave y la que la cierra (incluida).</summary>
    private static string ReadMethodBody(string path)
    {
        string source = File.ReadAllText(path);
        int start = source.IndexOf(MethodSignature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"No se encontró {MethodSignature} en {path}");
        Assert.True(source.IndexOf(MethodSignature, start + 1, StringComparison.Ordinal) < 0, "Prefix_FindStructure aparece dos veces (¿conflicto mal resuelto?)");

        int open = source.IndexOf('{', start);
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0)
                return source.Substring(open, i - open + 1);
        }

        throw new InvalidOperationException("Llaves sin cerrar en Prefix_FindStructure");
    }
}
