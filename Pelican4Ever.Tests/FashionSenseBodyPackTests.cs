using System.Buffers.Binary;
using System.Text.Json;
using Xunit;

namespace Pelican4Ever.Tests;

public class FashionSenseBodyPackTests
{
    [Fact]
    public void DragonBodySheet_UsesNativePixelFrameGeometry()
    {
        string root = FindRepoRoot();
        string bodyDirectory = Path.Combine(root, "agents", "Dr4co.FashionSense", "Bodies", "Dr4co");
        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(Path.Combine(bodyDirectory, "body.json")));
        JsonElement body = config.RootElement.GetProperty("FrontBody");
        int width = body.GetProperty("BodySize").GetProperty("Width").GetInt32();
        int height = body.GetProperty("BodySize").GetProperty("Length").GetInt32();
        float scale = body.GetProperty("Scale").GetSingle();

        byte[] png = File.ReadAllBytes(Path.Combine(bodyDirectory, "body.png"));
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        int imageWidth = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        int imageHeight = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));

        // Four animation frames across and four directions down; Scale controls draw size,
        // not the source rectangle dimensions.
        Assert.Equal(width * 4, imageWidth);
        Assert.Equal(height * 4, imageHeight);
        Assert.Equal(4f, scale);

        Assert.Equal(0, GetY(config.RootElement, "FrontBody"));
        Assert.Equal(height, GetY(config.RootElement, "RightBody"));
        Assert.Equal(height * 2, GetY(config.RootElement, "BackBody"));
        Assert.Equal(height * 3, GetY(config.RootElement, "LeftBody"));
    }

    private static int GetY(JsonElement root, string direction) =>
        root.GetProperty(direction).GetProperty("StartingPosition").GetProperty("Y").GetInt32();

    private static string FindRepoRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "agents", "Dr4co.FashionSense", "Bodies", "Dr4co", "body.json")))
                return dir.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate Fashion Sense body pack source.");
    }
}
