using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using SharpMUTerm.Core.Presence;

namespace SharpMUTerm.Core.Tests.Presence;

/// <summary>Reading <c>External.Discord</c>, and what goes down Discord's IPC socket.</summary>
public class GamePresenceTests
{
    [Test]
    public async Task StatusBeforeInfoIsIgnoredAndInfoNamesTheApplication()
    {
        var p = GamePresence.Apply(null, GamePresence.StatusPackage, """{"details":"x"}""");
        await Assert.That(p).IsNull();

        p = GamePresence.Apply(null, GamePresence.InfoPackage, """{"applicationid":"42","inviteurl":"https://x"}""");
        p = GamePresence.Apply(p, GamePresence.StatusPackage, """{"details":"Line\none","state":"Lvl 3","starttime":1700000000,"partysize":"2"}""");

        await Assert.That(p!.ApplicationId).IsEqualTo("42");
        await Assert.That(p.Details).IsEqualTo("Line one");
        await Assert.That(p.State).IsEqualTo("Lvl 3");
        await Assert.That(p.StartTime!.Value.ToUnixTimeSeconds()).IsEqualTo(1700000000);
        await Assert.That(p.PartySize).IsEqualTo(2);
    }

    [Test]
    public async Task ABadApplicationIdAndAnOverlongFieldAreRefusedOrCut()
    {
        await Assert.That(GamePresence.Apply(null, GamePresence.InfoPackage, """{"applicationid":"../../x"}""")).IsNull();
        var p = GamePresence.Apply(new GamePresence("1"), GamePresence.StatusPackage, "{\"details\":\"" + new string('d', 500) + "\"}");
        await Assert.That(p!.Details!.Length).IsEqualTo(GamePresence.MaxField);
        await Assert.That(GamePresence.Apply(p, GamePresence.StatusPackage, "not json")).IsSameReferenceAs(p);
    }

    [Test]
    public async Task TheIpcHandshakesForTheGamesApplicationThenSetsTheActivity()
    {
        var stream = new FakeDiscord();
        using (var ipc = new DiscordIpc(_ => Task.FromResult<Stream?>(stream), pid: 7))
        {
            ipc.Show(new GamePresence("42", Details: "Exploring", PartySize: 2, PartyMax: 4));
            await stream.WaitForFrames(2);
            ipc.Show(null);
            await stream.WaitForFrames(3);
        }

        var frames = stream.Frames;
        await Assert.That(frames[0].Op).IsEqualTo(0);
        await Assert.That(frames[0].Body["client_id"]!.GetValue<string>()).IsEqualTo("42");
        await Assert.That(frames[1].Body["cmd"]!.GetValue<string>()).IsEqualTo("SET_ACTIVITY");
        await Assert.That(frames[1].Body["args"]!["pid"]!.GetValue<int>()).IsEqualTo(7);
        await Assert.That(frames[1].Body["args"]!["activity"]!["details"]!.GetValue<string>()).IsEqualTo("Exploring");
        await Assert.That(frames[1].Body["args"]!["activity"]!["party"]!["size"]!.ToJsonString()).IsEqualTo("[2,4]");
        await Assert.That(frames[2].Body["args"]!["activity"]).IsNull();
    }

    [Test]
    public async Task NoDiscordMeansNothingHappens()
    {
        var calls = 0;
        using var ipc = new DiscordIpc(_ => { calls++; return Task.FromResult<Stream?>(null); });
        ipc.Show(new GamePresence("42"));
        for (var i = 0; i < 100 && calls == 0; i++)
        {
            await Task.Delay(10);
        }

        await Assert.That(calls).IsEqualTo(1);
    }

    /// <summary>Discord's side of the socket: records each frame and answers it with an empty one.</summary>
    private sealed class FakeDiscord : Stream
    {
        private readonly List<byte> _written = [];
        private readonly Queue<byte> _replies = new();
        private readonly SemaphoreSlim _readable = new(0);
        private readonly object _gate = new();

        public List<(int Op, JsonObject Body)> Frames { get; } = [];

        public async Task WaitForFrames(int count)
        {
            for (var i = 0; i < 300; i++)
            {
                lock (_gate)
                {
                    if (Frames.Count >= count)
                    {
                        return;
                    }
                }

                await Task.Delay(10);
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            lock (_gate)
            {
                _written.AddRange(buffer.AsSpan(offset, count).ToArray());
                while (_written.Count >= 8)
                {
                    var header = _written.Take(8).ToArray();
                    var length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4));
                    if (_written.Count < 8 + length)
                    {
                        break;
                    }

                    var body = Encoding.UTF8.GetString(_written.Skip(8).Take(length).ToArray());
                    _written.RemoveRange(0, 8 + length);
                    Frames.Add((BinaryPrimitives.ReadInt32LittleEndian(header), (JsonObject)JsonNode.Parse(body)!));
                    byte[] reply = [1, 0, 0, 0, 2, 0, 0, 0, (byte)'{', (byte)'}'];
                    foreach (var b in reply)
                    {
                        _replies.Enqueue(b);
                    }

                    _readable.Release();
                }
            }
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (true)
            {
                lock (_gate)
                {
                    if (_replies.Count > 0)
                    {
                        var n = 0;
                        while (n < buffer.Length && _replies.Count > 0)
                        {
                            buffer.Span[n++] = _replies.Dequeue();
                        }

                        return n;
                    }
                }

                await _readable.WaitAsync(cancellationToken);
            }
        }

        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
