using LibGit2Sharp;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace MinVer.Lib;

internal class LibGit : IGit
{
    public bool IsWorkingDirectory(string directory, ILogger log)
    {
        var repositoryPath = Repository.Discover(directory);
        if (repositoryPath == null)
        {
            return false;
        }

        using var repository = new Repository(repositoryPath);

        var status = repository.RetrieveStatus();
        return true;
    }

    public bool TryGetHead(string directory, [NotNullWhen(returnValue: true)] out Commit? head, ILogger log)
    {
        head = null;

        var repositoryPath = Repository.Discover(directory);
        if (repositoryPath == null)
        {
            return false;
        }

        using var repository = new Repository(repositoryPath);

        var headBranch = repository.Head;

        var headCommit = headBranch.Commits.FirstOrDefault();
        if (headCommit == null)
        {
            return false;
        }

        var headCommitSha = headCommit.Sha;

        head = new Commit(headCommitSha);
        head.Parents.AddRange(headCommit.Parents.Select(p =>
        {
            var pc = new Commit(p.Sha);
            pc.Parents.AddRange(p.Parents.Select(pp => new Commit(pp.Sha)));
            return pc;
        }));

        return true;
    }

    public IEnumerable<(string Name, string Sha)> GetTags(string directory, ILogger log)
    {
        var repositoryPath = Repository.Discover(directory);
        if (repositoryPath == null)
        {
            return Enumerable.Empty<(string, string)>();
        }

        using var repository = new Repository(repositoryPath);

        var tags = repository.Tags;

        //var listOfTags = tags.Select(tag => (Name: tag.FriendlyName, Sha: tag.Reference.TargetIdentifier));
        var listOfTags = tags.SelectMany(tag =>
        {
            var distinctTargets = new[] { tag.Reference.TargetIdentifier, tag.Target.Sha }.Distinct();
            return distinctTargets.Select(target => (Name: tag.FriendlyName, Sha: target));
        });
        return listOfTags.ToList(); // return a copy because repository will be disposed
    }
}
