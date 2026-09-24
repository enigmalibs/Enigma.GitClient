using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Enigma.GitClient.App.UnitTests.Infrastructure;
using Enigma.GitClient.App.ViewModels;
using Enigma.GitClient.App.Views;
using Enigma.Icons.Avalonia;
using Enigma.Icons.Phosphor;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// What a toolbar button looks like when it can be pressed and when it cannot: its glyph in the full
/// foreground inside a frame, or its glyph in the secondary grey and nothing else — and a row's own
/// action, which is a toolbar button too, without the frame.
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class ToolbarButtonLookTests
{
    private readonly HeadlessAvaloniaFixture _fixture;

    public ToolbarButtonLookTests(HeadlessAvaloniaFixture fixture) => _fixture = fixture;

    private static IBrush Brush(string key)
    {
        Application application = Application.Current!;

        Assert.True(application.TryFindResource(key, application.ActualThemeVariant, out object? brush), key);

        return Assert.IsAssignableFrom<IBrush>(brush);
    }

    private static ContentPresenter Presenter(TemplatedControl button)
        => button.GetVisualDescendants()
            .OfType<ContentPresenter>()
            .First(presenter => presenter.Name == "PART_ContentPresenter");

    private static Window Show(Control content)
    {
        Window window = new() { Content = content, Width = 400, Height = 200 };
        window.Show();
        window.UpdateLayout();

        return window;
    }

    private static Icon ToolbarIcon() => new() { Kind = PhosphorIcon.ArrowsClockwise, Classes = { "toolbar" } };

    /// <summary>
    /// Asserts the enabled look: the glyph in the full foreground, and a frame the pointer is not
    /// needed to see.
    /// </summary>
    private static void AssertEnabledLook(TemplatedControl button, Icon icon)
    {
        ContentPresenter presenter = Presenter(button);

        Assert.Same(Brush("EnigmaForegroundBrush"), icon.Foreground);
        Assert.Equal(new Thickness(1), presenter.BorderThickness);
        Assert.Same(Brush("EnigmaBorderBrush"), presenter.BorderBrush);
        Assert.Equal(1, button.Opacity);
    }

    /// <summary>
    /// Asserts the disabled look: the glyph in the secondary grey, no frame, and not faded.
    /// </summary>
    private static void AssertDisabledLook(TemplatedControl button, Icon icon)
    {
        ContentPresenter presenter = Presenter(button);

        Assert.Same(Brush("EnigmaForegroundSecondaryBrush"), icon.Foreground);
        Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(presenter.BorderBrush).Color);
        Assert.Equal(1, button.Opacity);
    }

    [Fact]
    public void AnEnabledToolbarButton_IsItsGlyphInFullInsideAFrame()
    {
        _fixture.Run(() =>
        {
            Icon icon = ToolbarIcon();
            Button button = new() { Classes = { "toolbar" }, Content = icon };
            Window window = Show(button);

            AssertEnabledLook(button, icon);

            window.Close();
        });
    }

    [Fact]
    public void ADisabledToolbarButton_IsItsGlyphInGreyWithNoFrameAndIsNotFaded()
    {
        _fixture.Run(() =>
        {
            Icon icon = ToolbarIcon();
            Button button = new() { Classes = { "toolbar" }, Content = icon, IsEnabled = false };
            Window window = Show(button);

            AssertDisabledLook(button, icon);

            // And it comes back when it can be pressed again.
            button.IsEnabled = true;
            window.UpdateLayout();

            AssertEnabledLook(button, icon);

            window.Close();
        });
    }

    [Fact]
    public void AToolbarButtonKeepsItsSize()
    {
        _fixture.Run(() =>
        {
            // The frame's pixel comes out of the padding, so a strip is exactly as tall as it was.
            Button button = new() { Classes = { "toolbar" }, Content = ToolbarIcon() };
            Window window = Show(button);

            Assert.Equal(new Thickness(6, 5), button.Padding);
            Assert.Equal(18 + (2 * 6), button.Bounds.Height);
            Assert.Equal(18 + (2 * 7), button.Bounds.Width);

            window.Close();
        });
    }

    [Fact]
    public void AToolbarButtonsText_IsInFullToo()
    {
        _fixture.Run(() =>
        {
            TextBlock text = new() { Text = "Stage all" };
            Button button = new() { Classes = { "toolbar" }, Content = text };
            Window window = Show(button);

            Assert.Same(Brush("EnigmaForegroundBrush"), text.Foreground);

            button.IsEnabled = false;
            window.UpdateLayout();

            Assert.Same(Brush("EnigmaForegroundSecondaryBrush"), text.Foreground);

            window.Close();
        });
    }

    [Fact]
    public void ARowsAction_HasNoFrameAndGreysWhenItCannotBePressed()
    {
        _fixture.Run(() =>
        {
            ListBox list = new()
            {
                ItemsSource = new[] { true, false },
                ItemTemplate = new FuncDataTemplate<bool>(
                    (enabled, _) => new Button
                    {
                        Classes = { "toolbar" },
                        IsEnabled = enabled,
                        Content = new Icon { Kind = PhosphorIcon.Trash, Classes = { "row" } },
                    },
                    supportsRecycling: false),
            };

            Window window = Show(list);

            Button Action(int index) => list.ContainerFromIndex(index)!
                .GetVisualDescendants()
                .OfType<Button>()
                .First();

            Icon Glyph(int index) => Action(index).GetVisualDescendants().OfType<Icon>().First();

            // A frame on every line of a list would be noise; the row action is its glyph alone.
            Assert.Equal(new Thickness(0), Action(0).BorderThickness);
            Assert.Equal(new Thickness(0), Presenter(Action(0)).BorderThickness);
            Assert.Same(Brush("EnigmaForegroundBrush"), Glyph(0).Foreground);

            // And one that cannot be pressed says so, the way a toolbar button does.
            Assert.Same(Brush("EnigmaForegroundSecondaryBrush"), Glyph(1).Foreground);
            Assert.Equal(1, Action(1).Opacity);

            window.Close();
        });
    }

    [Fact]
    public void ASegment_IsInFullInsideAFrameCheckedOrNot_AndGreyWhenDisabled()
    {
        _fixture.Run(() =>
        {
            Icon uncheckedIcon = ToolbarIcon();
            Icon checkedIcon = ToolbarIcon();
            Icon disabledIcon = ToolbarIcon();

            ToggleButton unchecked_ = new() { Classes = { "segment" }, Content = uncheckedIcon };
            ToggleButton checked_ = new() { Classes = { "segment" }, Content = checkedIcon, IsChecked = true };
            ToggleButton disabled = new() { Classes = { "segment" }, Content = disabledIcon, IsEnabled = false };

            Window window = Show(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { unchecked_, checked_, disabled },
            });

            AssertEnabledLook(unchecked_, uncheckedIcon);
            AssertEnabledLook(checked_, checkedIcon);
            Assert.Same(Brush("EnigmaSelectionBrush"), Presenter(checked_).Background);
            AssertDisabledLook(disabled, disabledIcon);

            window.Close();
        });
    }

    [Fact]
    public void TheRepositoryStrip_FramesWhatCanBePressedAndGreysWhatCannot()
    {
        _fixture.Run(() =>
        {
            using TestServices services = TestServices.Build();

            MainWindow window = services.Get<MainWindow>();
            window.DataContext = services.Get<MainWindowViewModel>();
            window.Show();
            window.UpdateLayout();

            Button Named(string name) => window.GetVisualDescendants()
                .OfType<Button>()
                .First(button => AutomationProperties.GetName(button) == name);

            Icon GlyphOf(Button button) => button.GetVisualDescendants().OfType<Icon>().First();

            // No repository is open: there is nothing to refresh, but a theme to switch.
            Button refresh = Named("Refresh everything");
            Button theme = Named("Switch between the dark and light themes");

            Assert.False(refresh.IsEffectivelyEnabled);
            AssertDisabledLook(refresh, GlyphOf(refresh));

            Assert.True(theme.IsEffectivelyEnabled);
            AssertEnabledLook(theme, GlyphOf(theme));

            window.Close();
        });
    }
}
