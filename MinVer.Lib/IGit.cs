using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace MinVer.Lib;

internal interface IGit
{
    IEnumerable<(string Name, string Sha)> GetTags(string directory, ILogger log);
    bool IsWorkingDirectory(string directory, ILogger log);
    bool TryGetHead(string directory, [NotNullWhen(true)] out Commit? head, ILogger log);
}
