using System.Globalization;
using RtspClientSharp.RawFrames;
using RtspClientSharp.RawFrames.Video;

namespace RtspClientSharp.Web.Services;

public enum BrowserMediaKind
{
    Mjpeg,
    H264,
    H265,
    Unsupported
}

public sealed record PublishedFrame
{
    public BrowserMediaKind Kind { get; init; }
    public byte[] Data { get; init; } = Array.Empty<byte>();
    public bool KeyFrame { get; init; }
    public long Sequence { get; init; }
    public string? Codec { get; init; }
    public DateTimeOffset CapturedAt { get; init; }
}

public static class BrowserPublisher
{
    public static bool TryCreate(RawFrame frame, long sequence, out PublishedFrame published)
    {
        switch (frame)
        {
            case RawJpegFrame:
                published = new PublishedFrame
                {
                    Kind = BrowserMediaKind.Mjpeg,
                    Data = Copy(frame.FrameSegment),
                    KeyFrame = true,
                    Sequence = sequence,
                    Codec = "mjpeg",
                    CapturedAt = DateTimeOffset.UtcNow
                };
                return true;

            case RawH264IFrame h264IFrame:
                published = new PublishedFrame
                {
                    Kind = BrowserMediaKind.H264,
                    Data = Combine(h264IFrame.SpsPpsSegment, h264IFrame.FrameSegment),
                    KeyFrame = true,
                    Sequence = sequence,
                    Codec = GetH264CodecString(h264IFrame.SpsPpsSegment),
                    CapturedAt = DateTimeOffset.UtcNow
                };
                return true;

            case RawH264PFrame:
                published = new PublishedFrame
                {
                    Kind = BrowserMediaKind.H264,
                    Data = Copy(frame.FrameSegment),
                    KeyFrame = false,
                    Sequence = sequence,
                    CapturedAt = DateTimeOffset.UtcNow
                };
                return true;

            case RawH265IFrame h265IFrame:
                published = new PublishedFrame
                {
                    Kind = BrowserMediaKind.H265,
                    Data = Combine(h265IFrame.ParametersBytesSegment, h265IFrame.FrameSegment),
                    KeyFrame = true,
                    Sequence = sequence,
                    Codec = GetH265CodecString(h265IFrame.ParametersBytesSegment),
                    CapturedAt = DateTimeOffset.UtcNow
                };
                return true;

            case RawH265PFrame:
                published = new PublishedFrame
                {
                    Kind = BrowserMediaKind.H265,
                    Data = Copy(frame.FrameSegment),
                    KeyFrame = false,
                    Sequence = sequence,
                    CapturedAt = DateTimeOffset.UtcNow
                };
                return true;

            default:
                published = null!;
                return false;
        }
    }

    public static byte[] CreateWebSocketPacket(PublishedFrame frame)
    {
        byte[] packet = new byte[9 + frame.Data.Length];
        packet[0] = frame.KeyFrame ? (byte)1 : (byte)0;
        Buffer.BlockCopy(BitConverter.GetBytes(frame.Sequence * 33333L), 0, packet, 1, 8);
        Buffer.BlockCopy(frame.Data, 0, packet, 9, frame.Data.Length);
        return packet;
    }

    public static byte[] CreateH264WebSocketPacket(PublishedFrame frame)
    {
        return CreateWebSocketPacket(frame);
    }

    public static string GetH264CodecString(ArraySegment<byte> spsPps)
    {
        if (spsPps.Array != null)
        {
            int end = spsPps.Offset + spsPps.Count;
            for (int index = spsPps.Offset; index + 7 < end; index++)
            {
                if (spsPps.Array[index] != 0 || spsPps.Array[index + 1] != 0 ||
                    spsPps.Array[index + 2] != 0 || spsPps.Array[index + 3] != 1)
                    continue;

                int nalOffset = index + 4;
                if ((spsPps.Array[nalOffset] & 0x1F) == 7 && nalOffset + 3 < end)
                {
                    return $"avc1.{spsPps.Array[nalOffset + 1]:X2}"
                         + $"{spsPps.Array[nalOffset + 2]:X2}{spsPps.Array[nalOffset + 3]:X2}";
                }
            }
        }

        return "avc1.42E01E";
    }

    public static string GetH265CodecString(ArraySegment<byte> parameters)
    {
        if (!TryReadH265ProfileTierLevel(parameters, out H265ProfileTierLevel profile))
            return "hvc1.1.6.L120.B0";

        string profileSpace = profile.ProfileSpace switch
        {
            1 => "A",
            2 => "B",
            3 => "C",
            _ => string.Empty
        };

        return $"hvc1.{profileSpace}{profile.ProfileIdc}.{FormatHex(profile.CompatibilityFlags)}.{(profile.TierFlag ? "H" : "L")}{profile.LevelIdc}.{FormatConstraintFlags(profile.ConstraintFlags)}";
    }

    private static bool TryReadH265ProfileTierLevel(ArraySegment<byte> parameters, out H265ProfileTierLevel profile)
    {
        profile = default;

        if (!TryGetH265SpsRbsp(parameters, out byte[] rbsp))
            return false;

        var reader = new BitReader(rbsp);
        if (!reader.TrySkip(4) ||
            !reader.TryRead(3, out ulong maxSubLayersMinus1) ||
            !reader.TrySkip(1) ||
            !reader.TryRead(2, out ulong profileSpace) ||
            !reader.TryRead(1, out ulong tierFlag) ||
            !reader.TryRead(5, out ulong profileIdc) ||
            !reader.TryRead(32, out ulong compatibilityFlags) ||
            !reader.TryRead(48, out ulong constraintFlags) ||
            !reader.TryRead(8, out ulong levelIdc))
            return false;

        int maxSubLayers = (int)maxSubLayersMinus1;
        bool[] subLayerProfilePresent = new bool[maxSubLayers];
        bool[] subLayerLevelPresent = new bool[maxSubLayers];
        for (int index = 0; index < maxSubLayers; index++)
        {
            if (!reader.TryRead(1, out ulong profilePresent) ||
                !reader.TryRead(1, out ulong levelPresent))
                return false;

            subLayerProfilePresent[index] = profilePresent != 0;
            subLayerLevelPresent[index] = levelPresent != 0;
        }

        for (int index = maxSubLayers; index < 8; index++)
        {
            if (!reader.TrySkip(2))
                return false;
        }

        for (int index = 0; index < maxSubLayers; index++)
        {
            if (subLayerProfilePresent[index] && !reader.TrySkip(2 + 1 + 5 + 32 + 48))
                return false;

            if (subLayerLevelPresent[index] && !reader.TrySkip(8))
                return false;
        }

        profile = new H265ProfileTierLevel(
            (int)profileSpace,
            (int)profileIdc,
            compatibilityFlags,
            tierFlag != 0,
            (int)levelIdc,
            constraintFlags);
        return true;
    }

    private static bool TryGetH265SpsRbsp(ArraySegment<byte> parameters, out byte[] rbsp)
    {
        rbsp = Array.Empty<byte>();
        if (parameters.Array is null || parameters.Count < 3)
            return false;

        int end = parameters.Offset + parameters.Count;
        int cursor = parameters.Offset;
        while (TryFindStartCode(parameters.Array, cursor, end, out int startCodeOffset, out int startCodeLength))
        {
            int nalStart = startCodeOffset + startCodeLength;
            if (nalStart + 2 > end)
                return false;

            int nalEnd = end;
            if (TryFindStartCode(parameters.Array, nalStart, end, out int nextStartCodeOffset, out _))
                nalEnd = nextStartCodeOffset;

            int nalType = (parameters.Array[nalStart] & 0x7E) >> 1;
            if (nalType == 33)
            {
                var payload = new List<byte>(nalEnd - nalStart - 2);
                int zeroCount = 0;
                for (int index = nalStart + 2; index < nalEnd; index++)
                {
                    byte value = parameters.Array[index];
                    if (zeroCount >= 2 && value == 0x03 && index + 1 < nalEnd && parameters.Array[index + 1] <= 0x03)
                    {
                        zeroCount = 0;
                        continue;
                    }

                    payload.Add(value);
                    zeroCount = value == 0 ? zeroCount + 1 : 0;
                }

                rbsp = payload.ToArray();
                return rbsp.Length > 0;
            }

            if (nalEnd >= end)
                break;

            cursor = nalEnd;
        }

        return false;
    }

    private static bool TryFindStartCode(byte[] data, int offset, int end, out int startCodeOffset, out int startCodeLength)
    {
        for (int index = offset; index + 2 < end; index++)
        {
            if (data[index] != 0 || data[index + 1] != 0)
                continue;

            if (data[index + 2] == 1)
            {
                startCodeOffset = index;
                startCodeLength = 3;
                return true;
            }

            if (index + 3 < end && data[index + 2] == 0 && data[index + 3] == 1)
            {
                startCodeOffset = index;
                startCodeLength = 4;
                return true;
            }
        }

        startCodeOffset = -1;
        startCodeLength = 0;
        return false;
    }

    private static string FormatHex(ulong value)
    {
        return value.ToString("X", CultureInfo.InvariantCulture);
    }

    private static string FormatConstraintFlags(ulong value)
    {
        var bytes = new byte[6];
        for (int index = 0; index < bytes.Length; index++)
            bytes[index] = (byte)(value >> ((bytes.Length - index - 1) * 8));

        int lastNonZero = bytes.Length - 1;
        while (lastNonZero > 0 && bytes[lastNonZero] == 0)
            lastNonZero--;

        string[] fields = new string[lastNonZero + 1];
        for (int index = 0; index <= lastNonZero; index++)
            fields[index] = bytes[index].ToString("X2", CultureInfo.InvariantCulture);

        return string.Join('.', fields);
    }

    private static byte[] Combine(ArraySegment<byte> first, ArraySegment<byte> second)
    {
        if (first.Count == 0)
            return Copy(second);
        if (second.Count == 0)
            return Copy(first);

        byte[] combined = new byte[first.Count + second.Count];
        Buffer.BlockCopy(first.Array!, first.Offset, combined, 0, first.Count);
        Buffer.BlockCopy(second.Array!, second.Offset, combined, first.Count, second.Count);
        return combined;
    }

    private static byte[] Copy(ArraySegment<byte> segment)
    {
        if (segment.Array == null || segment.Count == 0)
            return Array.Empty<byte>();

        byte[] copy = new byte[segment.Count];
        Buffer.BlockCopy(segment.Array, segment.Offset, copy, 0, segment.Count);
        return copy;
    }

    private readonly record struct H265ProfileTierLevel(
        int ProfileSpace,
        int ProfileIdc,
        ulong CompatibilityFlags,
        bool TierFlag,
        int LevelIdc,
        ulong ConstraintFlags);

    private sealed class BitReader
    {
        private readonly byte[] _data;
        private int _position;

        public BitReader(byte[] data)
        {
            _data = data;
        }

        public bool TryRead(int count, out ulong value)
        {
            value = 0;
            if (count < 0 || count > 64 || _position + count > _data.Length * 8)
                return false;

            for (int index = 0; index < count; index++)
            {
                value = (value << 1) | (uint)((_data[_position / 8] >> (7 - (_position % 8))) & 1);
                _position++;
            }

            return true;
        }

        public bool TrySkip(int count)
        {
            if (count < 0 || _position + count > _data.Length * 8)
                return false;

            _position += count;
            return true;
        }
    }
}
