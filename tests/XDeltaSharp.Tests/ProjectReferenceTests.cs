using System.Reflection;
using System.Runtime.Versioning;
using XDeltaSharp.Blake3;
using XDeltaSharp.Lzma;
using Xunit;

namespace XDeltaSharp.Tests;

public class ProjectReferenceTests
{
    [Fact]
    public void EachLibraryMatchesTheConsumerTargetFramework()
    {
        string expected = typeof(ProjectReferenceTests).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName;
        foreach (var library in new[] { typeof(Vcdiff), typeof(LzmaVcdiff), typeof(Blake3Armor) })
            Assert.Equal(expected, library.Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName);
    }
}
