using System;

namespace Enigma.GitClient.Desktop.Services;

/// <summary>
/// What changed in a repository on disk, as far as what the application shows is concerned.
/// </summary>
/// <remarks>
/// Two kinds, because they cost two different reads: a file in the working tree only changes what
/// <c>git status</c> says, while HEAD, a reference or the index changes the reference state the whole
/// shell is drawn from.
/// </remarks>
[Flags]
public enum RepositoryChanges
{
    /// <summary>Nothing the application shows.</summary>
    None = 0,

    /// <summary>A file in the working tree: what <c>git status</c> reports may have changed.</summary>
    WorkingTree = 1,

    /// <summary>
    /// HEAD, a reference, the index or the state of a merge: the reference state has to be read again,
    /// and the working tree with it.
    /// </summary>
    References = 2,

    /// <summary>Anything at all — events were lost — so everything is read again.</summary>
    Everything = WorkingTree | References,
}
