using Chapi.Domain.Entities;
using System.IO;
using System.Text.RegularExpressions;

namespace Chapi.Infrastructure.Git;

/// <summary>
/// Parser de salidas de comandos Git.
/// Convierte texto plano de Git en entidades del dominio.
/// </summary>
public class GitOutputParser
{
    private const string FieldSeparator = "\x1f";
    private const string RecordSeparator = "\x1e";

    /// <summary>
    /// Parsea la salida de 'git log' en una lista de commits.
    /// </summary>
    public IEnumerable<GitCommit> ParseLogOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return Enumerable.Empty<GitCommit>();

        var commits = new List<GitCommit>();
        var records = output.Split(new[] { RecordSeparator }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var record in records)
        {
            var commit = ParseCommitRecord(record);
            if (commit != null)
                commits.Add(commit);
        }

        return commits;
    }

    private GitCommit? ParseCommitRecord(string record)
    {
        var parts = record.Trim().Trim('"').Split(new[] { FieldSeparator }, StringSplitOptions.None);

        if (parts.Length < 4)
            return null;

        return new GitCommit
        {
            Hash = parts[0],
            Author = parts[1],
            RelativeDate = parts[2],
            Message = parts[3],
            Description = parts.Length > 4 ? parts[4].Trim() : string.Empty
        };
    }

    /// <summary>
    /// Parsea la salida de 'git status --porcelain' en una lista de cambios.
    /// </summary>
    public IEnumerable<FileChange> ParseStatusOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return Enumerable.Empty<FileChange>();

        var changes = new List<FileChange>();
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var regex = new Regex(@"^(?<status>[A-Z\?]{1,2})\s+(?<file>.+)$");

        foreach (var line in lines)
        {
            var match = regex.Match(line.Trim());
            if (match.Success)
            {
                var status = match.Groups["status"].Value.Trim();
                var filePath = match.Groups["file"].Value.Trim()
                    .Replace('/', Path.DirectorySeparatorChar)
                    .Trim('"');

                changes.Add(new FileChange
                {
                    FilePath = filePath,
                    Status = MapStatus(status)
                });
            }
        }

        return changes;
    }

    private ChangeStatus MapStatus(string status)
    {
        return status.Trim() switch
        {
            "M" => ChangeStatus.Modified,
            "A" => ChangeStatus.Added,
            "D" => ChangeStatus.Deleted,
            "R" => ChangeStatus.Renamed,
            "??" => ChangeStatus.Untracked,
            "UU" or "AU" or "UA" => ChangeStatus.Conflict,
            _ => ChangeStatus.Modified
        };
    }

    /// <summary>
    /// Parsea la salida de 'git branch' en una lista de nombres de ramas.
    /// </summary>
    public IEnumerable<string> ParseBranchOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return Enumerable.Empty<string>();

        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().TrimStart('*').Trim())
            .Where(branch => !string.IsNullOrWhiteSpace(branch));
    }
    /// <summary>
    /// Parsea la salida de 'git diff --numstat' en un diccionario de cambios por archivo.
    /// </summary>
    public Dictionary<string, (int Additions, int Deletions)> ParseNumStatOutput(string output)
    {
        var stats = new Dictionary<string, (int Additions, int Deletions)>();

        if (string.IsNullOrWhiteSpace(output))
            return stats;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var parts = line.Split(new[] { '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
            {
                // numstat output: <adds> <dels> <path>
                // git diff --numstat uses "-" for binary files
                int adds = 0;
                int dels = 0;

                if (parts[0] != "-") int.TryParse(parts[0], out adds);
                if (parts[1] != "-") int.TryParse(parts[1], out dels);

                var path = parts[2].Trim().Replace('/', Path.DirectorySeparatorChar).Trim('"');

                if (!stats.ContainsKey(path))
                {
                    stats[path] = (adds, dels);
                }
            }
        }

        return stats;
    }
}
