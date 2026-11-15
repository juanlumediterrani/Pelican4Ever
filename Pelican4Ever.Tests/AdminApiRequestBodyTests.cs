using Pelican4Ever.Core.Admin;
using Xunit;

namespace Pelican4Ever.Tests;

/// <summary>
/// Un POST sin longitud de cuerpo recibe de HttpListener un 411 automático con la respuesta ya cerrada; antes el mod
/// ejecutaba igualmente la acción (p. ej. arrancar el cambio de día) y, al escribir su respuesta, abortaba el proceso.
/// </summary>
public class AdminApiRequestBodyTests
{
    [Theory]
    [InlineData("POST", null, null)]
    [InlineData("post", "", null)]
    [InlineData("PUT", "  ", "identity")]
    [InlineData("PATCH", null, "gzip")]
    public void BodyMethodsWithoutLength_AreDiscarded(string method, string? contentLength, string? transferEncoding)
    {
        Assert.True(AdminApiLogic.IsMissingRequiredBodyLength(method, contentLength, transferEncoding));
    }

    [Theory]
    [InlineData("POST", "2", null)]       // curl -d '{}'
    [InlineData("POST", "0", null)]       // cuerpo vacío declarado: el handler usa sus valores por defecto
    [InlineData("POST", null, "chunked")]
    [InlineData("POST", null, "gzip, Chunked")]
    public void BodyMethodsWithDeclaredLength_AreProcessed(string method, string? contentLength, string? transferEncoding)
    {
        Assert.False(AdminApiLogic.IsMissingRequiredBodyLength(method, contentLength, transferEncoding));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("OPTIONS")]
    [InlineData("DELETE")]
    [InlineData(null)]
    public void MethodsWithoutBody_AreNeverDiscarded(string? method)
    {
        Assert.False(AdminApiLogic.IsMissingRequiredBodyLength(method, null, null));
    }
}
