using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BankingBrain.Application.Common.CodeIntelligence.Projects;

namespace BankingBrain.Infrastructure.CodeIntelligence.Projects;

public sealed class DotNetProjectDiscovery : ICodeProjectDiscovery
{
    private static readonly string[] IgnoredDirectories =
    [
        ".git",
        "bin",
        "obj",
        "node_modules",
        ".vs"
    ];

    public CodeRepositoryStructure Discover(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var root = Path.GetFullPath(repositoryRoot);

        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                $"Repository root does not exist: {root}");
        }

        var projectFiles = EnumerateFiles(
            root,
            "*.csproj");

        // SDK-style default item globs exclude any subtree that is itself the
        // location of another project file, so a parent project's implicit
        // glob never also pulls in a sibling/nested project's own sources.
        var projectDirectories = projectFiles
            .Select(path => Path.GetDirectoryName(path) ?? root)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // A single malformed/corrupted .csproj (truncated XML, unresolved merge-conflict
        // markers, a stray encoding issue) must not take down discovery for every other,
        // perfectly valid project in the repository - it is simply excluded, the same way
        // an unresolved external reference degrades gracefully elsewhere in this pipeline.
        var projects = projectFiles
            .Select(path => TryParseProject(root, path, projectDirectories))
            .Where(project => project is not null)
            .Select(project => project!)
            .ToArray();

        var solutionFiles = EnumerateSolutionFiles(root);

        var assignedFiles = projects
            .SelectMany(x => x.SourceFiles)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var allCSharpFiles = EnumerateFiles(root, "*.cs");

        var unassignedFiles = allCSharpFiles
            .Where(x => !assignedFiles.Contains(x))
            .ToArray();

        return new CodeRepositoryStructure(
            root,
            projects,
            solutionFiles,
            unassignedFiles);
    }

    /// <summary>
    /// Resolves a project's effective set of compiled .cs files without invoking
    /// MSBuild: applies the SDK's implicit default-item glob (unless disabled via
    /// EnableDefaultItems/EnableDefaultCompileItems), then applies explicit
    /// &lt;Compile Remove&gt; items, then adds explicit &lt;Compile Include&gt; items
    /// (each with its own optional Exclude), processed in document order - the
    /// same additive/subtractive sequencing MSBuild itself uses for item groups.
    /// &lt;Compile Update&gt; items only carry metadata and never add/remove files,
    /// so they are intentionally ignored here.
    /// </summary>
    private static CodeProject? TryParseProject(
        string repositoryRoot,
        string projectFile,
        IReadOnlyCollection<string> projectDirectories)
    {
        try
        {
            return ParseProject(repositoryRoot, projectFile, projectDirectories);
        }
        catch (Exception exception)
            when (exception is System.Xml.XmlException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static CodeProject ParseProject(
        string repositoryRoot,
        string projectFile,
        IReadOnlyCollection<string> projectDirectories)
    {
        var document = XDocument.Load(projectFile);

        var projectDirectory =
            Path.GetDirectoryName(projectFile)
            ?? repositoryRoot;

        var projectName =
            Path.GetFileNameWithoutExtension(projectFile);

        var nestedProjectDirectories = projectDirectories
            .Where(directory => !string.Equals(directory, projectDirectory, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var sourceFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (IsDefaultCompileItemsEnabled(document))
        {
            foreach (var file in EnumerateFiles(projectDirectory, "*.cs"))
            {
                if (IsUnderAnyDirectory(file, nestedProjectDirectories))
                    continue;

                sourceFiles.Add(file);
            }
        }

        foreach (var compileItem in document.Descendants().Where(x => x.Name.LocalName == "Compile"))
        {
            var removeValue = (string?)compileItem.Attribute("Remove");

            if (!string.IsNullOrWhiteSpace(removeValue))
            {
                foreach (var pattern in SplitItemValues(removeValue))
                foreach (var file in ExpandItemPattern(projectDirectory, pattern))
                    sourceFiles.Remove(file);
            }

            var includeValue = (string?)compileItem.Attribute("Include");

            if (string.IsNullOrWhiteSpace(includeValue))
                continue;

            var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var pattern in SplitItemValues(includeValue))
            foreach (var file in ExpandItemPattern(projectDirectory, pattern))
                included.Add(file);

            var excludeValue = (string?)compileItem.Attribute("Exclude");

            if (!string.IsNullOrWhiteSpace(excludeValue))
            {
                foreach (var pattern in SplitItemValues(excludeValue))
                foreach (var file in ExpandItemPattern(projectDirectory, pattern))
                    included.Remove(file);
            }

            sourceFiles.UnionWith(included);
        }

        var projectReferences = document
            .Descendants()
            .Where(x => x.Name.LocalName == "ProjectReference")
            .Select(x => (string?)x.Attribute("Include"))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => ResolveProjectPath(
                projectDirectory,
                x!))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new CodeProject(
            projectName,
            projectFile,
            sourceFiles
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            projectReferences,
            ParseTargetFramework(document),
            ParseFrameworkReferences(document));
    }

    /// <summary>
    /// Framework references (e.g. Microsoft.AspNetCore.App, Microsoft.WindowsDesktop.App)
    /// drive which SDK reference-assembly pack(s) ExternalReferenceResolver looks for, in
    /// addition to the always-implied Microsoft.NETCore.App. Covers explicit
    /// &lt;FrameworkReference&gt; items plus the SDKs that imply one implicitly.
    /// </summary>
    private static IReadOnlyList<string> ParseFrameworkReferences(XDocument document)
    {
        var frameworkReferences = new List<string>();

        var sdkAttribute = document.Root?.Attribute("Sdk")?.Value ?? string.Empty;

        if (sdkAttribute.Contains("Sdk.Web", StringComparison.OrdinalIgnoreCase))
            frameworkReferences.Add("Microsoft.AspNetCore.App");

        if (ParseBoolProperty(document, "UseWPF") == true ||
            ParseBoolProperty(document, "UseWindowsForms") == true)
        {
            frameworkReferences.Add("Microsoft.WindowsDesktop.App");
        }

        frameworkReferences.AddRange(
            document.Descendants()
                .Where(x => x.Name.LocalName == "FrameworkReference")
                .Select(x => (string?)x.Attribute("Include"))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim()));

        return frameworkReferences
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsDefaultCompileItemsEnabled(XDocument document)
    {
        if (ParseBoolProperty(document, "EnableDefaultItems") == false)
            return false;

        if (ParseBoolProperty(document, "EnableDefaultCompileItems") == false)
            return false;

        return true;
    }

    private static bool? ParseBoolProperty(XDocument document, string propertyName)
    {
        // MSBuild property evaluation is last-value-wins within a document.
        var value = document
            .Descendants()
            .LastOrDefault(x => x.Name.LocalName == propertyName)
            ?.Value;

        return bool.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string? ParseTargetFramework(XDocument document)
    {
        var single = document.Descendants()
            .FirstOrDefault(x => x.Name.LocalName == "TargetFramework")
            ?.Value;

        if (!string.IsNullOrWhiteSpace(single))
            return single.Trim();

        var multiple = document.Descendants()
            .FirstOrDefault(x => x.Name.LocalName == "TargetFrameworks")
            ?.Value;

        return string.IsNullOrWhiteSpace(multiple)
            ? null
            : SplitItemValues(multiple).FirstOrDefault();
    }

    private static bool IsUnderAnyDirectory(string filePath, IReadOnlyCollection<string> directories)
    {
        foreach (var directory in directories)
        {
            var relative = Path.GetRelativePath(directory, filePath);

            if (relative != ".."
                && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !Path.IsPathRooted(relative))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> SplitItemValues(string value)
    {
        return value.Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Resolves one MSBuild item value to actual files under the project directory.
    /// A literal path (no wildcard) is resolved directly; a pattern containing '*'
    /// or '?' is matched against every .cs file in the project tree using MSBuild's
    /// glob semantics ('**' = any depth, '*' = any characters within one segment).
    /// </summary>
    private static IEnumerable<string> ExpandItemPattern(string projectDirectory, string itemValue)
    {
        var normalized = itemValue.Replace('\\', '/');

        if (!normalized.Contains('*') && !normalized.Contains('?'))
        {
            var literal = ResolveProjectPath(projectDirectory, itemValue);
            return File.Exists(literal) ? [literal] : [];
        }

        var pattern = BuildGlobRegex(normalized);

        return EnumerateFiles(projectDirectory, "*.cs")
            .Where(file => pattern.IsMatch(
                Path.GetRelativePath(projectDirectory, file).Replace('\\', '/')));
    }

    private static Regex BuildGlobRegex(string globPattern)
    {
        var regex = new StringBuilder("^");

        for (var i = 0; i < globPattern.Length; i++)
        {
            var current = globPattern[i];

            if (current == '*' && i + 1 < globPattern.Length && globPattern[i + 1] == '*')
            {
                regex.Append(".*");
                i++;

                if (i + 1 < globPattern.Length && globPattern[i + 1] == '/')
                    i++;
            }
            else if (current == '*')
            {
                regex.Append("[^/]*");
            }
            else if (current == '?')
            {
                regex.Append("[^/]");
            }
            else
            {
                regex.Append(Regex.Escape(current.ToString()));
            }
        }

        regex.Append('$');

        return new Regex(regex.ToString(), RegexOptions.IgnoreCase);
    }

    private static string ResolveProjectPath(
        string projectDirectory,
        string relativePath)
    {
        // MSBuild Include/ProjectReference paths are conventionally authored with
        // backslashes (Windows-style), which Path.Combine/GetFullPath only treats
        // as a directory separator on Windows. On Linux/macOS a backslash is just
        // an ordinary filename character, so an unnormalized "..\Foo\Bar.csproj"
        // silently fails to resolve to any real file - which, left unfixed, made
        // every ProjectReference (and any backslash-using Compile Include/Remove)
        // invisible to discovery on non-Windows hosts, degrading multi-project
        // analysis to per-project isolation with no cross-project references at all.
        return Path.GetFullPath(
            Path.Combine(projectDirectory, relativePath.Replace('\\', '/')));
    }

    private static IReadOnlyList<string> EnumerateSolutionFiles(
        string root)
    {
        return EnumerateFiles(root, "*.*")
            .Where(x =>
                x.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                x.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> EnumerateFiles(
        string root,
        string pattern)
    {
        return Directory
            .EnumerateFiles(
                root,
                pattern,
                SearchOption.AllDirectories)
            .Where(IsAllowedPath)
            .ToArray();
    }

    private static bool IsAllowedPath(string path)
    {
        var segments = path.Split(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);

        return !segments.Any(segment =>
            IgnoredDirectories.Contains(
                segment,
                StringComparer.OrdinalIgnoreCase));
    }
}
