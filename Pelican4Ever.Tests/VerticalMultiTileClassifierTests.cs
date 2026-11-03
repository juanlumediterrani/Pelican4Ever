using System;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class VerticalMultiTileClassifierTests
    {
        [Theory]
        [InlineData(383, VerticalMultiTileKind.FenceLeftPost, 358)]
        [InlineData(384, VerticalMultiTileKind.FenceMiddleRail, 359)]
        [InlineData(434, VerticalMultiTileKind.FenceMiddleRail, 409)]
        [InlineData(385, VerticalMultiTileKind.FenceRightPost, 360)]
        [InlineData(436, VerticalMultiTileKind.FenceVerticalOrStandalone, 361)]
        [InlineData(435, VerticalMultiTileKind.Signpost, 410)]
        [InlineData(83, VerticalMultiTileKind.LampPost, 58)]
        [InlineData(408, VerticalMultiTileKind.Gate, 383)]
        [InlineData(63, VerticalMultiTileKind.Bush, 38)]
        [InlineData(64, VerticalMultiTileKind.Bush, 39)]
        [InlineData(65, VerticalMultiTileKind.Bush, 40)]
        [InlineData(38, VerticalMultiTileKind.Bush, 13)]
        [InlineData(39, VerticalMultiTileKind.Bush, 14)]
        [InlineData(40, VerticalMultiTileKind.Bush, 15)]
        [InlineData(113, VerticalMultiTileKind.Bush, 88)]
        [InlineData(114, VerticalMultiTileKind.Bush, 89)]
        [InlineData(126, VerticalMultiTileKind.Bush, 101)]
        [InlineData(168, VerticalMultiTileKind.TreeTrunk, 143)]
        [InlineData(169, VerticalMultiTileKind.TreeTrunk, 144)]
        [InlineData(143, VerticalMultiTileKind.TreeTrunk, 118)]
        [InlineData(144, VerticalMultiTileKind.TreeTrunk, 119)]
        [InlineData(129, VerticalMultiTileKind.TreeTrunk, 104)]
        [InlineData(104, VerticalMultiTileKind.TreeTrunk, 79)]
        public void TryGetExpectedTopTile_ReturnsMatchingUpperTileAndKind(
            int baseTileIndex,
            VerticalMultiTileKind expectedKind,
            int expectedTopTileIndex)
        {
            Assert.Equal(expectedKind, VerticalMultiTileClassifier.ClassifyOutdoorsBaseTile(baseTileIndex));
            Assert.True(VerticalMultiTileClassifier.TryGetExpectedTopTile(baseTileIndex, out int defaultTop, out var validTops));
            Assert.Equal(expectedTopTileIndex, defaultTop);
            Assert.Contains(expectedTopTileIndex, validTops);
        }

        [Fact]
        public void GetHorizontalFencePair_BuildsClosedContinuousSegment()
        {
            // Tramo x = 2..5 (como la valla norte del Paso Oeste de la Granja Host):
            // x=2 es extremo izquierdo (383/358 |--), x=3..4 es interior (384/359 --|--), x=5 es extremo derecho (385/360 --|).
            Assert.Equal((383, 358), VerticalMultiTileClassifier.GetHorizontalFencePair(2, startX: 2, endX: 5));
            Assert.Equal((384, 359), VerticalMultiTileClassifier.GetHorizontalFencePair(3, startX: 2, endX: 5));
            Assert.Equal((384, 359), VerticalMultiTileClassifier.GetHorizontalFencePair(4, startX: 2, endX: 5));
            Assert.Equal((385, 360), VerticalMultiTileClassifier.GetHorizontalFencePair(5, startX: 2, endX: 5));
        }

        [Fact]
        public void GetHorizontalFencePair_SinglePost_UsesStandalonePostWithoutSideRails()
        {
            // Un único poste aislado no debe dejar travesaños al aire (383/385), sino usar el poste cerrado (436/361).
            Assert.Equal((436, 361), VerticalMultiTileClassifier.GetHorizontalFencePair(5, startX: 5, endX: 5));
        }

        [Fact]
        public void GetFenceGatePosts_ReturnsRightEndForLeftFenceAndLeftStartForRightFence()
        {
            var (leftEndOfGap, rightStartOfGap) = VerticalMultiTileClassifier.GetFenceGatePosts();
            Assert.Equal((385, 360), leftEndOfGap);
            Assert.Equal((383, 358), rightStartOfGap);
        }

        [Fact]
        public void GetVerticalFenceBodyTile_BuildsVerticalFenceColumn()
        {
            Assert.Equal(386, VerticalMultiTileClassifier.GetVerticalFenceBodyTile(22, startY: 22, endY: 25));
            Assert.Equal(386, VerticalMultiTileClassifier.GetVerticalFenceBodyTile(23, startY: 22, endY: 25));
            Assert.Equal(411, VerticalMultiTileClassifier.GetVerticalFenceBodyTile(24, startY: 22, endY: 25));
            Assert.Equal(436, VerticalMultiTileClassifier.GetVerticalFenceBodyTile(25, startY: 22, endY: 25));
        }

        [Theory]
        [InlineData(0, 30, false)] // y = 0 no tiene fila y - 1 para el remate superior
        [InlineData(1, 30, true)]
        [InlineData(26, 30, true)]
        [InlineData(30, 30, false)]
        public void CanPlaceVerticalBaseAt_RejectsTopBorderY0(int y, int mapHeight, bool expected)
        {
            Assert.Equal(expected, VerticalMultiTileClassifier.CanPlaceVerticalBaseAt(y, mapHeight));
        }

        [Fact]
        public void HasValidHorizontalFenceNeighbors_ValidatesRailContinuity()
        {
            // 383 (|--) requiere valla a su derecha
            Assert.True(VerticalMultiTileClassifier.HasValidHorizontalFenceNeighbors(383, leftBuildingsTile: null, rightBuildingsTile: 384));
            Assert.False(VerticalMultiTileClassifier.HasValidHorizontalFenceNeighbors(383, leftBuildingsTile: 384, rightBuildingsTile: null));

            // 385 (--|) requiere valla a su izquierda
            Assert.True(VerticalMultiTileClassifier.HasValidHorizontalFenceNeighbors(385, leftBuildingsTile: 384, rightBuildingsTile: null));
            Assert.False(VerticalMultiTileClassifier.HasValidHorizontalFenceNeighbors(385, leftBuildingsTile: null, rightBuildingsTile: 384));

            // 384 (--|--) requiere valla a ambos lados
            Assert.True(VerticalMultiTileClassifier.HasValidHorizontalFenceNeighbors(384, leftBuildingsTile: 383, rightBuildingsTile: 385));
            Assert.False(VerticalMultiTileClassifier.HasValidHorizontalFenceNeighbors(384, leftBuildingsTile: null, rightBuildingsTile: 385));
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData(412, false)]
        [InlineData(413, false)]
        [InlineData(414, false)]
        [InlineData(360, true)]
        [InlineData(358, true)]
        [InlineData(96, true)]
        public void ShouldClearFrontOverMailboxPost_ClearsFenceCapsAndCanopyOverMailboxPost(int? frontTile, bool expectedClear)
        {
            Assert.Equal(expectedClear, VerticalMultiTileClassifier.ShouldClearFrontOverMailboxPost(frontTile));
        }

        [Fact]
        public void ExtendedMultiTileElements_LampPostAndGate_HaveExpectedPairing()
        {
            Assert.Equal(VerticalMultiTileKind.LampPost, VerticalMultiTileClassifier.ClassifyOutdoorsBaseTile(VerticalMultiTileClassifier.LampPostBaseTileIndex));
            Assert.True(VerticalMultiTileClassifier.TryGetExpectedTopTile(VerticalMultiTileClassifier.LampPostBaseTileIndex, out int lampTop, out _));
            Assert.Equal(VerticalMultiTileClassifier.LampPostTopTileIndex, lampTop);

            Assert.Equal(VerticalMultiTileKind.Gate, VerticalMultiTileClassifier.ClassifyOutdoorsBaseTile(VerticalMultiTileClassifier.GateBaseTileIndex));
            Assert.True(VerticalMultiTileClassifier.TryGetExpectedTopTile(VerticalMultiTileClassifier.GateBaseTileIndex, out int gateTop, out _));
            Assert.Equal(VerticalMultiTileClassifier.GateTopTileIndex, gateTop);
        }
    }
}
