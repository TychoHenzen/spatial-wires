using System.Reflection;
using System.Runtime.Versioning;
using Xunit;

namespace SpatialCircuits.Core.Tests;

public sealed class PureProjectBuildTests
{
    private static readonly string[] PureAssemblyNames =
    [
        "SpatialCircuits.Core",
        "SpatialCircuits.Cells",
        "SpatialCircuits.Hierarchy",
    ];

    // covers: spatial-circuits/package-baseline :: Enforced dependency boundary :: Pure projects build independently
    [Fact]
    public void PureProjectsBuildAsNet8AssembliesWithoutGodotSharp()
    {
        foreach (var assemblyName in PureAssemblyNames)
        {
            var assemblyPath = Path.Combine(AppContext.BaseDirectory, $"{assemblyName}.dll");

            Assert.True(File.Exists(assemblyPath), $"Pure assembly was not built: {assemblyPath}");

            var assembly = Assembly.LoadFrom(assemblyPath);
            var targetFramework = assembly.GetCustomAttribute<TargetFrameworkAttribute>();
            Assert.NotNull(targetFramework);
            Assert.Equal(".NETCoreApp,Version=v8.0", targetFramework.FrameworkName);
            Assert.DoesNotContain(
                assembly.GetReferencedAssemblies(),
                reference => string.Equals(reference.Name, "GodotSharp", StringComparison.Ordinal));
        }
    }
}
