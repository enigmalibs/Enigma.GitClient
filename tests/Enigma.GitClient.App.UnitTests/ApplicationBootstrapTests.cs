using Avalonia;
using Enigma.GitClient.App;
using Xunit;

namespace Enigma.GitClient.App.UnitTests;

/// <summary>
/// Smoke test proving the application's <see cref="AppBuilder"/> can be constructed, which is what
/// the headless render tests of later work items build on.
/// </summary>
public sealed class ApplicationBootstrapTests
{
    [Fact]
    public void BuildAvaloniaApp_ReturnsAConfiguredBuilder()
    {
        AppBuilder builder = Program.BuildAvaloniaApp();

        Assert.NotNull(builder);
        Assert.Equal(typeof(global::Enigma.GitClient.App.App), builder.ApplicationType);
    }
}
