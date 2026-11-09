using Pelican4Ever.Core.Config;
using Pelican4Ever.Core.Networking;
using Xunit;

namespace Pelican4Ever.Tests;

public class ClientModRequirementsTests
{
    private static readonly KeyValuePair<string, string>[] Required =
    {
        new("Pathoschild.ContentPatcher", "2.9.1"),
        new("spacechase0.SpaceCore", "1.28.4"),
        new("Nature.NatureInTheValley", "1.3.4"),
    };

    [Fact]
    public void FullModpack_IsAccepted_IgnoringIdCase_AndExtraClientMods()
    {
        var client = new Dictionary<string, string>
        {
            ["pathoschild.contentpatcher"] = "2.9.1",
            ["spacechase0.SpaceCore"] = "1.28.4",
            ["Nature.NatureInTheValley"] = "1.3.4",
            ["Someone.ClientOnlyMod"] = "1.0.0",  // mods de más no se exigen ni se rechazan aquí
        };

        Assert.Empty(ClientModRequirements.Check(Required, client));
    }

    [Fact]
    public void MissingAndMismatchedMods_AreReportedInOrder()
    {
        var client = new Dictionary<string, string>
        {
            ["Pathoschild.ContentPatcher"] = "2.9.0",
            ["Nature.NatureInTheValley"] = "1.3.4",
        };

        var problems = ClientModRequirements.Check(Required, client);

        Assert.Equal(2, problems.Count);
        Assert.Equal("Pathoschild.ContentPatcher", problems[0].UniqueId);
        Assert.False(problems[0].IsMissing);
        Assert.Equal("2.9.0", problems[0].ClientVersion);
        Assert.Equal("spacechase0.SpaceCore", problems[1].UniqueId);
        Assert.True(problems[1].IsMissing);
    }

    [Fact]
    public void EmptyRequirement_AcceptsEveryone()
    {
        Assert.Empty(ClientModRequirements.Check(Array.Empty<KeyValuePair<string, string>>(), new Dictionary<string, string>()));
    }

    [Fact]
    public void Rejection_IsReadable_AndTruncated()
    {
        var problems = ClientModRequirements.Check(Required, new Dictionary<string, string> { ["Pathoschild.ContentPatcher"] = "2.0.0" });

        string reason = ClientModRequirements.DescribeRejection(problems, maxShown: 2);

        Assert.Contains("Pathoschild.ContentPatcher 2.0.0 (el servidor usa 2.9.1)", reason);
        Assert.Contains("falta spacechase0.SpaceCore 1.28.4", reason);
        Assert.Contains("y 1 más", reason);
        Assert.Equal(string.Empty, ClientModRequirements.DescribeRejection(Array.Empty<ClientModProblem>()));
    }

    [Theory]
    [InlineData("Pathoschild.ContentPatcher", true)]
    [InlineData("Dr4co.Stardew.FashionSenseBody", true)]
    [InlineData("SmolHooman.WildAnimalsInTheValley", true)]
    [InlineData("", false)]
    [InlineData("con espacio", false)]
    [InlineData("../etc", false)]
    public void UniqueIdValidation(string id, bool valid)
    {
        Assert.Equal(valid, ClientModRequirements.IsValidUniqueId(id));
    }

    [Fact]
    public void Config_DefaultsToNoExtraRequirements()
    {
        var config = new ServerConfig();

        Assert.Empty(config.RequiredClientMods);
        Assert.Empty(config.Validate());
    }

    [Fact]
    public void Config_RejectsInvalidIds()
    {
        var config = new ServerConfig { RequiredClientMods = new List<string> { "Pathoschild.ContentPatcher", "mal id" } };

        Assert.Contains(config.Validate(), e => e.Contains(nameof(ServerConfig.RequiredClientMods)));
    }
}
