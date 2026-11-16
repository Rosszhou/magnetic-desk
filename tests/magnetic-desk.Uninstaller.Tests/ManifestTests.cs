using System.Text.Json;
using MagneticDesk.Uninstaller;

namespace MagneticDesk.Uninstaller.Tests;

public sealed class ManifestTests
{
    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("..\\outside.txt")]
    [InlineData("C:\\outside.txt")]
    [InlineData("file.txt:stream")]
    [InlineData("Assets/../outside.txt")]
    [InlineData("Assets//image.png")]
    [InlineData("")]
    public void UnsafePathsAreRejected(string relative)
    {
        Assert.Throws<InvalidDataException>(() => UninstallManifest.Resolve(Path.GetTempPath(), relative));
    }

    [Fact]
    public void ManifestMustIdentifyBothApplicationAndUninstaller()
    {
        string root = Path.Combine(Path.GetTempPath(), "uninstall-unit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, UninstallManifest.FileName), JsonSerializer.Serialize(
                new UninstallManifest("magnetic-desk", [new("user-note.txt", new string('0', 64))])));
            Assert.Throws<InvalidDataException>(() => UninstallManifest.Load(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void NestedPayloadPathStaysInsideItsRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "uninstall-unit");
        Assert.Equal(Path.Combine(root, "Assets", "logo.png"), UninstallManifest.Resolve(root, "Assets/logo.png"));
    }
}
