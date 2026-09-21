using Tappy.Windows.Output;

namespace Tappy.Windows.Tests;

public sealed class TartarusIndicatorTests
{
    [Theory]
    [InlineData(0, 0x0E)] // Yellow: observed on the operator's original 1532:0201.
    [InlineData(1, 0x0C)] // Green.
    [InlineData(2, 0x0D)] // Blue.
    public void Named_colors_use_the_physically_observed_channel_for_reads_and_writes(int color, byte wireChannel)
    {
        foreach (bool? enabled in new bool?[] { null, false, true })
        {
            var request = TartarusIndicatorProtocol.Request(color, enabled);
            Assert.Equal(wireChannel, request[10]);
            Assert.Equal(TartarusIndicatorProtocol.Checksum(request), request[89]);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void Every_pattern_is_read_back_and_only_changed_channels_are_written(int mask)
    {
        using var port = new FakePort { Mask = 1 };
        Assert.Equal(mask, new TartarusIndicatorProtocol(port, () => true).Synchronize(mask));
        Assert.Equal(mask, port.Mask);
        Assert.All(port.Requests.Take(3), request => Assert.Equal(0x80, request[8]));
        Assert.All(port.Requests.TakeLast(3), request => Assert.Equal(0x80, request[8]));
        Assert.All(port.Requests, request =>
        {
            Assert.Equal(91, request.Length); Assert.Equal(0, request[0]); Assert.Equal(0xFF, request[2]);
            Assert.Equal(3, request[7]); Assert.InRange(request[10], (byte)0x0C, (byte)0x0E);
            Assert.Equal(TartarusIndicatorProtocol.Checksum(request), request[89]);
            Assert.True(request[8] is 0 or 0x80);
        });
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 7)]
    [InlineData(3, 1)]
    [InlineData(6, 4)]
    [InlineData(7, 0)]
    [InlineData(8, 0)]
    [InlineData(9, 0)]
    [InlineData(10, 13)]
    [InlineData(11, 2)]
    [InlineData(89, 7)]
    [InlineData(90, 1)]
    public void Invalid_reply_is_refused(int index, byte value)
    {
        var request = TartarusIndicatorProtocol.Request(0, null);
        var response = (byte[])request.Clone(); response[1] = 2;
        response[index] = value;
        if (index != 89) { response[89] = TartarusIndicatorProtocol.Checksum(response); }
        Assert.Throws<IOException>(() => TartarusIndicatorProtocol.Validate(request, response));
    }

    [Fact]
    public void Short_reply_and_wrong_set_acknowledgement_are_refused()
    {
        var request = TartarusIndicatorProtocol.Request(2, true);
        Assert.Throws<IOException>(() => TartarusIndicatorProtocol.Validate(request, []));
        var reply = (byte[])request.Clone(); reply[1] = 2; reply[11] = 0; reply[89] = TartarusIndicatorProtocol.Checksum(reply);
        Assert.Throws<IOException>(() => TartarusIndicatorProtocol.Validate(request, reply));
    }

    [Fact]
    public void Initial_read_failure_never_writes_a_state()
    {
        using var port = new FakePort { FailAt = 2 };
        Assert.Throws<IOException>(() => new TartarusIndicatorProtocol(port, () => true).Synchronize(4));
        Assert.All(port.Requests, request => Assert.Equal(0x80, request[8]));
    }

    [Fact]
    public void Partially_applied_pattern_is_restored_and_reported_as_failure()
    {
        using var port = new FakePort { Mask = 1, FailAt = 5 };
        var exception = Assert.Throws<IOException>(() => new TartarusIndicatorProtocol(port, () => true).Synchronize(4));
        Assert.Contains("restored and verified", exception.Message);
        Assert.Equal(1, port.Mask);
    }

    [Fact]
    public void Changed_confirmation_stops_without_reopening_or_rollback_writes()
    {
        using var port = new FakePort { Mask = 1 };
        Assert.Throws<OperationCanceledException>(() => new TartarusIndicatorProtocol(port, () => port.Requests.Count < 4).Synchronize(4));
        Assert.Equal(4, port.Requests.Count);
    }

    [Fact]
    public async Task Invalid_target_or_mask_is_rejected_before_open()
    {
        var opened = false;
        var output = new NativeRazerTartarusIndicatorOutput(_ => { opened = true; return new FakePort(); });
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => output.SynchronizeAsync("target", 8, () => true));
        await Assert.ThrowsAsync<OperationCanceledException>(() => output.SynchronizeAsync("target", 1, () => false));
        Assert.False(opened);
    }

    [Fact]
    public void Identical_devices_are_selected_by_container_identity_never_first_model_match()
    {
        var first = Guid.NewGuid(); var second = Guid.NewGuid();
        var a = new TartarusFeatureCandidate("hid#vid_1532&pid_0201&mi_02#A", first, 0x1532, 0x0201, 91, 1, 2);
        var b = a with { Container = second, Path = "hid#vid_1532&pid_0201&mi_02#B" };
        Assert.Equal(b.Path, NativeTartarusFeaturePort.SelectPath([a, b], NativeTartarusFeaturePort.PersistentId(second)));
        Assert.Throws<IOException>(() => NativeTartarusFeaturePort.SelectPath([a, b], "unconfirmed"));
        Assert.Throws<IOException>(() => NativeTartarusFeaturePort.SelectPath([a, a with { Path = a.Path + "-duplicate" }], NativeTartarusFeaturePort.PersistentId(first)));
        foreach (var wrong in new[] { a with { Vendor = 0x046D }, a with { Product = 0x0208 }, a with { FeatureLength = 90 },
                     a with { Usage = 6 }, a with { Container = Guid.Empty }, a with { Path = "hid#vid_1532&pid_0201&mi_01#A" } })
        { Assert.Throws<IOException>(() => NativeTartarusFeaturePort.SelectPath([wrong], NativeTartarusFeaturePort.PersistentId(first))); }
    }

    private sealed class FakePort : ITartarusFeaturePort
    {
        public int Mask { get; set; }
        public int? FailAt { get; init; }
        public List<byte[]> Requests { get; } = [];
        public byte[] Exchange(byte[] request)
        {
            Requests.Add((byte[])request.Clone());
            if (Requests.Count == FailAt) { throw new IOException("Simulated response loss."); }
            // Model the physical observations independently of the production
            // translator: 0C lit green, 0D blue, 0E yellow on the original unit.
            // Mask bits remain semantic yellow=1, green=2, blue=4.
            var channel = request[10] switch
            { 0x0C => 1, 0x0D => 2, 0x0E => 0, _ => throw new IOException("Unknown LED channel.") };
            if (request[8] == 0) { Mask = (Mask & ~(1 << channel)) | (request[11] << channel); }
            var reply = (byte[])request.Clone(); reply[1] = 2; reply[11] = (byte)((Mask >> channel) & 1);
            reply[89] = TartarusIndicatorProtocol.Checksum(reply);
            return reply;
        }
        public void Dispose() { }
    }
}
