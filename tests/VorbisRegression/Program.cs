using System.Buffers.Binary;
using System.Text;
using OSTream.Core.Audio;

byte[] fixture = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "fixture.ogg"));
byte[] validComments = Comments("vendor", "TITLE=test");
CheckPlayback("original", fixture);
CheckPlayback("ordinary comments", ReplaceComments(fixture, validComments));
CheckPlayback("continued comments", ReplaceComments(fixture, Comments("vendor", new string('x', 70000))));
CheckPlayback("continued setup", ReplaceComments(fixture, validComments, splitSetup: true));
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
RejectSetup("ordered codebook at EOF", Setup(1, 0, 0), "Truncated Ogg Vorbis codebook.");
RejectSetup("ordered length over 32", Setup(1, 31, 0, 0, 1), "Invalid Ogg Vorbis ordered codebook.");
RejectSetup("ordered count exceeds entries", Setup(2, 0, 3), "Invalid Ogg Vorbis ordered codebook.");
RejectSetup("valid zero run followed by truncated book", SetupAfterZeroRun(), "Truncated Ogg Vorbis codebook.");
byte[] badSetupStream = ReplaceComments(fixture, validComments, serialDelta: 2, setupOverride: Setup(1, 0, 0));
int badFirstPage = PageSize(badSetupStream, 0);
RejectSetupData("multiplexed malformed setup", fixture[..firstPage].Concat(badSetupStream[..badFirstPage])
    .Concat(fixture[firstPage..]).Concat(badSetupStream[badFirstPage..]).ToArray(), "Truncated Ogg Vorbis codebook.");
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

void RejectSetup(string name, byte[] setup, string message) =>
    RejectSetupData(name, ReplaceComments(fixture, validComments, setupOverride: setup), message);

static void RejectSetupData(string name, byte[] data, string message)
{
    try
    {
        using var decoder = new VorbisDecoder(new MemoryStream(data));
        _ = decoder.Read(new float[2048]);
    }
    catch (InvalidDataException ex) when (ex.Message == message)
    {
        Console.WriteLine($"PASS {name}");
        return;
    }
    throw new Exception($"{name}: setup was not rejected by the codebook guard");
}

// Build setup packets with a single ordered codebook, using Vorbis least-significant-bit-first packing.
static byte[] Setup(int entries, int initialLengthMinusOne, params int[] counts)
{
    var bits = new List<int>();
    void Add(uint value, int count) { for (int i = 0; i < count; i++) bits.Add((int)(value >> i) & 1); }
    foreach (byte b in "\x05vorbis"u8) Add(b, 8);
    Add(0, 8); Add(0x564342, 24); Add(1, 16); Add((uint)entries, 24); Add(1, 1); Add((uint)initialLengthMinusOne, 5);
    int remaining = entries;
    foreach (int count in counts)
    {
        int width = 0;
        for (int n = remaining; n != 0; n >>= 1) width++;
        Add((uint)count, width);
        remaining -= count;
        if (remaining <= 0) break;
    }
    return PackBits(bits);
}

static byte[] SetupAfterZeroRun()
{
    var bits = new List<int>();
    void Add(uint value, int count) { for (int i = 0; i < count; i++) bits.Add((int)(value >> i) & 1); }
    foreach (byte b in "\x05vorbis"u8) Add(b, 8);
    Add(1, 8); // two codebooks
    Add(0x564342, 24); Add(1, 16); Add(1, 24); Add(1, 1); Add(0, 5);
    Add(0, 1); Add(1, 1); // a legitimate zero-count run, followed by a length-2 entry
    Add(0, 4); // no lookup table
    Add(0x564342, 24); Add(1, 16); Add(1, 24); Add(1, 1); Add(0, 5); // second book runs out
    return PackBits(bits);
}

static byte[] PackBits(List<int> bits)
{
    byte[] data = new byte[(bits.Count + 7) / 8];
    for (int i = 0; i < bits.Count; i++) data[i / 8] |= (byte)(bits[i] << (i % 8));
    return data;
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
static byte[] ReplaceComments(byte[] source, byte[] comments, uint serialDelta = 0, byte[]? setupOverride = null, bool splitSetup = false)
{
    int firstSize = PageSize(source, 0);
    int headerSize = PageSize(source, firstSize);
    int audioStart = firstSize + headerSize;
    int headerSegments = source[firstSize + 26];
    int commentSize = source[firstSize + 27];
    byte[] setup = setupOverride ?? source[(firstSize + 27 + headerSegments + commentSize)..audioStart];
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
    int setupPosition = 0;
    do
    {
        int remaining = setup.Length - setupPosition;
        int chunk = splitSetup && remaining > 255 ? 255 : remaining;
        bool complete = chunk == remaining;
        int segments = complete ? chunk / 255 + 1 : chunk / 255;
        byte[] setupPage = new byte[27 + segments + chunk];
        "OggS"u8.CopyTo(setupPage);
        setupPage[5] = setupPosition > 0 ? (byte)1 : (byte)0;
        setupPage[26] = (byte)segments;
        for (int i = 0; i < segments; i++) setupPage[27 + i] = (byte)Math.Min(255, chunk - i * 255);
        setup.AsSpan(setupPosition, chunk).CopyTo(setupPage.AsSpan(27 + segments));
        WriteExistingPage(setupPage, result, serial, sequence++);
        setupPosition += chunk;
        if (complete) break;
    } while (true);
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
