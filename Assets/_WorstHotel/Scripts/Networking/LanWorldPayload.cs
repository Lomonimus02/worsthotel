using System;
using System.IO;
using System.IO.Compression;

namespace WorstHotel
{
    /// <summary>
    /// World-only wire envelope: positive decoded byte count (uint32 little endian),
    /// followed by gzip data. This codec bounds bytes; the caller still validates JSON,
    /// epoch and sequence before applying the authoritative world frame.
    /// </summary>
    public static class LanWorldPayload
    {
        // Fastest DEFLATE can use nine bits for an incompressible input byte on some
        // runtimes. Permit bounded expansion rather than assuming it always shrinks.
        public const int MaxPacketOverhead = LanProtocol.MaxSnapshotBytes / 8 + 1024;
        const int PrefixBytes = 4, GzipHeaderBytes = 10, GzipTrailerBytes = 8;
        static readonly uint[] CrcTable = CreateCrcTable();

        public static byte[] Encode(byte[] json, int limit)
        {
            if (!ValidLimit(limit)) throw new ArgumentOutOfRangeException(nameof(limit));
            if (json == null) throw new ArgumentNullException(nameof(json));
            if (json.Length == 0 || json.Length > limit) throw new ArgumentException("World JSON exceeds its decoded bound.", nameof(json));
            using (var output = new MemoryStream(json.Length + MaxPacketOverhead))
            {
                uint size = (uint)json.Length;
                for (int shift = 0; shift < 32; shift += 8) output.WriteByte((byte)(size >> shift));
                using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true))
                    gzip.Write(json, 0, json.Length);
                if (output.Length > limit + MaxPacketOverhead)
                    throw new InvalidOperationException("Compressed world payload exceeds its wire bound.");
                return output.ToArray();
            }
        }

        public static bool TryDecode(byte[] packet, int limit, out byte[] json)
        {
            json = null;
            if (!ValidLimit(limit) || packet == null ||
                packet.Length < PrefixBytes + GzipHeaderBytes + GzipTrailerBytes || packet.Length > limit + MaxPacketOverhead) return false;
            uint declared = ReadUInt32(packet, 0);
            if (declared == 0 || declared > (uint)limit) return false;
            // Encode emits ordinary gzip with no optional filename/comment/extra fields.
            if (packet[4] != 0x1f || packet[5] != 0x8b || packet[6] != 8 || packet[7] != 0) return false;
            if (ReadUInt32(packet, packet.Length - 4) != declared) return false;
            uint expectedCrc = ReadUInt32(packet, packet.Length - GzipTrailerBytes);
            try
            {
                // The allocation is controlled only by an already validated small decoded size.
                // Never CopyTo a growable stream: a forged compressed member may expand much further.
                var decoded = new byte[(int)declared];
                using (var source = new MemoryStream(packet, PrefixBytes, packet.Length - PrefixBytes, false))
                using (var gzip = new GZipStream(source, CompressionMode.Decompress))
                {
                    int offset = 0;
                    while (offset < decoded.Length)
                    {
                        int count = gzip.Read(decoded, offset, decoded.Length - offset);
                        if (count <= 0) return false;
                        offset += count;
                    }
                    if (gzip.ReadByte() != -1) return false;
                }
                // Verify ourselves as well: gzip EOF/truncation behaviour varies between
                // managed runtimes, and a footer must never turn a partial frame into valid JSON.
                if (Crc32(decoded) != expectedCrc) return false;
                json = decoded;
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is ArgumentException || exception is InvalidOperationException)
            { return false; }
        }

        static bool ValidLimit(int limit) => limit > 0 && limit <= LanProtocol.MaxSnapshotBytes;
        static uint ReadUInt32(byte[] data, int at) => (uint)data[at] | (uint)data[at + 1] << 8 |
            (uint)data[at + 2] << 16 | (uint)data[at + 3] << 24;

        static uint Crc32(byte[] data)
        {
            uint crc = uint.MaxValue;
            foreach (byte value in data) crc = CrcTable[(int)((crc ^ value) & 255)] ^ (crc >> 8);
            return ~crc;
        }

        static uint[] CreateCrcTable()
        {
            var table = new uint[256];
            for (int index = 0; index < table.Length; index++)
            {
                uint crc = (uint)index;
                for (int bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? 0xedb88320u ^ (crc >> 1) : crc >> 1;
                table[index] = crc;
            }
            return table;
        }
    }
}
