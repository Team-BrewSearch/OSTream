using System.Buffers;
using System.Buffers.Binary;
using System.Net;
using System.Reflection;
using OSTream.Core.Audio;

await Check(44100, 2, 20000, 8820);
await Check(192000, 2, 80000, 38400);
await Check(int.MaxValue, 2, 0, 0);
await Check(int.MaxValue, 2, 140000, 131072);
await Check(int.MaxValue, 1, 270000, 262144);
await Check(int.MaxValue, 8, 140000, 131072);
Console.WriteLine("All audio buffer regression checks passed.");

static async Task Check(int rate, int streamChannels, int frames, int expectedFirstFrames)
{
    byte[] wav = MakeWav(rate, streamChannels, frames);
    using var http = new HttpClient(new BytesHandler(wav));
    HttpRangeStream stream = await HttpRangeStream.OpenAsync(http, () => new HttpRequestMessage(HttpMethod.Get, "https://example.test/audio.wav"), CancellationToken.None);
    var decoder = new RecordingDecoder(new WavDecoder(stream));
    Type sessionType = typeof(AudioEngine).GetNestedType("Session", BindingFlags.NonPublic)!;
    var session = (IDisposable)Activator.CreateInstance(sessionType, decoder, stream, 1L)!;
    try
    {
        sessionType.GetMethod("Start")!.Invoke(session, null);
        var first = Take(sessionType, session);
        if (first.Error is not null || first.Frames != expectedFirstFrames || first.Start != 0)
            throw new Exception($"Unexpected first chunk for {rate} Hz, {streamChannels} channels: {first}");
        int outputChannels = Math.Min(streamChannels, 2);
        int expectedSamples = Math.Min(Math.Max(4096, rate / 5), 262144 / outputChannels) * outputChannels;
        if (decoder.FirstReadLength != expectedSamples || first.Data.Length > 262144)
            throw new Exception($"Unbounded or unexpected buffer for {rate} Hz, {streamChannels} channels");
        if (first.Frames > 0)
        {
            if (streamChannels <= 2 && first.Data[0] != 0.5f)
                throw new Exception("PCM samples were not decoded correctly");
            ArrayPool<float>.Shared.Return(first.Data);
            var next = Take(sessionType, session);
            if (next.Error is not null || next.Start != first.Frames || next.Frames != Math.Min(frames - first.Frames, expectedSamples / outputChannels))
                throw new Exception($"Unexpected second chunk for {rate} Hz: {next}");
            if (next.Frames > 0) ArrayPool<float>.Shared.Return(next.Data);
            sessionType.GetMethod("RequestSeek")!.Invoke(session, [100L]);
            var sought = Take(sessionType, session);
            if (sought.Error is not null || sought.Start != 100 || sought.Frames <= 0)
                throw new Exception($"Seek failed for {rate} Hz: {sought}");
            ArrayPool<float>.Shared.Return(sought.Data);
        }
        Console.WriteLine($"PASS {rate} Hz, {streamChannels} channels, {frames} frames");
    }
    finally
    {
        session.Dispose();
    }
}

static (float[] Data, int Frames, long Start, string? Error) Take(Type sessionType, IDisposable session)
{
    MethodInfo take = sessionType.GetMethod("TryTake")!;
    DateTime deadline = DateTime.UtcNow.AddSeconds(10);
    while (DateTime.UtcNow < deadline)
    {
        object?[] args = [null];
        if ((bool)take.Invoke(session, args)!)
        {
            object chunk = args[0]!;
            Type type = chunk.GetType();
            return ((float[])type.GetProperty("Data")!.GetValue(chunk)!, (int)type.GetProperty("Frames")!.GetValue(chunk)!,
                (long)type.GetProperty("Start")!.GetValue(chunk)!, (string?)type.GetProperty("Error")!.GetValue(chunk));
        }
        Thread.Sleep(10);
    }
    throw new Exception("Timed out waiting for an audio chunk");
}

static byte[] MakeWav(int rate, int channels, int frames)
{
    byte[] bytes = new byte[44 + frames * channels * 2];
    "RIFF"u8.CopyTo(bytes);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), bytes.Length - 8);
    "WAVEfmt "u8.CopyTo(bytes.AsSpan(8));
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 16);
    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20), 1);
    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22), (short)channels);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), rate);
    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(32), (short)(channels * 2));
    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(34), 16);
    "data"u8.CopyTo(bytes.AsSpan(36));
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40), bytes.Length - 44);
    for (int i = 44; i < bytes.Length; i += 2) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i), 16384);
    return bytes;
}

sealed class BytesHandler(byte[] bytes) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
}

sealed class RecordingDecoder(IAudioDecoder inner) : IAudioDecoder
{
    public int FirstReadLength { get; private set; }
    public int SampleRate => inner.SampleRate;
    public int Channels => inner.Channels;
    public long TotalFrames => inner.TotalFrames;
    public long Position => inner.Position;
    public long StreamPosition => inner.StreamPosition;
    public int Read(Span<float> dest)
    {
        if (FirstReadLength == 0) FirstReadLength = dest.Length;
        return inner.Read(dest);
    }
    public void Seek(long frame) => inner.Seek(frame);
    public void Dispose() => inner.Dispose();
}
