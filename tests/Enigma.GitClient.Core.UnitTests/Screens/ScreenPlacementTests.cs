using System.Collections.Generic;
using Enigma.GitClient.Core.Screens;
using Xunit;

namespace Enigma.GitClient.Core.UnitTests.Screens;

/// <summary>
/// Covers which screen a window opens on — the remembered one, found again however the desktop was
/// rearranged, or the primary one when it is gone — and where on it a window is centred.
/// </summary>
public sealed class ScreenPlacementTests
{
    // A 1080p primary screen with a 40 px taskbar along its bottom, a 1440p one at 150 % to its right,
    // and a 1080p one to the left of the primary, at negative coordinates.
    private static readonly ScreenInfo Primary = new(new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), 1.0, IsPrimary: true);
    private static readonly ScreenInfo Right = new(new(1920, 0, 2560, 1440), new(1920, 0, 2560, 1400), 1.5, IsPrimary: false);
    private static readonly ScreenInfo Left = new(new(-1920, 0, 1920, 1080), new(-1920, 0, 1920, 1080), 1.0, IsPrimary: false);

    private static readonly IReadOnlyList<ScreenInfo> Desktop = [Primary, Right, Left];

    // ---------------------------------------------------------------- choosing

    [Fact]
    public void Choose_TheScreenWhoseBoundsAreTheHints()
    {
        Assert.Same(Right, ScreenPlacement.Choose(Desktop, Right.Bounds));
        Assert.Same(Left, ScreenPlacement.Choose(Desktop, Left.Bounds));
    }

    [Fact]
    public void Choose_AScreenThatChangedSize_IsTheOneHoldingTheHintsCentre()
    {
        // The right-hand screen was 1080p when it was remembered.
        ScreenArea before = new(1920, 0, 1920, 1080);

        Assert.Same(Right, ScreenPlacement.Choose(Desktop, before));
    }

    [Fact]
    public void Choose_WithTheCentreOnNoScreen_IsTheOneTheHintOverlapsMost()
    {
        // Mostly over the right-hand screen, with its centre below every screen.
        ScreenArea hint = new(2000, 900, 1000, 1200);

        Assert.Same(Right, ScreenPlacement.Choose(Desktop, hint));
    }

    [Fact]
    public void Choose_AScreenThatIsGone_FallsBackToThePrimary()
    {
        IReadOnlyList<ScreenInfo> unplugged = [Left, Primary];

        Assert.Same(Primary, ScreenPlacement.Choose(unplugged, Right.Bounds));
    }

    [Fact]
    public void Choose_WithNoHint_IsThePrimary()
    {
        Assert.Same(Primary, ScreenPlacement.Choose([Right, Primary], hint: null));
        Assert.Same(Primary, ScreenPlacement.Choose([Right, Primary], new ScreenArea(0, 0, 0, 0)));
    }

    [Fact]
    public void Choose_WithNoPrimary_IsTheFirst()
    {
        ScreenInfo second = Right with { };

        Assert.Same(Left, ScreenPlacement.Choose([Left, second], new ScreenArea(-50000, -50000, 10, 10)));
    }

    [Fact]
    public void Choose_WithNoScreenAtAll_IsNothing()
        => Assert.Null(ScreenPlacement.Choose([], Primary.Bounds));

    // ---------------------------------------------------------------- the window's screen

    [Fact]
    public void FromWindow_AWindowStraddlingTwoScreens_IsOnTheOneHoldingMostOfIt()
    {
        // 300 px on the primary, 700 on the right-hand screen.
        Assert.Same(Right, ScreenPlacement.FromWindow(Desktop, new ScreenArea(1620, 100, 1000, 600)));

        // 700 on the primary, 300 on the right.
        Assert.Same(Primary, ScreenPlacement.FromWindow(Desktop, new ScreenArea(1220, 100, 1000, 600)));
    }

    [Fact]
    public void FromWindow_AMaximisedWindow_ReachingPastItsScreensEdges_IsStillOnIt()
        => Assert.Same(Left, ScreenPlacement.FromWindow(Desktop, new ScreenArea(-1928, -8, 1936, 1056)));

    [Fact]
    public void FromWindow_AWindowOnNoScreen_IsOnThePrimary()
        => Assert.Same(Primary, ScreenPlacement.FromWindow(Desktop, new ScreenArea(100000, 100000, 800, 600)));

    [Fact]
    public void FromWindow_WithNoScreenAtAll_IsNothing()
        => Assert.Null(ScreenPlacement.FromWindow([], new ScreenArea(0, 0, 800, 600)));

    // ---------------------------------------------------------------- centring

    [Fact]
    public void CenterIn_ASmallerFrame_IsCentred()
        => Assert.Equal((410, 160), ScreenPlacement.CenterIn(new ScreenArea(0, 0, 1920, 1080), 1100, 760));

    [Fact]
    public void CenterIn_AnEqualFrame_SitsAtTheCorner()
        => Assert.Equal((0, 0), ScreenPlacement.CenterIn(new ScreenArea(0, 0, 1920, 1040), 1920, 1040));

    [Fact]
    public void CenterIn_ALargerFrame_KeepsItsCornerOnTheScreen()
    {
        Assert.Equal((0, 0), ScreenPlacement.CenterIn(new ScreenArea(0, 0, 1366, 728), 2160, 1350));

        // Larger one way only: centred the other way.
        Assert.Equal((0, 94), ScreenPlacement.CenterIn(new ScreenArea(0, 0, 1366, 728), 1500, 540));
    }

    [Fact]
    public void CenterIn_AWorkingAreaOffsetByATaskbar_IsCentredInWhatIsLeft()
        => Assert.Equal((410, 200), ScreenPlacement.CenterIn(new ScreenArea(0, 40, 1920, 1040), 1100, 720));

    [Fact]
    public void CenterIn_AScreenLeftOfThePrimary_HasNegativeCoordinates()
        => Assert.Equal((-1510, 180), ScreenPlacement.CenterIn(Left.WorkingArea, 1100, 720));

    [Fact]
    public void CenterIn_AtTheTargetScreensScaling_CentresAMixedDpiFrame()
    {
        // The start window's 1100 × 720 at the right-hand screen's 150 %.
        (int x, int y) = ScreenPlacement.CenterIn(Right.WorkingArea, (int)(1100 * Right.Scaling), (int)(720 * Right.Scaling));

        Assert.Equal((1920 + ((2560 - 1650) / 2), (1400 - 1080) / 2), (x, y));
    }

    // ---------------------------------------------------------------- the rectangle

    [Fact]
    public void ScreenArea_SharesNoPointWithTheScreenBesideIt()
    {
        Assert.True(Primary.Bounds.Contains(1919, 0));
        Assert.False(Primary.Bounds.Contains(1920, 0));
        Assert.True(Right.Bounds.Contains(1920, 0));
        Assert.Equal(0, Primary.Bounds.OverlapWith(Right.Bounds));
    }

    [Fact]
    public void ScreenArea_AnEmptyOneHoldsNothing()
    {
        Assert.True(new ScreenArea(0, 0, 0, 10).IsEmpty);
        Assert.False(new ScreenArea(0, 0, 0, 10).Contains(0, 0));
    }
}
