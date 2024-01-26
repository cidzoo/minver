using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace MinVer.Lib;

class DebugGit : IGit
{
    private readonly IGit installedGit = new InstalledGit();
    private readonly IGit libGit = new LibGit();

    public IEnumerable<(string Name, string Sha)> GetTags(string directory, ILogger log)
    {
        var installed = this.installedGit.GetTags(directory, log);
        var lib = this.libGit.GetTags(directory, log);

        return lib;
    }
    public bool IsWorkingDirectory(string directory, ILogger log)
    {
        var installed =  this.installedGit.IsWorkingDirectory(directory, log);
        var lib = this.libGit.IsWorkingDirectory(directory, log);

        return lib;
    }
    public bool TryGetHead(string directory, [NotNullWhen(true)] out Commit? head, ILogger log)
    {

        var installed =  this.installedGit.TryGetHead(directory, out var installedHead, log);
        var lib = this.libGit.TryGetHead(directory, out var libHead, log);

        head = libHead;
        return lib;
    }
}
