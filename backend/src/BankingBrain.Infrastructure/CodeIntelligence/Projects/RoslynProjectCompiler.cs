using System.Text.RegularExpressions;
using BankingBrain.Application.Common.CodeIntelligence.Projects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace BankingBrain.Infrastructure.CodeIntelligence.Projects;

/// <summary>
/// Shared Roslyn compilation-building logic, extracted verbatim from
/// <see cref="RoslynCodeRepositoryAnalyzer"/> so both it and
/// <see cref="BankingBrain.Infrastructure.ApiIntelligence.ApiEndpointDiscoveryAnalyzer"/>
/// compile the exact same repository the exact same way (dependency-ordered projects,
/// transitive project-reference closures, framework/package reference resolution) without
/// duplicating that logic or risking the two analyzers silently drifting apart.
/// </summary>
internal static class RoslynProjectCompiler
{
    private static readonly Lazy<IReadOnlyList<MetadataReference>> FrameworkReferences =
        new(LoadFrameworkReferences);

    /// <summary>Builds one <see cref="CSharpCompilation"/> per project, in dependency order, each referencing every project it transitively depends on.</summary>
    public static (IReadOnlyList<CodeProjectCompilation> OrderedProjects, IReadOnlyDictionary<string, CSharpCompilation> Compilations) Build(
        CodeRepositoryCompilation compilationPlan,
        CancellationToken cancellationToken = default)
    {
        var orderedProjects = OrderByDependencies(compilationPlan.Projects, out var transitiveReferences);

        var compilations = new Dictionary<string, CSharpCompilation>(StringComparer.OrdinalIgnoreCase);

        foreach (var project in orderedProjects)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var compilation = CreateCompilation(
                project,
                compilations,
                transitiveReferences[project.ProjectFilePath]);

            compilations[project.ProjectFilePath] = compilation;
        }

        return (orderedProjects, compilations);
    }

    private static CSharpCompilation CreateCompilation(
        CodeProjectCompilation project,
        IReadOnlyDictionary<string, CSharpCompilation> builtProjects,
        IReadOnlyCollection<string> transitiveReferencePaths)
    {
        var preprocessorSymbols = GetPreprocessorSymbols(project.TargetFramework);

        // Discovery and analysis are two separate passes over a live working copy, so
        // a file can vanish, become unreadable, or be mid-write by the time it's
        // actually read here. One such file must not abort compilation (and therefore
        // analysis) of every other file in the project - it is simply excluded, the
        // same graceful-degradation contract already applied to unresolved references.
        var syntaxTrees = project.SourceFiles
            .Select(sourceFile => TryParseSourceFile(sourceFile, preprocessorSymbols))
            .Where(tree => tree is not null)
            .Select(tree => tree!)
            .ToArray();

        var references = new List<MetadataReference>();

        var resolvedFrameworkReferences = ExternalReferenceResolver.ResolveFrameworkReferences(
            project.TargetFramework,
            project.FrameworkReferences ?? []);

        // Ref-pack assemblies and the analyzer host's own runtime assemblies must
        // never be mixed for the same framework - both define the same BCL types,
        // and Roslyn would treat them as ambiguous duplicate symbols. Prefer the
        // project's actual declared target framework when its ref pack is
        // installed; otherwise fall back to the host runtime as before.
        references.AddRange(
            resolvedFrameworkReferences.Count > 0
                ? resolvedFrameworkReferences
                : FrameworkReferences.Value);

        references.AddRange(
            ExternalReferenceResolver.ResolvePackageReferences(
                project.ProjectFilePath,
                project.TargetFramework));

        foreach (var referencePath in transitiveReferencePaths)
        {
            if (builtProjects.TryGetValue(referencePath, out var referencedCompilation))
            {
                references.Add(referencedCompilation.ToMetadataReference());
            }
        }

        return CSharpCompilation.Create(
            project.ProjectName,
            syntaxTrees,
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static SyntaxTree? TryParseSourceFile(
        string filePath,
        IReadOnlyList<string> preprocessorSymbols)
    {
        try
        {
            return ParseSourceFile(filePath, preprocessorSymbols);
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static SyntaxTree ParseSourceFile(
        string filePath,
        IReadOnlyList<string> preprocessorSymbols)
    {
        var sourceText = File.ReadAllText(filePath);

        var parseOptions = preprocessorSymbols.Count == 0
            ? CSharpParseOptions.Default
            : CSharpParseOptions.Default.WithPreprocessorSymbols(preprocessorSymbols);

        return CSharpSyntaxTree.ParseText(
            sourceText,
            parseOptions,
            path: filePath);
    }

    /// <summary>
    /// Derives the preprocessor symbols the SDK itself would define for a target
    /// framework moniker (e.g. "net8.0" -&gt; NET, NET8_0, NET8_0_OR_GREATER, ...),
    /// so `#if`-gated code compiles the branch that actually applies to the
    /// project's declared TFM instead of always following the analyzer host's own
    /// runtime. This is a best-effort approximation of the well-known, common TFM
    /// shapes - not a reproduction of MSBuild's full TFM/RID resolution.
    /// </summary>
    private static IReadOnlyList<string> GetPreprocessorSymbols(string? targetFramework)
    {
        if (string.IsNullOrWhiteSpace(targetFramework))
            return [];

        var tfm = targetFramework.Trim().ToLowerInvariant();
        var symbols = new List<string>();

        var modernNetMatch = Regex.Match(tfm, @"^net(\d+)\.(\d+)$");

        if (modernNetMatch.Success)
        {
            var major = int.Parse(modernNetMatch.Groups[1].Value);

            symbols.Add("NET");
            symbols.Add("NETCOREAPP");

            for (var version = 5; version <= major; version++)
            {
                symbols.Add($"NET{version}_0_OR_GREATER");
            }

            symbols.Add($"NET{major}_0");

            return symbols.Distinct().ToArray();
        }

        var netStandardMatch = Regex.Match(tfm, @"^netstandard(\d+)\.(\d+)$");

        if (netStandardMatch.Success)
        {
            var major = int.Parse(netStandardMatch.Groups[1].Value);
            var minor = int.Parse(netStandardMatch.Groups[2].Value);

            symbols.Add("NETSTANDARD");
            symbols.Add($"NETSTANDARD{major}_{minor}");

            foreach (var (candidateMajor, candidateMinor) in StandardVersionsUpTo(major, minor))
            {
                symbols.Add($"NETSTANDARD{candidateMajor}_{candidateMinor}_OR_GREATER");
            }

            return symbols.Distinct().ToArray();
        }

        var netCoreAppMatch = Regex.Match(tfm, @"^netcoreapp(\d+)\.(\d+)$");

        if (netCoreAppMatch.Success)
        {
            symbols.Add("NETCOREAPP");
            symbols.Add($"NETCOREAPP{netCoreAppMatch.Groups[1].Value}_{netCoreAppMatch.Groups[2].Value}");

            return symbols.Distinct().ToArray();
        }

        if (tfm.StartsWith("net4", StringComparison.Ordinal))
        {
            symbols.Add("NETFRAMEWORK");
            symbols.Add(tfm.ToUpperInvariant());

            return symbols.Distinct().ToArray();
        }

        return [];
    }

    private static IEnumerable<(int Major, int Minor)> StandardVersionsUpTo(int major, int minor)
    {
        (int Major, int Minor)[] versions =
        [
            (1, 0), (1, 1), (1, 2), (1, 3), (1, 4), (1, 5), (1, 6), (2, 0), (2, 1)
        ];

        return versions.Where(version =>
            version.Major < major || (version.Major == major && version.Minor <= minor));
    }

    /// <summary>
    /// Orders projects so dependencies compile before dependents, and computes each
    /// project's transitive project-reference closure (a project's own .csproj only
    /// lists its direct references, but a dependent needs every referenced project's
    /// compilation, transitively, to resolve types that flow through the reference
    /// chain - the way MSBuild would flow them at build time).
    /// </summary>
    private static IReadOnlyList<CodeProjectCompilation> OrderByDependencies(
        IReadOnlyList<CodeProjectCompilation> projects,
        out Dictionary<string, HashSet<string>> transitiveReferences)
    {
        var byPath = projects.ToDictionary(
            x => x.ProjectFilePath,
            StringComparer.OrdinalIgnoreCase);

        var ordered = new List<CodeProjectCompilation>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var closures = new Dictionary<string, HashSet<string>>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var project in projects)
        {
            Visit(project, byPath, visited, new HashSet<string>(StringComparer.OrdinalIgnoreCase), ordered, closures);
        }

        transitiveReferences = closures;
        return ordered;
    }

    private static void Visit(
        CodeProjectCompilation project,
        IReadOnlyDictionary<string, CodeProjectCompilation> byPath,
        HashSet<string> visited,
        HashSet<string> inProgress,
        List<CodeProjectCompilation> ordered,
        Dictionary<string, HashSet<string>> closures)
    {
        if (!visited.Add(project.ProjectFilePath) ||
            !inProgress.Add(project.ProjectFilePath))
        {
            return;
        }

        var closure = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var referencePath in project.ProjectReferences)
        {
            if (!byPath.TryGetValue(referencePath, out var referencedProject))
            {
                continue;
            }

            Visit(referencedProject, byPath, visited, inProgress, ordered, closures);

            closure.Add(referencePath);

            if (closures.TryGetValue(referencePath, out var referencedClosure))
            {
                closure.UnionWith(referencedClosure);
            }
        }

        closures[project.ProjectFilePath] = closure;
        ordered.Add(project);
        inProgress.Remove(project.ProjectFilePath);
    }

    private static IReadOnlyList<MetadataReference> LoadFrameworkReferences()
    {
        var trustedPlatformAssemblies =
            AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;

        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            return [];
        }

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Where(File.Exists)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToArray();
    }
}
