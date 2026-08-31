using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

namespace SpatialCircuits.Core.Tests;

internal static class PureDependencyBoundary
{
    public static IReadOnlyList<string> InspectProjectGraph(
        IEnumerable<string> pureProjectPaths,
        IEnumerable<string> godotFacingAssemblyNames)
    {
        var normalizedPureProjects = pureProjectPaths
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var godotFacingNames = godotFacingAssemblyNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var violations = new List<string>();

        foreach (var projectPath in normalizedPureProjects.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var document = XDocument.Load(projectPath);
            var root = document.Root ?? throw new InvalidDataException($"Project has no root element: {projectPath}");
            var sdk = root.Attribute("Sdk")?.Value;

            if (sdk?.StartsWith("Godot.NET.Sdk", StringComparison.OrdinalIgnoreCase) == true)
            {
                violations.Add($"{projectPath} uses Godot-facing SDK {sdk}.");
            }

            foreach (var packageReference in root.Descendants("PackageReference"))
            {
                var packageName = packageReference.Attribute("Include")?.Value;
                if (packageName?.StartsWith("GodotSharp", StringComparison.OrdinalIgnoreCase) == true)
                {
                    violations.Add($"{projectPath} references package {packageName}.");
                }
            }

            foreach (var assemblyReference in root.Descendants("Reference"))
            {
                var assemblyName = assemblyReference.Attribute("Include")?.Value?.Split(',')[0];
                if (assemblyName is not null &&
                    (assemblyName.Equals("GodotSharp", StringComparison.OrdinalIgnoreCase) ||
                     godotFacingNames.Contains(assemblyName)))
                {
                    violations.Add($"{projectPath} references Godot-facing assembly {assemblyName}.");
                }
            }

            foreach (var projectReference in root.Descendants("ProjectReference"))
            {
                var include = projectReference.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(include))
                {
                    continue;
                }

                var referencedPath = Path.GetFullPath(
                    Path.Combine(Path.GetDirectoryName(projectPath)!, include));
                if (!normalizedPureProjects.Contains(referencedPath))
                {
                    violations.Add($"{projectPath} references non-pure project {referencedPath}.");
                }
            }
        }

        return violations;
    }

    public static IReadOnlyList<string> InspectCompiledAssemblies(
        IEnumerable<string> assemblyPaths,
        IEnumerable<string> godotFacingAssemblyNames)
    {
        var rejectedNames = godotFacingAssemblyNames
            .Append("GodotSharp")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var violations = new List<string>();

        foreach (var assemblyPath in assemblyPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);
            var metadataReader = peReader.GetMetadataReader();
            foreach (var handle in metadataReader.AssemblyReferences)
            {
                var reference = metadataReader.GetAssemblyReference(handle);
                var referenceName = metadataReader.GetString(reference.Name);
                if (rejectedNames.Contains(referenceName))
                {
                    violations.Add($"{assemblyPath} references Godot-facing assembly {referenceName}.");
                }
            }
        }

        return violations;
    }

    public static IReadOnlySet<string> FindGodotFacingAssemblyNames(string repositoryRoot)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var projectPath in Directory.EnumerateFiles(repositoryRoot, "*.csproj", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(projectPath))
            {
                continue;
            }

            var document = XDocument.Load(projectPath);
            var root = document.Root;
            var sdk = root?.Attribute("Sdk")?.Value;
            var hasGodotPackage = root?.Descendants("PackageReference").Any(reference =>
                reference.Attribute("Include")?.Value?.StartsWith(
                    "GodotSharp",
                    StringComparison.OrdinalIgnoreCase) == true) == true;

            if (sdk?.StartsWith("Godot.NET.Sdk", StringComparison.OrdinalIgnoreCase) != true && !hasGodotPackage)
            {
                continue;
            }

            var assemblyName = root?.Descendants("AssemblyName").SingleOrDefault()?.Value;
            names.Add(assemblyName ?? Path.GetFileNameWithoutExtension(projectPath));
        }

        return names;
    }

    private static bool IsBuildOutput(string path)
    {
        var segments = Path.GetRelativePath(Directory.GetCurrentDirectory(), path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }
}
