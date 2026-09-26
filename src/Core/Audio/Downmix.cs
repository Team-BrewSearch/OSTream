using System;
namespace OSTream.Core.Audio;
internal static class Downmix
{
    public static void ToStereo(ReadOnlySpan<float> source, int channels, Span<float> dest, int frames)
    {
        const float c = 0.7071f;
        for (var i = 0; i < frames; i++)
        {
            var ch = source.Slice(i * channels, channels);
            float left = ch[0], right = ch[1];
            switch (channels)
            {
                case 3: left += c * ch[2]; right += c * ch[2]; break;
                case 4: left += c * ch[2]; right += c * ch[3]; break;
                case 5: left += c * (ch[2] + ch[3]); right += c * (ch[2] + ch[4]); break;
                default: left += c * (ch[2] + ch[4]); right += c * (ch[2] + ch[5]); break;
            }
            dest[2 * i] = left * 0.5f;
            dest[2 * i + 1] = right * 0.5f;
        }
    }
}