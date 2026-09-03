using System.Buffers.Binary;
using Tappy.App.Services;
using Tappy.Windows.Input;

namespace Tappy.App.Tests;

public sealed class ControllerPhotoCatalogTests
{
    [Fact]
    public void Exact_physical_G13_identity_has_one_bounded_hotspot_per_supported_control()
    {
        var photo = Assert.IsType<ControllerPhotoDefinition>(ControllerPhotoCatalog.Find(
            "raw-hid-g13",
            LogitechG13Protocol.VendorId,
            LogitechG13Protocol.ProductId));

        Assert.Equal("logitech-g13-user-photo-v1", photo.Id);
        Assert.Equal(39, photo.Hotspots.Count);
        Assert.Equal(
            LogitechG13InputProvider.SupportedControls.Select(item => item.ControlId.Value).Order(),
            photo.Hotspots.Keys.Order());
        Assert.All(photo.Hotspots.Values, hotspot =>
        {
            Assert.True(hotspot.Width > 0);
            Assert.True(hotspot.Height > 0);
            Assert.InRange(hotspot.Left, 0, photo.Width - hotspot.Width);
            Assert.InRange(hotspot.Top, 0, photo.Height - hotspot.Height);
        });
    }

    [Fact]
    public void Photo_catalog_refuses_similar_or_unknown_controller_identities()
    {
        Assert.Null(ControllerPhotoCatalog.Find("raw-input", 0x046D, 0xC21C));
        Assert.Null(ControllerPhotoCatalog.Find("raw-hid-g13", 0x046D, 0xC232));
        Assert.Null(ControllerPhotoCatalog.Find("raw-hid-g13", 0x1532, 0x0201));
        Assert.Null(ControllerPhotoCatalog.Find(null, null, null));
    }

    [Fact]
    public void Embedded_G13_asset_is_the_verified_rgba_source_instead_of_an_opaque_checkerboard()
    {
        var bytes = File.ReadAllBytes(SourcePath(
            "src", "Tappy.App", "Assets", "Controllers", "logitech-g13-user-photo.png"));

        Assert.Equal([137, 80, 78, 71, 13, 10, 26, 10], bytes[..8]);
        Assert.Equal(853, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)));
        Assert.Equal(1844, BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)));
        Assert.Equal(8, bytes[24]);
        Assert.Equal(6, bytes[25]);
    }

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
