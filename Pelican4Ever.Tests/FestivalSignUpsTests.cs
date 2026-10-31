using Pelican4Ever.Core.Festivals;
using Xunit;

namespace Pelican4Ever.Tests;

public class FestivalSignUpsTests
{
    private const string EggFestival = "festival_spring13";

    [Fact]
    public void StartRun_TakesSignedUpPlayersAndClosesTheList()
    {
        var signUps = new FestivalSignUps();
        signUps.SetSignedUp(EggFestival, 20L, join: true);
        signUps.SetSignedUp(EggFestival, 10L, join: true);

        FestivalRun? run = signUps.StartRun(EggFestival);

        Assert.NotNull(run);
        Assert.Equal(1, run!.RunId);
        Assert.Equal(new[] { 10L, 20L }, run.Participants);
        Assert.Empty(signUps.Participants);
    }

    [Fact]
    public void StartRun_WithNobodySignedUp_ReturnsNull()
    {
        Assert.Null(new FestivalSignUps().StartRun(EggFestival));
    }

    [Fact]
    public void SetSignedUp_RejectsPlayersWhoAlreadyPlayedToday()
    {
        var signUps = new FestivalSignUps();
        signUps.SetSignedUp(EggFestival, 10L, join: true);
        signUps.StartRun(EggFestival);

        Assert.Equal(SignUpResult.AlreadyPlayed, signUps.SetSignedUp(EggFestival, 10L, join: true));
        Assert.Equal(SignUpResult.Joined, signUps.SetSignedUp(EggFestival, 30L, join: true));
        Assert.True(signUps.HasPlayed(EggFestival, 10L));
    }

    [Fact]
    public void SetSignedUp_LeaveAndRepeatedJoinAreIdempotent()
    {
        var signUps = new FestivalSignUps();

        Assert.Equal(SignUpResult.Joined, signUps.SetSignedUp(EggFestival, 10L, join: true));
        Assert.Equal(SignUpResult.NoChange, signUps.SetSignedUp(EggFestival, 10L, join: true));
        Assert.Equal(SignUpResult.Left, signUps.SetSignedUp(EggFestival, 10L, join: false));
        Assert.Equal(SignUpResult.NoChange, signUps.SetSignedUp(EggFestival, 10L, join: false));
    }

    [Fact]
    public void SecondRun_GetsItsOwnGateIds()
    {
        var signUps = new FestivalSignUps();
        signUps.SetSignedUp(EggFestival, 10L, join: true);
        FestivalRun first = signUps.StartRun(EggFestival)!;
        signUps.SetSignedUp(EggFestival, 20L, join: true);
        FestivalRun second = signUps.StartRun(EggFestival)!;

        Assert.Equal("startContest#1", first.GateId("startContest"));
        Assert.Equal("startContest#2", second.GateId("startContest"));
    }

    [Fact]
    public void ANewFestival_StartsWithACleanList()
    {
        var signUps = new FestivalSignUps();
        signUps.SetSignedUp(EggFestival, 10L, join: true);
        signUps.StartRun(EggFestival);

        Assert.Equal(SignUpResult.Joined, signUps.SetSignedUp("festival_spring24", 10L, join: true));
        Assert.False(signUps.HasPlayed("festival_spring24", 10L));
    }
}
