using System.Diagnostics;
using Xunit;

namespace SpatialCircuits.Core.Tests;

public sealed class DependencyBoundaryTests
{
    private static readonly string[] PureProjectRelativePaths =
    [
        "src/SpatialCircuits.Core/SpatialCircuits.Core.csproj",
        "src/SpatialCircuits.Cells/SpatialCircuits.Cells.csproj",
        "src/SpatialCircuits.Hierarchy/SpatialCircuits.Hierarchy.csproj",
        "src/SpatialCircuits.Runner/SpatialCircuits.Runner.csproj",
    ];

    private static readonly string[] PureAssemblyNames =
    [
        "SpatialCircuits.Core",
        "SpatialCircuits.Cells",
        "SpatialCircuits.Hierarchy",
        "SpatialCircuits.Runner",
    ];

    [Fact]
    public void PureProjectsRejectGodotDependenciesInProjectGraphAndCompiledAssemblies()
    {
        var repositoryRoot = FindRepositoryRoot();
        var pureProjects = PureProjectRelativePaths
            .Select(path => Path.Combine(repositoryRoot, path))
            .ToArray();
        var pureAssemblies = PureAssemblyNames
            .Select(name => Path.Combine(AppContext.BaseDirectory, $"{name}.dll"))
            .ToArray();
        var godotFacingNames = PureDependencyBoundary.FindGodotFacingAssemblyNames(repositoryRoot);

        Assert.All(pureAssemblies, path => Assert.True(File.Exists(path), $"Missing pure assembly: {path}"));
        Assert.Empty(PureDependencyBoundary.InspectProjectGraph(pureProjects, godotFacingNames));
        Assert.Empty(PureDependencyBoundary.InspectCompiledAssemblies(pureAssemblies, godotFacingNames));
    }

    [Fact]
    public void ControlledFixtureDemonstratesProjectAndAssemblyRejection()
    {
        var repositoryRoot = FindRepositoryRoot();
        var fixtureRoot = Path.Combine(repositoryRoot, "tests", "fixtures", "dependency-boundary");
        var violatingProject = Path.Combine(fixtureRoot, "PureViolation", "PureViolation.csproj");
        var godotProject = Path.Combine(fixtureRoot, "GodotFacing", "GodotSharp.csproj");
        var artifactsPath = Path.Combine(Path.GetTempPath(), $"spatial-wires-boundary-{Guid.NewGuid():N}");

        try
        {
            var build = RunDotnetBuild(violatingProject, artifactsPath);
            Assert.True(build.ExitCode == 0, $"Fixture build failed.{Environment.NewLine}{build.Output}");

            var fixtureAssembly = Directory.GetFiles(artifactsPath, "PureViolation.dll", SearchOption.AllDirectories)
                .Single(path => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));
            var projectViolations = PureDependencyBoundary.InspectProjectGraph(
                [violatingProject],
                ["GodotSharp"]);
            var assemblyViolations = PureDependencyBoundary.InspectCompiledAssemblies(
                [fixtureAssembly],
                ["GodotSharp"]);

            Assert.Contains(projectViolations, violation => violation.Contains(godotProject, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(assemblyViolations, violation => violation.Contains("GodotSharp", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(artifactsPath))
            {
                Directory.Delete(artifactsPath, recursive: true);
            }
        }
    }

    private static (int ExitCode, string Output) RunDotnetBuild(string projectPath, string artifactsPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--artifacts-path");
        startInfo.ArgumentList.Add(artifactsPath);
        startInfo.ArgumentList.Add("--nologo");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start dotnet build.");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, standardOutput + standardError);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SpatialWires.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }
}
