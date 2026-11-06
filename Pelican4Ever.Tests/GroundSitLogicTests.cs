using Pelican4Ever.Core.Players;
using Xunit;

namespace Pelican4Ever.Tests;

public class GroundSitLogicTests
{
    private static readonly GroundSitConditions Free = new(false, false, false, false, false, false);

    [Theory]
    [InlineData(0, 62, false)]
    [InlineData(1, 58, false)]
    [InlineData(2, 29, false)]
    [InlineData(3, 58, true)]
    public void SittingPose_DependsOnFacingDirection(int direction, int frame, bool flipped)
    {
        Assert.Equal(new FarmerFrame(frame, flipped), GroundSitLogic.SittingPoseFor(direction));
    }

    [Fact]
    public void CanSit_OnlyWhenFreeAndNothingBlocks()
    {
        Assert.True(GroundSitLogic.CanSit(playerFree: true, Free));
        Assert.False(GroundSitLogic.CanSit(playerFree: false, Free));
        Assert.False(GroundSitLogic.CanSit(true, Free with { Swimming = true }));
        Assert.False(GroundSitLogic.CanSit(true, Free with { RidingHorse = true }));
        Assert.False(GroundSitLogic.CanSit(true, Free with { Eating = true }));
        Assert.False(GroundSitLogic.CanSit(true, Free with { UsingTool = true }));
        Assert.False(GroundSitLogic.CanSit(true, Free with { InFurnitureSeat = true }));
        Assert.False(GroundSitLogic.CanSit(true, Free with { EventUp = true }));
    }

    [Fact]
    public void ShouldStandUp_WhenMovingHitWarpedOrBlocked()
    {
        Assert.False(GroundSitLogic.ShouldStandUp(Free, isMoving: false, tookDamage: false, changedLocation: false));
        Assert.True(GroundSitLogic.ShouldStandUp(Free, isMoving: true, tookDamage: false, changedLocation: false));
        Assert.True(GroundSitLogic.ShouldStandUp(Free, isMoving: false, tookDamage: true, changedLocation: false));
        Assert.True(GroundSitLogic.ShouldStandUp(Free, isMoving: false, tookDamage: false, changedLocation: true));
        Assert.True(GroundSitLogic.ShouldStandUp(Free with { UsingTool = true }, false, false, false));
    }

    [Fact]
    public void SittingState_CarriesPoseForBroadcast()
    {
        GroundSitState state = GroundSitLogic.SittingState(playerId: 42, facingDirection: 3);

        Assert.Equal((42L, true, 3, 58, true), (state.PlayerId, state.IsSitting, state.Direction, state.Frame, state.Flipped));
    }
}
