using System.Collections.Generic;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class FurnaceUnlockLogicTests
    {
        [Theory]
        [InlineData(true, true, false)]
        [InlineData(true, false, false)]
        [InlineData(false, false, false)]
        [InlineData(false, true, true)]
        public void ShouldSetCopperFound_OnlyWhenCopperExistsAndFlagIsMissing(
            bool alreadyHasMail,
            bool hasCopperOre,
            bool expected)
        {
            Assert.Equal(expected, FurnaceUnlockLogic.ShouldSetCopperFound(alreadyHasMail, hasCopperOre));
        }

        [Theory]
        [InlineData(true, false, 1159, false)]
        [InlineData(true, false, 1130, false)]
        [InlineData(true, false, 1200, true)]
        [InlineData(true, false, 1800, true)]
        [InlineData(true, true, 1800, false)]
        [InlineData(false, false, 1800, false)]
        public void ShouldGrantFallback_OnlyAfterEventWindowAndWithoutRecipe(
            bool hasOrWillReceiveCopperFoundMail,
            bool knowsFurnaceRecipe,
            int timeOfDay,
            bool expected)
        {
            Assert.Equal(expected, FurnaceUnlockLogic.ShouldGrantFallback(hasOrWillReceiveCopperFoundMail, knowsFurnaceRecipe, timeOfDay));
        }

        [Fact]
        public void ContainsCopperOre_FindsQualifiedId()
        {
            var ids = new List<string?> { "(O)388", "(O)378", "(O)390" };

            Assert.True(FurnaceUnlockLogic.ContainsCopperOre(ids));
        }

        [Theory]
        [InlineData("(O)388", "(O)390", "(O)378x")]
        [InlineData("378", "Copper Ore", "")]
        public void ContainsCopperOre_RejectsOtherIds(params string[] ids)
        {
            Assert.False(FurnaceUnlockLogic.ContainsCopperOre(ids));
        }

        [Fact]
        public void ContainsCopperOre_NullList_ReturnsFalse()
        {
            Assert.False(FurnaceUnlockLogic.ContainsCopperOre(null!));
        }
    }
}
