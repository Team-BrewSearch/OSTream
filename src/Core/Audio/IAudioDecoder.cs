using System;
namespace OSTream.Core.Audio;

public interface IAudioDecoder : IDisposable
{
    int SampleRate { get; }
    int Channels { get; }
    long TotalFrames { get; }
    long Position { get; }
    long StreamPosition { get; }
    int Read(Span<float> dest);
    void Seek(long frame);
}