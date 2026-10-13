using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.CodeAnalysis;

namespace BankingBrain.Infrastructure.CodeIntelligence.Projects;

/// <summary>
/// Resolves external (framework and NuGet/package) MetadataReferences for a
/// project's declared target framework using only files the .NET SDK and NuGet
/// already produce on disk - the installed reference-assembly packs under
/// "packs/*.Ref" and the project's own restored "obj/project.assets.json" -
/// rather than invoking MSBuild/MSBuildWorkspace or a hardcoded assembly list.
/// Both are best-effort: when the relevant files aren't present (project not
/// restored, or no ref pack installed for the declared TFM), resolution simply
/// yields nothing for that source, and the caller falls back accordingly - a
/// project that references an unavailable assembly still analyzes, just with
/// that external symbol left unresolved rather than throwing.
/// </summary>
internal static class ExternalReferenceResolver
{
    public static IReadOnlyList<MetadataReference> ResolveFrameworkReferences(
        string? targetFramework,
        IReadOnlyList<string> frameworkReferenceNames)
    {
        if (string.IsNullOrWhiteSpace(targetFramework))
            return [];

        var dotnetRoot = FindDotNetRoot();

        if (dotnetRoot is null)
            return [];

        var packsRoot = Path.Combine(dotnetRoot, "packs");

        if (!Directory.Exists(packsRoot))
            return [];

        var packNames = new List<string> { "Microsoft.NETCore.App.Ref" };

        if (frameworkReferenceNames.Any(name => name.Contains("AspNetCore", StringComparison.OrdinalIgnoreCase)))
            packNames.Add("Microsoft.AspNetCore.App.Ref");

        if (frameworkReferenceNames.Any(name => name.Contains("WindowsDesktop", StringComparison.OrdinalIgnoreCase)))
            packNames.Add("Microsoft.WindowsDesktop.App.Ref");

        var references = new List<MetadataReference>();

        foreach (var packName in packNames)
        {
            var refDirectory = FindBestRefDirectory(
                Path.Combine(packsRoot, packName),
                targetFramework);

            if (refDirectory is null)
                continue;

            foreach (var assemblyPath in Directory.EnumerateFiles(refDirectory, "*.dll"))
            {
                AddReferenceIfLoadable(references, assemblyPath);
            }
        }

        return references;
    }

    public static IReadOnlyList<MetadataReference> ResolvePackageReferences(
        string projectFilePath,
        string? targetFramework)
    {
        var projectDirectory = Path.GetDirectoryName(projectFilePath);

        if (projectDirectory is null)
            return [];

        var assetsPath = Path.Combine(projectDirectory, "obj", "project.assets.json");

        if (!File.Exists(assetsPath))
            return [];

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(assetsPath));
            var root = document.RootElement;

            if (!root.TryGetProperty("targets", out var targets) ||
                !root.TryGetProperty("packageFolders", out var packageFoldersElement))
            {
                return [];
            }

            var targetLibraries = SelectTarget(targets, targetFramework);

            if (targetLibraries is null)
                return [];

            var packageFolders = packageFoldersElement
                .EnumerateObject()
                .Select(property => property.Name)
                .ToArray();

            var references = new List<MetadataReference>();

            foreach (var package in targetLibraries.Value.EnumerateObject())
            {
                if (!package.Value.TryGetProperty("compile", out var compileAssets))
                    continue;

                var separatorIndex = package.Name.IndexOf('/');

                if (separatorIndex < 0)
                    continue;

                var packageId = package.Name[..separatorIndex].ToLowerInvariant();
                var packageVersion = package.Name[(separatorIndex + 1)..];

                foreach (var compileAsset in compileAssets.EnumerateObject())
                {
                    if (compileAsset.Name.EndsWith("_._", StringComparison.Ordinal))
                        continue;

                    var assemblyPath = packageFolders
                        .Select(folder => Path.Combine(folder, packageId, packageVersion, compileAsset.Name))
                        .FirstOrDefault(File.Exists);

                    if (assemblyPath is not null)
                        AddReferenceIfLoadable(references, assemblyPath);
                }
            }

            return references;
        }
        catch (Exception exception)
            when (exception is JsonException or IOException or InvalidOperationException)
        {
            // project.assets.json can exist but be unexpectedly shaped (a different
            // NuGet client version, a partially-written file from an interrupted
            // restore) - EnumerateObject()/property access on a value that isn't the
            // expected JSON object type throws InvalidOperationException, not
            // JsonException. Per this resolver's contract, any of these are a
            // best-effort miss for this project's package references, not a reason
            // to fail the whole repository's extraction.
            return [];
        }
    }

    private static JsonElement? SelectTarget(JsonElement targets, string? targetFramework)
    {
        if (!string.IsNullOrWhiteSpace(targetFramework))
        {
            foreach (var target in targets.EnumerateObject())
            {
                if (target.Name.StartsWith(targetFramework, StringComparison.OrdinalIgnoreCase))
                    return target.Value;
            }
        }

        foreach (var target in targets.EnumerateObject())
            return target.Value;

        return null;
    }

    private static string? FindBestRefDirectory(string packDirectory, string targetFramework)
    {
        if (!Directory.Exists(packDirectory))
            return null;

        return Directory.EnumerateDirectories(packDirectory)
            .Select(versionDirectory => (
                RefDirectory: Path.Combine(versionDirectory, "ref", targetFramework),
                Version: TryParseVersion(Path.GetFileName(versionDirectory))))
            .Where(candidate => candidate.Version is not null && Directory.Exists(candidate.RefDirectory))
            .OrderByDescending(candidate => candidate.Version)
            .Select(candidate => candidate.RefDirectory)
            .FirstOrDefault();
    }

    private static Version? TryParseVersion(string value)
    {
        var corePart = value.Split('-')[0];
        return Version.TryParse(corePart, out var version) ? version : null;
    }

    private static string? FindDotNetRoot()
    {
        var runtimeDirectory = RuntimeEnvironment.GetRuntimeDirectory();

        var directory = new DirectoryInfo(runtimeDirectory);

        for (var depth = 0; depth < 5 && directory is not null; depth++)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "packs")))
                return directory.FullName;

            directory = directory.Parent;
        }

        return null;
    }

    private static void AddReferenceIfLoadable(List<MetadataReference> references, string assemblyPath)
    {
        try
        {
            references.Add(MetadataReference.CreateFromFile(assemblyPath));
        }
        catch (Exception exception)
            when (exception is IOException or BadImageFormatException or UnauthorizedAccessException)
        {
        }
    }
}
