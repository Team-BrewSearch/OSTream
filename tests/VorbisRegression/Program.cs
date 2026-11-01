using System.Buffers.Binary;
using System.Text;
using OSTream.Core.Audio;

byte[] fixture = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixture.ogg"));
byte[] validComments = Comments("vendor", "TITLE=test");
CheckPlayback("original", fixture);
CheckPlayback("ordinary comments", ReplaceComments(fixture, validComments));
CheckPlayback("continued comments", ReplaceComments(fixture, Comments("vendor", new string('x', 70000))));
Reject("huge comment count", ReplaceComments(fixture, Packet(0, 0x7fffffff)));
Reject("huge vendor length", ReplaceComments(fixture, Packet(0x7fffffff)));
Reject("truncated vendor", ReplaceComments(fixture, Packet(100, 0)));
Reject("truncated count", ReplaceComments(fixture, Packet(0).Concat(new byte[] { 1, 0 }).ToArray()));
Reject("truncated comment length", ReplaceComments(fixture, Packet(0, 1).Concat(new byte[] { 2, 0 }).ToArray()));
Reject("truncated comment", ReplaceComments(fixture, Packet(0, 1, 100)));
Reject("metadata budget", ReplaceComments(fixture, Comments("vendor", new string('x', 350000))));
Reject("many empty comments", ReplaceComments(fixture, Packet(0, 30000)));
byte[] second = ReplaceComments(fixture, Packet(0, 0x7fffffff), serialDelta: 1);
int firstPage = PageSize(fixture, 0), secondPage = PageSize(second, 0);
Reject("multiplexed logical stream", fixture[..firstPage].Concat(second[..secondPage])
    .Concat(fixture[firstPage..]).Concat(second[secondPage..]).ToArray());
Console.WriteLine("All Vorbis regression checks passed.");

static void CheckPlayback(string name, byte[] data)
{
    using var decoder = new VorbisDecoder(new MemoryStream(data));
    if (decoder.Channels != 2 || decoder.SampleRate != 44100 || decoder.TotalFrames <= 0)
        throw new Exception($"{name}: invalid audio properties");
    float[] buffer = new float[2048];
    if (decoder.Read(buffer) <= 0) throw new Exception($"{name}: no decoded audio");
    decoder.Seek(1000);
    if (decoder.Read(buffer) <= 0) throw new Exception($"{name}: no audio after seeking");
    Console.WriteLine($"PASS {name}");
}

static void Reject(string name, byte[] data)
{
    try
    {
        using var decoder = new VorbisDecoder(new MemoryStream(data));
        _ = decoder.Read(new float[2048]);
    }
    catch (InvalidDataException)
    {
        Console.WriteLine($"PASS {name}");
        return;
    }
    throw new Exception($"{name}: malformed comments accepted");
}

static byte[] Packet(params uint[] values)
{
    using var bytes = new MemoryStream();
    bytes.Write("\x03vorbis"u8);
    Span<byte> buf = stackalloc byte[4];
    foreach (uint value in values)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(buf, value);
        bytes.Write(buf);
    }
    return bytes.ToArray();
}

static byte[] Comments(string vendor, string comment)
{
    byte[] v = Encoding.UTF8.GetBytes(vendor), c = Encoding.UTF8.GetBytes(comment);
    using var bytes = new MemoryStream();
    bytes.Write(Packet((uint)v.Length));
    bytes.Write(v);
    Span<byte> buf = stackalloc byte[4];
    BinaryPrimitives.WriteUInt32LittleEndian(buf, 1);
    bytes.Write(buf);
    BinaryPrimitives.WriteUInt32LittleEndian(buf, (uint)c.Length);
    bytes.Write(buf);
    bytes.Write(c);
    bytes.WriteByte(1);
    return bytes.ToArray();
}

// Rebuild the fixture's comment pages while retaining its identification, setup and audio packets.
static byte[] ReplaceComments(byte[] source, byte[] comments, uint serialDelta = 0)
{
    int firstSize = PageSize(source, 0);
    int headerSize = PageSize(source, firstSize);
    int audioStart = firstSize + headerSize;
    int headerSegments = source[firstSize + 26];
    int commentSize = source[firstSize + 27];
    byte[] setup = source[(firstSize + 27 + headerSegments + commentSize)..audioStart];
    uint serial = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(14)) + serialDelta;
    using var result = new MemoryStream();
    uint sequence = 0;
    WriteExistingPage(source[..firstSize], result, serial, sequence++);
    int position = 0;
    bool continuation = false;
    while (position < comments.Length || (comments.Length > 0 && comments.Length % 255 == 0 && position == comments.Length))
    {
        int remaining = comments.Length - position;
        int chunk = Math.Min(remaining, 255 * 255);
        bool complete = chunk == remaining && chunk < 255 * 255;
        int segments = complete ? chunk / 255 + 1 : 255;
        byte[] page = new byte[27 + segments + chunk];
        "OggS"u8.CopyTo(page);
        page[5] = continuation ? (byte)1 : (byte)0;
        BinaryPrimitives.WriteInt64LittleEndian(page.AsSpan(6), -1);
        page[26] = (byte)segments;
        for (int i = 0; i < segments; i++) page[27 + i] = (byte)Math.Min(255, chunk - i * 255);
        comments.AsSpan(position, chunk).CopyTo(page.AsSpan(27 + segments));
        WriteExistingPage(page, result, serial, sequence++);
        position += chunk;
        continuation = true;
        if (complete) break;
    }
    int setupSegments = setup.Length / 255 + 1;
    byte[] setupPage = new byte[27 + setupSegments + setup.Length];
    "OggS"u8.CopyTo(setupPage);
    setupPage[26] = (byte)setupSegments;
    for (int i = 0; i < setupSegments; i++) setupPage[27 + i] = (byte)Math.Min(255, setup.Length - i * 255);
    setup.CopyTo(setupPage, 27 + setupSegments);
    WriteExistingPage(setupPage, result, serial, sequence++);
    while (audioStart < source.Length)
    {
        int size = PageSize(source, audioStart);
        WriteExistingPage(source[audioStart..(audioStart + size)], result, serial, sequence++);
        audioStart += size;
    }
    return result.ToArray();
}

static int PageSize(byte[] bytes, int offset)
{
    int segments = bytes[offset + 26], size = 27 + segments;
    for (int i = 0; i < segments; i++) size += bytes[offset + 27 + i];
    return size;
}

static void WriteExistingPage(byte[] page, Stream output, uint serial, uint sequence)
{
    BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(14), serial);
    BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(18), sequence);
    page.AsSpan(22, 4).Clear();
    uint crc = 0;
    foreach (byte b in page)
    {
        crc ^= (uint)b << 24;
        for (int bit = 0; bit < 8; bit++) crc = (crc & 0x80000000) != 0 ? (crc << 1) ^ 0x04c11db7 : crc << 1;
    }
    BinaryPrimitives.WriteUInt32LittleEndian(page.AsSpan(22), crc);
    output.Write(page);
}
