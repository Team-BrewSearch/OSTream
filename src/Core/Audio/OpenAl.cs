using System;
using System.Reflection;
using System.Runtime.InteropServices;
namespace OSTream.Core.Audio;

internal static partial class OpenAl
{
    private const string Lib = "openal";
    public const int AL_SOURCE_RELATIVE = 0x202;
    public const int AL_GAIN = 0x100A;
    public const int AL_SOURCE_STATE = 0x1010;
    public const int AL_PLAYING = 0x1012;
    public const int AL_BUFFERS_QUEUED = 0x1015;
    public const int AL_BUFFERS_PROCESSED = 0x1016;
    public const int AL_SAMPLE_OFFSET = 0x1025;
    public const int AL_FORMAT_MONO16 = 0x1101;
    public const int AL_FORMAT_STEREO16 = 0x1103;
    public const int AL_FORMAT_MONO_FLOAT32 = 0x10010;
    public const int AL_FORMAT_STEREO_FLOAT32 = 0x10011;
    public const int ALC_CONNECTED = 0x313;
    static OpenAl()
    {
        NativeLibrary.SetDllImportResolver(typeof(OpenAl).Assembly, Resolve);
    }
    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
    {
        if (name != Lib)
        {
            return IntPtr.Zero;
        }
        string[] candidates = OperatingSystem.IsWindows() ? ["soft_oal.dll", "OpenAL32.dll"]
            : OperatingSystem.IsMacOS() ? ["libopenal.dylib", "libopenal.1.dylib"]
            : ["libopenal.so", "libopenal.so.1"];
        foreach (string candidate in candidates)
        {
            if (NativeLibrary.TryLoad(candidate, assembly, path, out nint handle))
            {
                return handle;
            }
        }
        return IntPtr.Zero;
    }
    [DllImport(Lib, EntryPoint = "alcOpenDevice", CharSet = CharSet.Ansi)]
    public static extern IntPtr alcOpenDevice(string? deviceName);
    [DllImport(Lib, EntryPoint = "alcCloseDevice")]
    public static extern byte alcCloseDevice(IntPtr device);
    [DllImport(Lib, EntryPoint = "alcCreateContext")]
    public static extern IntPtr alcCreateContext(IntPtr device, int[]? attributes);
    [DllImport(Lib, EntryPoint = "alcMakeContextCurrent")]
    public static extern byte alcMakeContextCurrent(IntPtr context);
    [DllImport(Lib, EntryPoint = "alcDestroyContext")]
    public static extern void alcDestroyContext(IntPtr context);
    [DllImport(Lib, EntryPoint = "alcGetIntegerv")]
    public static extern void alcGetIntegerv(IntPtr device, int param, int size, out int value);
    [DllImport(Lib, EntryPoint = "alcIsExtensionPresent", CharSet = CharSet.Ansi)]
    public static extern byte alcIsExtensionPresent(IntPtr device, string name);
    [DllImport(Lib, EntryPoint = "alcGetProcAddress", CharSet = CharSet.Ansi)]
    public static extern IntPtr alcGetProcAddress(IntPtr device, string name);
    [DllImport(Lib, EntryPoint = "alGetError")]
    public static extern int alGetError();
    [DllImport(Lib, EntryPoint = "alIsExtensionPresent", CharSet = CharSet.Ansi)]
    public static extern byte alIsExtensionPresent(string name);
    [DllImport(Lib, EntryPoint = "alGenSources")]
    public static extern void alGenSources(int n, [Out] uint[] sources);
    [DllImport(Lib, EntryPoint = "alDeleteSources")]
    public static extern void alDeleteSources(int n, uint[] sources);
    [DllImport(Lib, EntryPoint = "alGenBuffers")]
    public static extern void alGenBuffers(int n, [Out] uint[] buffers);
    [DllImport(Lib, EntryPoint = "alDeleteBuffers")]
    public static extern void alDeleteBuffers(int n, uint[] buffers);
    [DllImport(Lib, EntryPoint = "alBufferData")]
    public static extern void alBufferData(uint buffer, int format, float[] data, int size, int frequency);
    [DllImport(Lib, EntryPoint = "alBufferData")]
    public static extern void alBufferData(uint buffer, int format, short[] data, int size, int frequency);
    [DllImport(Lib, EntryPoint = "alSourceQueueBuffers")]
    public static extern void alSourceQueueBuffers(uint source, int n, uint[] buffers);
    [DllImport(Lib, EntryPoint = "alSourceUnqueueBuffers")]
    public static extern void alSourceUnqueueBuffers(uint source, int n, [Out] uint[] buffers);
    [DllImport(Lib, EntryPoint = "alSourcePlay")]
    public static extern void alSourcePlay(uint source);
    [DllImport(Lib, EntryPoint = "alSourcePause")]
    public static extern void alSourcePause(uint source);
    [DllImport(Lib, EntryPoint = "alSourceStop")]
    public static extern void alSourceStop(uint source);
    [DllImport(Lib, EntryPoint = "alSourcei")]
    public static extern void alSourcei(uint source, int param, int value);
    [DllImport(Lib, EntryPoint = "alSourcef")]
    public static extern void alSourcef(uint source, int param, float value);
    [DllImport(Lib, EntryPoint = "alGetSourcei")]
    public static extern void alGetSourcei(uint source, int param, out int value);
    public static int GetSource(uint source, int param)
    {
        alGetSourcei(source, param, out int value);
        return value;
    }
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate byte ReopenDeviceDelegate(IntPtr device, IntPtr deviceName, IntPtr attributes);
    private static ReopenDeviceDelegate? _reopen;
    public static bool ReopenDeviceIfNeeded(IntPtr device)
    {
        if (!HasAlcExtension(device, "ALC_SOFT_reopen_device"))
        {
            return false;
        }
        if (_reopen == null)
        {
            nint ptr = AlcProc(device, "alcReopenDeviceSOFT");
            if (ptr == IntPtr.Zero)
            {
                return false;
            }
            _reopen = (ReopenDeviceDelegate)Marshal.GetDelegateForFunctionPointer(ptr, typeof(ReopenDeviceDelegate));
        }
        return _reopen(device, IntPtr.Zero, IntPtr.Zero) != 0;
    }
    public static bool HasAlcExtension(IntPtr device, string name)
    {
        return alcIsExtensionPresent(device, name) != 0;
    }
    public static bool HasAlExtension(string name)
    {
        return alIsExtensionPresent(name) != 0;
    }
    public static IntPtr AlcProc(IntPtr device, string name)
    {
        return alcGetProcAddress(device, name);
    }
}
