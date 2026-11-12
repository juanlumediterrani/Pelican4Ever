using System.Text.Json;
using Pelican4Ever.Core.Players;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class AgentBodyPresetTests
    {
        [Fact]
        public void Enable_SelectsTheDr4coBodyAndBlankLayers()
        {
            var data = AgentBodyPreset.Dr4co(enable: true);

            Assert.Equal(AgentToolOffset.Dr4coBodyId, data[AgentToolOffset.BodyModDataKey]);
            Assert.Equal("Dr4co.Stardew.FashionSenseBody/Hair/None", data["FashionSense.CustomHair.Id"]);
            Assert.Equal("Dr4co.Stardew.FashionSenseBody/Shirt/None", data["FashionSense.CustomShirt.Id"]);
            Assert.Equal("Dr4co.Stardew.FashionSenseBody/Pants/None", data["FashionSense.CustomPants.Id"]);
            Assert.Equal("Dr4co.Stardew.FashionSenseBody/Shoes/None", data["FashionSense.CustomShoes.Id"]);
        }

        [Fact]
        public void Disable_ClearsTheSameKeys()
        {
            var on = AgentBodyPreset.Dr4co(enable: true);
            var off = AgentBodyPreset.Dr4co(enable: false);

            Assert.Equal(on.Keys.OrderBy(k => k), off.Keys.OrderBy(k => k));
            Assert.All(off.Values, v => Assert.Equal(string.Empty, v));
        }

        [Theory]
        [InlineData(null, true)]
        [InlineData("", true)]
        [InlineData("on", true)]
        [InlineData("SÍ", true)]
        [InlineData("off", false)]
        [InlineData("no", false)]
        [InlineData("Dr4co", null)]
        public void ParseToggle(string? arg, bool? expected)
        {
            Assert.Equal(expected, AgentBodyPreset.ParseToggle(arg));
        }

        [Fact]
        public void PutrePreset_ContainsExactVanillaParametersAndColors()
        {
            var p = AgentBodyPreset.PutrePreset;

            Assert.Equal(17, p.Skin);
            Assert.Equal(50, p.Hair);
            Assert.Equal("21", p.Shirt);
            Assert.Equal("1", p.Pants);
            Assert.Equal(2, p.Accessory);
            Assert.True(p.IsMale);

            // Colores calculados a partir de los deslizadores (H, S, V)
            Assert.Equal(181, p.EyeColorR);
            Assert.Equal(50, p.EyeColorG);
            Assert.Equal(21, p.EyeColorB);

            Assert.Equal(45, p.HairColorR);
            Assert.Equal(91, p.HairColorG);
            Assert.Equal(23, p.HairColorB);

            Assert.Equal(36, p.PantsColorR);
            Assert.Equal(43, p.PantsColorG);
            Assert.Equal(37, p.PantsColorB);

            // Mascota y cosas favoritas
            Assert.Equal("Dog", p.PetType);
            Assert.Equal("0", p.PetBreed);
            Assert.False(p.CatPerson);
            Assert.Equal("Zerbesa", p.FavoriteThing);
            Assert.Equal("Putre", p.Name);
        }

        [Fact]
        public void FashionSenseOverrideKeys_ContainsAllOverridableLayers()
        {
            var keys = AgentBodyPreset.FashionSenseOverrideKeys;

            Assert.Contains(AgentToolOffset.BodyModDataKey, keys);
            Assert.Contains("FashionSense.CustomHair.Id", keys);
            Assert.Contains("FashionSense.CustomShirt.Id", keys);
            Assert.Contains("FashionSense.CustomPants.Id", keys);
            Assert.Contains("FashionSense.CustomShoes.Id", keys);
        }

        [Fact]
        public void DefaultVanillaPreset_ContainsExpectedDefaults()
        {
            var p = AgentBodyPreset.DefaultVanillaPreset;

            Assert.Equal(0, p.Skin);
            Assert.Equal(0, p.Hair);
            Assert.Equal("0", p.Shirt);
            Assert.Equal("0", p.Pants);
            Assert.Equal(-1, p.Accessory);
            Assert.True(p.IsMale);
            Assert.Equal("Dog", p.PetType);
            Assert.Equal("0", p.PetBreed);
            Assert.False(p.CatPerson);
        }

        [Fact]
        public void EveryReferencedAppearanceExistsInThePack()
        {
            string pack = Path.Combine(FindRepoRoot(), "agents", "Dr4co.FashionSense");
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(pack, "manifest.json")));
            Assert.Equal(AgentBodyPreset.Dr4coPackId, manifest.RootElement.GetProperty("UniqueID").GetString());

            // Fashion Sense type -> pack folder / json file
            var folders = new Dictionary<string, (string Folder, string Json)>
            {
                ["Player"] = ("Bodies", "body.json"),
                ["Hair"] = ("Hairs", "hair.json"),
                ["Shirt"] = ("Shirts", "shirt.json"),
                ["Pants"] = ("Pants", "pants.json"),
                ["Shoes"] = ("Shoes", "shoes.json"),
            };

            foreach (string id in AgentBodyPreset.Dr4co(enable: true).Values)
            {
                string[] parts = id.Split('/');
                Assert.Equal(3, parts.Length);
                (string folder, string json) = folders[parts[1]];
                string path = Path.Combine(pack, folder, parts[2], json);
                Assert.True(File.Exists(path), $"Falta {path} para {id}");
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
                Assert.Equal(parts[2], doc.RootElement.GetProperty("Name").GetString());
            }
        }

        private static string FindRepoRoot()
        {
            for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "agents", "Dr4co.FashionSense")))
                    return dir.FullName;
            }

            throw new DirectoryNotFoundException("Could not locate the Dr4co Fashion Sense pack.");
        }
    }
}
