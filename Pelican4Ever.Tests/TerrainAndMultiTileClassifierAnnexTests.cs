using System;
using System.Collections.Generic;
using Pelican4Ever.Core.World;
using Xunit;

namespace Pelican4Ever.Tests
{
    public class TerrainAndMultiTileClassifierAnnexTests
    {
        [Fact]
        public void TerrainTileClassifier_CorrectlyIdentifiesForestShadeGrass()
        {
            // Sombra oscura de bosque que NUNCA debe colocarse en entradas
            Assert.True(TerrainTileClassifier.IsForestShadeGrass(326));
            Assert.True(TerrainTileClassifier.IsForestShadeGrass(351));
            Assert.True(TerrainTileClassifier.IsForestShadeGrass(376));
            Assert.True(TerrainTileClassifier.IsForestShadeGrass(401));

            // Pradera verde soleada abierta NO es sombra
            Assert.False(TerrainTileClassifier.IsForestShadeGrass(TerrainTileClassifier.SunnyGrassTileIndex));
            Assert.False(TerrainTileClassifier.IsForestShadeGrass(TerrainTileClassifier.SunnyGrassSoftTileIndex));
            Assert.False(TerrainTileClassifier.IsForestShadeGrass(TerrainTileClassifier.SunnyGrassThickTileIndex));
            Assert.False(TerrainTileClassifier.IsForestShadeGrass(TerrainTileClassifier.DirtPlainTileIndex));
        }

        [Fact]
        public void TerrainTileClassifier_CorrectlyIdentifiesWoodenBridgeTiles()
        {
            Assert.True(TerrainTileClassifier.IsWoodenBridgeTile(TerrainTileClassifier.WoodenBridgeMidTileIndex));
            Assert.True(TerrainTileClassifier.IsWoodenBridgeTile(TerrainTileClassifier.WoodenBridgeSouthLeftTileIndex));
            Assert.True(TerrainTileClassifier.IsWoodenBridgeTile(TerrainTileClassifier.WoodenBridgeSouthRightTileIndex));
            Assert.True(TerrainTileClassifier.IsWoodenBridgeTile(TerrainTileClassifier.WoodenBridgeRampLeftTileIndex));
            Assert.True(TerrainTileClassifier.IsWoodenBridgeTile(TerrainTileClassifier.WoodenBridgeRampRightTileIndex));
            Assert.True(TerrainTileClassifier.IsWoodenBridgeTile(TerrainTileClassifier.WoodenBridgeHorizMidTileIndex));
            Assert.True(TerrainTileClassifier.IsWoodenBridgeTile(TerrainTileClassifier.WoodenBridgeHorizEdgeTileIndex));

            // Suelo común no es puente
            Assert.False(TerrainTileClassifier.IsWoodenBridgeTile(TerrainTileClassifier.SunnyGrassTileIndex));
            Assert.False(TerrainTileClassifier.IsWoodenBridgeTile(TerrainTileClassifier.DirtPlainTileIndex));
        }

        [Fact]
        public void VerticalMultiTileClassifier_IdentifiesTreeTrunkBasesAndParts()
        {
            // Raíces y bases de robles/arces (168, 169) y pinos (129, 132)
            Assert.True(VerticalMultiTileClassifier.IsTreeTrunkBase(168));
            Assert.True(VerticalMultiTileClassifier.IsTreeTrunkBase(169));
            Assert.True(VerticalMultiTileClassifier.IsTreeTrunkBase(129));
            Assert.True(VerticalMultiTileClassifier.IsTreeTrunkBase(132));

            Assert.True(VerticalMultiTileClassifier.IsTreePart(168));
            Assert.True(VerticalMultiTileClassifier.IsTreePart(118)); // tronco superior

            // Valla no es tronco
            Assert.False(VerticalMultiTileClassifier.IsTreeTrunkBase(383));
            Assert.False(VerticalMultiTileClassifier.IsTreePart(383));
        }

        [Fact]
        public void VerticalMultiTileClassifier_CalculatesTreeBoundingBoxesAccurately()
        {
            // Árbol ancho de 2 casillas (168 en x=10, y=20)
            TileArea boxWide = VerticalMultiTileClassifier.GetStaticTreeBoundingBox(10, 20, 168);
            Assert.Equal(8, boxWide.X);
            Assert.Equal(15, boxWide.Y);
            Assert.Equal(6, boxWide.Width);
            Assert.Equal(6, boxWide.Height);
            Assert.True(boxWide.Contains(10, 20));
            Assert.True(boxWide.Contains(11, 20));
            Assert.True(boxWide.Contains(10, 16));

            // Árbol delgado de 1 casilla (129 en x=5, y=10)
            TileArea boxSlim = VerticalMultiTileClassifier.GetStaticTreeBoundingBox(5, 10, 129);
            Assert.Equal(4, boxSlim.X);
            Assert.Equal(6, boxSlim.Y);
            Assert.Equal(3, boxSlim.Width);
            Assert.Equal(5, boxSlim.Height);
            Assert.True(boxSlim.Contains(5, 10));
        }

        [Fact]
        public void VerticalMultiTileClassifier_IdentifiesBushesAndFences()
        {
            // Arbustos (63..65 base, 38..40 medio, 13..15 copa)
            Assert.True(VerticalMultiTileClassifier.IsBushTile(63));
            Assert.True(VerticalMultiTileClassifier.IsBushTile(39));
            Assert.True(VerticalMultiTileClassifier.IsBushTile(15));
            Assert.False(VerticalMultiTileClassifier.IsBushTile(175));

            // Vallas (383..386 base, 358..361 remate)
            Assert.True(VerticalMultiTileClassifier.IsFenceTile(383));
            Assert.True(VerticalMultiTileClassifier.IsFenceTile(358));
            Assert.True(VerticalMultiTileClassifier.IsFenceTile(436));
            Assert.False(VerticalMultiTileClassifier.IsFenceTile(175));
        }
    }
}
