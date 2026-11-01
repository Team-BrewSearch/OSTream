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
