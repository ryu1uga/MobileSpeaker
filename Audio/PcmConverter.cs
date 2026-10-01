using System.Buffers.Binary;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace MobileSpeaker.Audio;

/// <summary>
/// Convierte el mix format de Windows (float32 / PCM 16, 24, 32 bits, normal o Extensible,
/// con cualquier cantidad de canales) a PCM 16 bits estereo intercalado little-endian.
/// Mantiene el sample rate original.
/// </summary>
public sealed class PcmConverter
{
    private static readonly Guid SubTypePcm = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid SubTypeFloat = new("00000003-0000-0010-8000-00aa00389b71");

    // Bits de dwChannelMask (ksmedia.h)
    private const int SpeakerFrontLeft = 0x1;
    private const int SpeakerFrontRight = 0x2;
    private const int SpeakerFrontCenter = 0x4;
    private const int SpeakerLowFrequency = 0x8;
    private const int SpeakerBackLeft = 0x10;
    private const int SpeakerBackRight = 0x20;
    private const int SpeakerFrontLeftOfCenter = 0x40;
    private const int SpeakerFrontRightOfCenter = 0x80;
    private const int SpeakerBackCenter = 0x100;
    private const int SpeakerSideLeft = 0x200;
    private const int SpeakerSideRight = 0x400;

    private const float Minus3dB = 0.7071f;

    private enum SampleKind { Float32, Int16, Int24, Int32 }

    private readonly SampleKind _kind;
    private readonly int _channels;
    private readonly int _bytesPerSample;
    private readonly int _blockAlign;
    private readonly float[] _weightLeft;
    private readonly float[] _weightRight;

    public int SampleRate { get; }
    public string SourceDescription { get; }

    public PcmConverter(WaveFormat format)
    {
        _channels = format.Channels;
        if (_channels < 1)
            throw new NotSupportedException("El formato de audio no tiene canales.");

        SampleRate = format.SampleRate;
        _bytesPerSample = format.BitsPerSample / 8;
        _blockAlign = format.BlockAlign > 0 ? format.BlockAlign : _channels * _bytesPerSample;

        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat;
        bool isPcm = format.Encoding == WaveFormatEncoding.Pcm;
        bool isExtensible = false;
        int channelMask = 0;

        if (format is WaveFormatExtensible ext)
        {
            isExtensible = true;
            isFloat = ext.SubFormat == SubTypeFloat;
            isPcm = ext.SubFormat == SubTypePcm;
            channelMask = ReadChannelMask(ext);
        }

        _kind = (isFloat, isPcm, format.BitsPerSample) switch
        {
            (true, _, 32) => SampleKind.Float32,
            (_, true, 16) => SampleKind.Int16,
            (_, true, 24) => SampleKind.Int24,
            (_, true, 32) => SampleKind.Int32,
            _ => throw new NotSupportedException(
                $"Formato de captura no soportado: {format.Encoding}, {format.BitsPerSample} bits.")
        };

        (_weightLeft, _weightRight) = BuildDownmixWeights(_channels, channelMask);

        string kindText = _kind == SampleKind.Float32 ? "float" : "PCM";
        SourceDescription =
            $"{SampleRate} Hz, {format.BitsPerSample} bits {kindText}, {_channels} canal(es)" +
            (isExtensible ? " (Extensible)" : string.Empty);
    }

    /// <summary>
    /// Convierte un bloque capturado. Devuelve un arreglo nuevo (se comparte entre clientes, no se modifica).
    /// </summary>
    public byte[] Convert(byte[] buffer, int byteCount)
    {
        int frames = byteCount / _blockAlign;
        if (frames <= 0)
            return Array.Empty<byte>();

        var output = new byte[frames * 4];
        var src = buffer.AsSpan(0, frames * _blockAlign);
        var dst = output.AsSpan();

        for (int f = 0; f < frames; f++)
        {
            int frameOffset = f * _blockAlign;
            float left = 0f, right = 0f;

            for (int c = 0; c < _channels; c++)
            {
                float wl = _weightLeft[c];
                float wr = _weightRight[c];
                if (wl == 0f && wr == 0f)
                    continue;

                float sample = ReadSample(src.Slice(frameOffset + c * _bytesPerSample, _bytesPerSample));
                left += sample * wl;
                right += sample * wr;
            }

            BinaryPrimitives.WriteInt16LittleEndian(dst.Slice(f * 4, 2), ToInt16(left));
            BinaryPrimitives.WriteInt16LittleEndian(dst.Slice(f * 4 + 2, 2), ToInt16(right));
        }

        return output;
    }

    private float ReadSample(ReadOnlySpan<byte> s)
    {
        switch (_kind)
        {
            case SampleKind.Float32:
                return BinaryPrimitives.ReadSingleLittleEndian(s);
            case SampleKind.Int16:
                return BinaryPrimitives.ReadInt16LittleEndian(s) / 32768f;
            case SampleKind.Int24:
                int v = s[0] | (s[1] << 8) | ((sbyte)s[2] << 16);
                return v / 8388608f;
            default:
                return BinaryPrimitives.ReadInt32LittleEndian(s) / 2147483648f;
        }
    }

    private static short ToInt16(float value)
    {
        if (float.IsNaN(value))
            return 0;
        if (value >= 1f)
            return short.MaxValue;
        if (value <= -1f)
            return short.MinValue;
        return (short)MathF.Round(value * 32767f);
    }

    /// <summary>
    /// Pesos de mezcla por canal hacia L y R. Si hay mascara de canales se usa para ubicar
    /// centro y surround; si no, se toma canal 0 como L y canal 1 como R.
    /// </summary>
    private static (float[] left, float[] right) BuildDownmixWeights(int channels, int mask)
    {
        var left = new float[channels];
        var right = new float[channels];

        if (channels == 1)
        {
            left[0] = 1f;
            right[0] = 1f;
            return (left, right);
        }

        if (mask != 0 && System.Numerics.BitOperations.PopCount((uint)mask) == channels)
        {
            int channel = 0;
            for (int bit = 0; bit < 32 && channel < channels; bit++)
            {
                int speaker = 1 << bit;
                if ((mask & speaker) == 0)
                    continue;

                switch (speaker)
                {
                    case SpeakerFrontLeft:
                    case SpeakerFrontLeftOfCenter:
                        left[channel] = 1f;
                        break;
                    case SpeakerFrontRight:
                    case SpeakerFrontRightOfCenter:
                        right[channel] = 1f;
                        break;
                    case SpeakerFrontCenter:
                        left[channel] = Minus3dB;
                        right[channel] = Minus3dB;
                        break;
                    case SpeakerBackLeft:
                    case SpeakerSideLeft:
                        left[channel] = Minus3dB;
                        break;
                    case SpeakerBackRight:
                    case SpeakerSideRight:
                        right[channel] = Minus3dB;
                        break;
                    case SpeakerBackCenter:
                        left[channel] = 0.5f;
                        right[channel] = 0.5f;
                        break;
                    case SpeakerLowFrequency:
                    default:
                        break; // LFE y canales superiores se descartan
                }
                channel++;
            }

            if (left.Any(w => w != 0f) && right.Any(w => w != 0f))
                return (left, right);

            Array.Clear(left);
            Array.Clear(right);
        }

        left[0] = 1f;
        right[1] = 1f;
        return (left, right);
    }

    /// <summary>
    /// NAudio no expone dwChannelMask; se lee de la estructura WAVEFORMATEXTENSIBLE serializada
    /// (WAVEFORMATEX de 18 bytes + wValidBitsPerSample de 2 bytes => offset 20).
    /// </summary>
    private static int ReadChannelMask(WaveFormatExtensible ext)
    {
        try
        {
            int size = Marshal.SizeOf(ext);
            if (size < 24)
                return 0;

            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(ext, ptr, false);
                return Marshal.ReadInt32(ptr, 20);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch
        {
            return 0;
        }
    }
}
