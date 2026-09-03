using System.Security.Cryptography;
using System.Xml.Linq;

namespace Tappy.App.Tests;

public sealed class BrandingAssetTests
{
    private static readonly XNamespace XamlNamespace =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData("tappy-hand-t.png", 682, 908, "89329DE6252F28851CD4653548D51B551B3C794A5FFEEA97E931CB6851758D51")]
    [InlineData("tappy-hand.png", 682, 908, "552AC1BAEAB6623E6327157A66397B8A5C58DDD816BD1ECFB3DD5B0A6AAC03B5")]
    [InlineData("tappy-wordmark.png", 667, 399, "833DBCEB738E311956C854A85DB9C8B4A8A75354F86CA2CAFDAFD9F09387926F")]
    public void Approved_brand_assets_are_preserved_exactly(
        string fileName,
        int expectedWidth,
        int expectedHeight,
        string expectedHash)
    {
        var path = SourcePath("src", "Tappy.App", "Assets", "Branding", fileName);
        var bytes = File.ReadAllBytes(path);

        Assert.Equal(expectedHash, Convert.ToHexString(SHA256.HashData(bytes)));
        Assert.Equal(expectedWidth, ReadBigEndianInt32(bytes, 16));
        Assert.Equal(expectedHeight, ReadBigEndianInt32(bytes, 20));
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);
    }

    [Fact]
    public void Windows_icon_contains_the_approved_multisize_frame_set()
    {
        var path = SourcePath("src", "Tappy.App", "Assets", "Icons", "tappy.ico");
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);

        Assert.Equal(0, reader.ReadUInt16());
        Assert.Equal(1, reader.ReadUInt16());
        var count = reader.ReadUInt16();
        Assert.Equal(10, count);

        var sizes = new List<int>();
        var frames = new List<(uint Length, uint Offset)>();
        for (var index = 0; index < count; index++)
        {
            var width = reader.ReadByte();
            var height = reader.ReadByte();
            sizes.Add(width == 0 ? 256 : width);
            Assert.Equal(width, height);
            Assert.Equal(0, reader.ReadByte());
            Assert.Equal(0, reader.ReadByte());
            Assert.Equal(1, reader.ReadUInt16());
            Assert.Equal(32, reader.ReadUInt16());
            frames.Add((reader.ReadUInt32(), reader.ReadUInt32()));
        }

        Assert.Equal([16, 20, 24, 32, 40, 48, 64, 96, 128, 256], sizes);
        foreach (var (length, offset) in frames)
        {
            Assert.True(offset + length <= stream.Length);
            stream.Position = offset;
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, reader.ReadBytes(8));
        }
    }

    [Fact]
    public void Main_splash_and_about_follow_the_sister_product_brand_scheme()
    {
        var main = XDocument.Load(SourcePath("src", "Tappy.App", "MainWindow.xaml"));
        AssertWindowIcon(main);
        Assert.Equal(
            "/Tappy;component/Assets/Branding/tappy-hand-t.png",
            NamedElement(main, "Image", "HeaderMascot").Attribute("Source")?.Value);
        Assert.Equal(
            "/Tappy;component/Assets/Branding/tappy-wordmark.png",
            NamedElement(main, "Image", "HeaderWordmark").Attribute("Source")?.Value);

        var splash = XDocument.Load(SourcePath("src", "Tappy.App", "SplashWindow.xaml"));
        Assert.Contains(splash.Descendants(), element =>
            element.Name.LocalName == "Image" &&
            element.Attribute("Source")?.Value == "/Tappy;component/Assets/Branding/tappy-hand.png");
        Assert.DoesNotContain(splash.Descendants(), element =>
            element.Attribute("Text")?.Value?.Contains("Placeholder branding", StringComparison.Ordinal) == true);

        var about = XDocument.Load(SourcePath("src", "Tappy.App", "AboutWindow.xaml"));
        AssertWindowIcon(about);
        Assert.Contains(about.Descendants(), element =>
            element.Name.LocalName == "Image" &&
            element.Attribute("Source")?.Value == "/Tappy;component/Assets/Branding/tappy-hand.png");

        var assignment = XDocument.Load(SourcePath("src", "Tappy.App", "KeyboardAssignmentWindow.xaml"));
        AssertWindowIcon(assignment);
    }

    [Fact]
    public void Project_embeds_branding_and_uses_the_t_hand_application_icon()
    {
        var project = XDocument.Load(SourcePath("src", "Tappy.App", "Tappy.App.csproj"));
        Assert.Equal(
            "Assets\\Icons\\tappy.ico",
            project.Descendants("ApplicationIcon").Single().Value);
        Assert.Contains(project.Descendants("Resource"), element =>
            element.Attribute("Include")?.Value == "Assets\\Branding\\*.png");
        Assert.Contains(project.Descendants("Resource"), element =>
            element.Attribute("Include")?.Value == "Assets\\Icons\\tappy.ico");
    }

    private static void AssertWindowIcon(XDocument document) =>
        Assert.Equal(
            "/Tappy;component/Assets/Icons/tappy.ico",
            document.Root?.Attribute("Icon")?.Value);

    private static XElement NamedElement(XDocument document, string type, string name) =>
        document.Descendants().Single(element =>
            element.Name.LocalName == type &&
            element.Attribute(XamlNamespace + "Name")?.Value == name);

    private static int ReadBigEndianInt32(byte[] bytes, int offset) =>
        (bytes[offset] << 24) |
        (bytes[offset + 1] << 16) |
        (bytes[offset + 2] << 8) |
        bytes[offset + 3];

    private static string SourcePath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tappy.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine([directory.FullName, .. segments]);
    }
}
