using System.Collections.Generic;
using Avalonia.Controls;
using Enigma.GitClient.Core.Screens;
using Enigma.GitClient.Desktop.Services;

namespace Enigma.GitClient.Desktop.UnitTests.Infrastructure;

/// <summary>
/// An <see cref="IScreenReader"/> answering with a desktop the test lays out — two screens at different
/// scalings, one, or none at all — instead of the headless platform's own.
/// </summary>
/// <param name="screens">The screens every read returns.</param>
public sealed class FixedScreenReader(params ScreenInfo[] screens) : IScreenReader
{
    /// <inheritdoc />
    public IReadOnlyList<ScreenInfo> Read(Window window) => screens;
}
