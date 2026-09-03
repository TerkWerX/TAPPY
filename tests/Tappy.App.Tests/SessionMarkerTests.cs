using Tappy.App.Services;

namespace Tappy.App.Tests;

public sealed class SessionMarkerTests
{
    [Fact]
    public void Unsafe_output_state_preserves_recovery_marker_until_a_safe_completion()
    {
        var root = Path.Combine(Path.GetTempPath(), $"Tappy-SessionMarkerTests-{Guid.NewGuid():N}");
        try
        {
            var marker = new SessionMarker(root);
            Assert.Null(marker.Begin());

            Assert.False(marker.TryComplete(outputStateConfirmedSafe: false));
            var nextLaunch = new SessionMarker(root).Begin();
            Assert.NotNull(nextLaunch);

            Assert.True(marker.TryComplete(outputStateConfirmedSafe: true));
            Assert.Null(new SessionMarker(root).Begin());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
