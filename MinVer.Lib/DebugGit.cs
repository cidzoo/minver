using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace MinVer.Lib;

class DebugGit : IGit
{
    private readonly IGit installedGit = new InstalledGit();
    private readonly IGit libGit = new LibGit();

    public IEnumerable<(string Name, string Sha)> GetTags(string directory, ILogger log) =>
        this.installedGit.GetTags(directory, log);
    public bool IsWorkingDirectory(string directory, ILogger log) =>
        this.installedGit.IsWorkingDirectory(directory, log);
    public bool TryGetHead(string directory, [NotNullWhen(true)] out Commit? head, ILogger log) =>
        this.libGit.TryGetHead(directory, out head, log);
}
