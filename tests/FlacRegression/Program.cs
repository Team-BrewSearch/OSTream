using OSTream.Core.Audio;

// Seven bad candidates can be skipped, and a successful frame resets the recovery limit.
byte[] bad = Header(16).Concat(new byte[] { 0xff }).ToArray();
byte[] good = Header(16).Concat(new byte[] { 0, 32, 0, 64, 0, 0 }).ToArray();
byte[] recoverable = Enumerable.Repeat(bad, 7).SelectMany(x => x).Concat(good)
    .Concat(Enumerable.Repeat(bad, 7).SelectMany(x => x)).Concat(good).ToArray();
using (var decoder = new FlacDecoder(new MemoryStream(Stream(recoverable))))
{
    float[] output = new float[64];
    if (decoder.Read(output) != 32 || output[0] != 0.25f || output[1] != 0.5f || output[32] != 0.25f)
        throw new Exception("Recovery did not preserve valid audio after invalid candidates.");
}
Console.WriteLine("PASS recovery and reset");

// Each candidate starts a long verbatim subframe and fails on the next header.
// The old recovery path re-decodes the overlapping samples for every candidate.
byte[] pattern = new byte[16];
Header(4087).CopyTo(pattern, 0);
pattern[8] = 2;
byte[] overlapping = Stream(Enumerable.Repeat(pattern, 2048).SelectMany(x => x).ToArray());
RejectOverlapping("read", decoder => decoder.Read(new float[2]));
RejectOverlapping("seek", decoder => { decoder.Seek(0); return 0; });

// Seek-table entries must be complete, even when the stream reports a larger length.
byte[] point = new byte[18];
RejectSeekTable("declared length exceeds stream", 0xffffff, point, false);
RejectSeekTable("EOF after complete point", 36, point, true);
RejectSeekTable("partial point", 36, point.Concat(new byte[17]).ToArray(), true);
RejectSeekTable("missing trailing bytes", 19, point, true);
try
{
    using var stream = new ChunkedStream(SeekTable(36, point, []), 0, false);
    using var decoder = new FlacDecoder(stream);
    throw new Exception("Non-seekable stream accepted a truncated seek table.");
}
catch (InvalidDataException)
{
    Console.WriteLine("PASS truncated seek table on non-seekable stream");
}

// A placeholder and the existing tolerated trailing byte must not change the first frame.
byte[] first = Header(16).Concat(new byte[] { 0, 16, 0, 8, 0, 0 }).ToArray();
byte[] validPoint = point.ToArray();
System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(validPoint, 1);
System.Buffers.Binary.BinaryPrimitives.WriteUInt64BigEndian(validPoint.AsSpan(8), (ulong)first.Length);
byte[] placeholder = Enumerable.Repeat((byte)0xff, 8).Concat(new byte[10]).ToArray();
byte[] table = validPoint.Concat(placeholder).Concat(new byte[] { 0 }).ToArray();
using (var decoder = new FlacDecoder(new ChunkedStream(SeekTable(table.Length, table, first.Concat(good).ToArray()), 0, true)))
{
    decoder.Seek(1);
    float[] output = new float[32];
    if (decoder.Read(output) != 15 || output[0] != 0.25f || output[1] != 0.5f)
        throw new Exception("Valid seek table did not preserve seeking and audio.");
}
Console.WriteLine("PASS valid seek table with fragmented reads");
using (var decoder = new FlacDecoder(new ChunkedStream(SeekTable(table.Length, table, good), 0, false)))
{
    float[] output = new float[32];
    if (decoder.Read(output) != 16 || output[0] != 0.25f)
        throw new Exception("Non-seekable stream did not decode valid audio.");
}
Console.WriteLine("PASS valid seek table on non-seekable stream");

void RejectSeekTable(string name, int declaredLength, byte[] data, bool overstateLength)
{
    byte[] fixture = SeekTable(declaredLength, data, []);
    using var stream = new ChunkedStream(fixture, overstateLength ? declaredLength : 0, true);
    try { using var decoder = new FlacDecoder(stream); }
    catch (InvalidDataException)
    {
        Console.WriteLine($"PASS truncated seek table ({name})");
        return;
    }
    throw new Exception($"{name}: truncated seek table was accepted");
}

static byte[] SeekTable(int declaredLength, byte[] data, byte[] frames)
{
    byte[] metadata = Stream([]);
    metadata[4] = 0;
    byte[] header = [0x83, (byte)(declaredLength >> 16), (byte)(declaredLength >> 8), (byte)declaredLength];
    return metadata.Concat(header).Concat(data).Concat(frames).ToArray();
}

void RejectOverlapping(string name, Func<FlacDecoder, int> action)
{
    using var decoder = new FlacDecoder(new MemoryStream(overlapping));
    try { _ = action(decoder); }
    catch (InvalidDataException ex) when (ex.Message == "Too many invalid FLAC frames.")
    {
        Console.WriteLine($"PASS overlapping candidates ({name})");
        return;
    }
    throw new Exception($"{name}: overlapping invalid frames did not stop recovery");
}

static byte[] Stream(byte[] frames)
{
    byte[] metadata = new byte[42];
    "fLaC"u8.CopyTo(metadata);
    metadata[4] = 0x80;
    metadata[7] = 34;
    metadata[10] = 0x10; // max block size 4096
    // 44100 Hz, stereo, 8 bits per sample
    metadata[18] = 0x0a;
    metadata[19] = 0xc4;
    metadata[20] = 0x42;
    metadata[21] = 0x70;
    return metadata.Concat(frames).ToArray();
}

static byte[] Header(int blockSize)
{
    byte[] header = [0xff, 0xf8, 0x70, 0x12, 0, (byte)((blockSize - 1) >> 8), (byte)(blockSize - 1), 0];
    byte crc = 0;
    foreach (byte value in header.AsSpan(0, 7))
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++) crc = (byte)((crc & 0x80) != 0 ? (crc << 1) ^ 7 : crc << 1);
    }
    header[7] = crc;
    return header;
}

sealed class ChunkedStream(byte[] data, int extraLength, bool seekable) : MemoryStream(data)
{
    public override bool CanSeek => seekable;
    public override long Length => seekable ? base.Length + extraLength : throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, 3));
}
