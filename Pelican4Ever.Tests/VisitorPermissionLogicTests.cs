using System.Collections.Generic;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class VisitorPermissionLogicTests
    {
        [Theory]
        [InlineData("Pelican4Ever_Farm_03", "Pelican4Ever_Farm_03")]
        [InlineData("Pelican4Ever_Farm_03_Cave", "Pelican4Ever_Farm_03")]
        [InlineData("Pelican4Ever_Farm_03_Cellar", "Pelican4Ever_Farm_03")]
        [InlineData("Pelican4Ever_Farm_03_Greenhouse", "Pelican4Ever_Farm_03")]
        [InlineData("Pelican4Ever_Farm_03_Home", "Pelican4Ever_Farm_03")]
        [InlineData("Pelican4Ever_Annex_03_East", "Pelican4Ever_Farm_03")]
        [InlineData("Farm", null)]
        [InlineData("Town", null)]
        [InlineData(null, null)]
        public void ResidenceSlotOf_MapsSubLocationsToTheirResidence(string? location, string? expected)
        {
            Assert.Equal(expected, VisitorPermissionLogic.ResidenceSlotOf(location));
        }

        [Fact]
        public void CanEdit_OwnerAlways_VisitorsOnlyWhenAllowed()
        {
            var door = new DoorAssignment { OwnerId = 10, Policy = AccessPolicy.Public };
            Assert.True(VisitorPermissionLogic.CanEdit(door, 10));
            Assert.False(VisitorPermissionLogic.CanEdit(door, 20));

            door.VisitorsCanEdit = true;
            Assert.True(VisitorPermissionLogic.CanEdit(door, 20));
        }

        [Theory]
        [InlineData(AccessPolicy.Locked, false, false)]
        [InlineData(AccessPolicy.Locked, true, false)]
        [InlineData(AccessPolicy.Friends, false, false)]
        [InlineData(AccessPolicy.Friends, true, true)]
        [InlineData(AccessPolicy.Public, false, true)]
        public void CanEnter_DependsOnPolicyAndFriendList(AccessPolicy policy, bool isFriend, bool expected)
        {
            var door = new DoorAssignment { OwnerId = 10, Policy = policy };
            if (isFriend)
                door.Friends.Add(20);

            Assert.Equal(expected, VisitorPermissionLogic.CanEnter(door, 20));
            Assert.True(VisitorPermissionLogic.CanEnter(door, 10));
        }

        [Fact]
        public void CanEdit_WithoutOwner_IsAllowed()
        {
            Assert.True(VisitorPermissionLogic.CanEdit(null, 20));
            Assert.True(VisitorPermissionLogic.CanEdit(new DoorAssignment { OwnerId = 0 }, 20));
        }

        [Theory]
        [InlineData(AccessPolicy.Locked, true, "cerrada")]
        [InlineData(AccessPolicy.Public, false, "abierta, visitas solo miran")]
        [InlineData(AccessPolicy.Public, true, "abierta, visitas pueden editar")]
        [InlineData(AccessPolicy.Friends, false, "solo amigos, visitas solo miran")]
        public void Describe_ReadsAccessAndEdit(AccessPolicy policy, bool canEdit, string expected)
        {
            Assert.Equal(expected, VisitorPermissionLogic.Describe(policy, canEdit));
        }

        [Theory]
        [InlineData(AccessPolicy.Locked, true, "closed")]
        [InlineData(AccessPolicy.Public, false, "open, visitors view only")]
        [InlineData(AccessPolicy.Public, true, "open, visitors can edit")]
        [InlineData(AccessPolicy.Friends, false, "friends only, visitors view only")]
        public void Describe_TranslatesInEnglishWhenProviderIsEnglish(AccessPolicy policy, bool canEdit, string expected)
        {
            var en = new Dictionary<string, string>
            {
                ["residence.permission.closed"] = "closed",
                ["residence.permission.friends_only"] = "friends only",
                ["residence.permission.open"] = "open",
                ["residence.permission.visitors_can_edit"] = "{{access}}, visitors can edit",
                ["residence.permission.visitors_view_only"] = "{{access}}, visitors view only"
            };
            var provider = new Pelican4Ever.Core.Localization.DictionaryTranslationProvider(en);
            Assert.Equal(expected, VisitorPermissionLogic.Describe(policy, canEdit, provider));
        }

        [Theory]
        [InlineData("Farm", true)]
        [InlineData("Pelican4Ever_Bridge_1", true)]
        [InlineData("Pelican4Ever_Bridge_3", true)]
        [InlineData("Pelican4Ever_Farm_01", false)]
        [InlineData("Town", false)]
        [InlineData(null, false)]
        public void IsProtectedPublicHub_IdentifiesPlazaAndBridges(string? locationName, bool expected)
        {
            Assert.Equal(expected, VisitorPermissionLogic.IsProtectedPublicHub(locationName));
        }

        [Fact]
        public void ChatSummary_ListsOnlyClaimedAndCountsFree()
        {
            var entries = new List<CatalogEntry>
            {
                new("door_01", "Farm", "Granja Host", 1, 1, "Pelican4Ever_Farm_01", "", true, "Juan", "Beach"),
                new("door_02", "Farm", "Granja Host", 2, 1, "Pelican4Ever_Farm_02", "", false, null, null),
                new("door_03", "Farm", "Granja Host", 3, 1, "Pelican4Ever_Farm_03", "", false, null, null),
            };

            var lines = PlazaCatalogFormatter.FormatChatSummary(entries);

            Assert.Equal(3, lines.Count);
            Assert.Equal("door_01: Juan, Beach - Granja Host", lines[1]);
            Assert.StartsWith("1 ocupadas, 2 libres.", lines[2]);
            Assert.DoesNotContain(lines, l => l.Contains("door_02"));
        }
    }
}
