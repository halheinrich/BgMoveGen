using System.Reflection;

namespace BgMoveGen.Tests;

/// <summary>
/// The declaration that makes this library's half of the trim gate a gate
/// rather than a suggestion (halheinrich/backgammon#228, in the mould of
/// XgFilter_Razor's posture pins for halheinrich/backgammon#193 and
/// BackgammonDiagram_Lib's <c>CoreTrimPostureTests</c> for
/// halheinrich/backgammon#197). BgMoveGen declares <c>IsAotCompatible</c>,
/// from which the SDK defaults <c>IsTrimmable</c> and the trim, AOT and
/// single-file analyzers. The analyzers' verdict is enforced by the build
/// itself, under TreatWarningsAsErrors; what this pins is that nobody quietly
/// switches the premise off — dropping the declaration from the csproj fails
/// a test rather than deferring the next trim- or AOT-unsafe construct to a
/// publish: this project's NativeAOT DLL, or BgQuiz's trimmed WebAssembly
/// client.
/// </summary>
public class TrimPostureTests
{
    // Both settings surface in the built assembly as SDK-emitted metadata —
    // the one trace of the csproj a test can read. IsTrimmable is the entry
    // the WebAssembly SDK's partial trim mode reads to decide whether to trim
    // the assembly at all; IsAotCompatible is the declaration it defaults
    // from. The analyzer switches leave no trace; the build exercises them.
    [Fact]
    public void TheLibrary_DeclaresItselfAotCompatibleAndTrimmable()
    {
        var metadata = typeof(MoveGenerator).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>();

        Assert.Contains(metadata, a => a.Key == "IsAotCompatible" && a.Value == "True");
        Assert.Contains(metadata, a => a.Key == "IsTrimmable" && a.Value == "True");
    }
}
