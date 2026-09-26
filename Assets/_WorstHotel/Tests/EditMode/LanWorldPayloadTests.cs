using System;
using System.IO;
using System.IO.Compression;
using CompressionLevel = System.IO.Compression.CompressionLevel;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace WorstHotel.Tests
{
    public sealed class LanWorldPayloadTests
    {
        const int Limit = LanProtocol.MaxSnapshotBytes;

        [Test]
        public void LargeWorldFrameRoundTripsExactUtf8BytesAndRemovesRepeatedJsonOverhead()
        {
            var frame = new LanWorldFrame
            {
                epoch = 1947, sequence = 81,
                visuals = Enumerable.Range(0, 600).Select(i => new LanWorldVisual
                { id = i.ToString("x16"), active = true, enabled = true, color = Color.white, emission = Color.black }).ToArray(),
                objects = Enumerable.Range(0, 100).Select(i => new LanWorldObject
                { id = "supply:" + i, active = true, position = new Vector3(i % 6, 1, i / 6), rotation = Quaternion.identity }).ToArray(),
                texts = Enumerable.Range(0, 40).Select(i => new LanWorldText
                { id = "label:" + i, text = "КОМНАТА / ROOM " + (101 + i % 6) + " · CLEAN BLANKETS", color = Color.white }).ToArray()
            };
            var json = Encoding.UTF8.GetBytes(JsonUtility.ToJson(frame));
            Assert.That(json.Length, Is.GreaterThan(100000), "Exercise a real large world schema, not a tiny scalar.");
            var packet = LanWorldPayload.Encode(json, Limit);
            Assert.That(packet.Length, Is.LessThan(json.Length / 3), "Repeated world field names and static values must not occupy the reliable queue verbatim.");
            Assert.That(LanWorldPayload.TryDecode(packet, Limit, out var decoded), Is.True);
            Assert.That(decoded, Is.EqualTo(json));
            Assert.That(JsonUtility.FromJson<LanWorldFrame>(Encoding.UTF8.GetString(decoded)).visuals.Length, Is.EqualTo(600));
            TestContext.WriteLine("Representative world: JSON=" + json.Length + " bytes, gzip envelope=" + packet.Length + " bytes.");
        }

        [Test]
        public void IncompressibleBytesAtDecodedLimitStayBoundedAndRoundTrip()
        {
            var bytes = new byte[Limit]; new System.Random(1947).NextBytes(bytes);
            var packet = LanWorldPayload.Encode(bytes, Limit);
            Assert.That(packet.Length, Is.LessThanOrEqualTo(Limit + LanWorldPayload.MaxPacketOverhead));
            Assert.That(LanWorldPayload.TryDecode(packet, Limit, out var decoded), Is.True);
            Assert.That(decoded, Is.EqualTo(bytes));
        }

        [Test]
        public void InvalidLengthsAndBoundsAreRejectedBeforeDecodedAllocation()
        {
            var valid = LanWorldPayload.Encode(Encoding.UTF8.GetBytes("{\"world\":true}"), Limit);
            foreach (uint length in new[] { 0u, (uint)Limit + 1, uint.MaxValue })
            {
                var packet = (byte[])valid.Clone(); WriteUInt32(packet, 0, length);
                AssertRejected(packet, Limit);
            }
            AssertRejected(null, Limit);
            AssertRejected(new byte[21], Limit);
            AssertRejected(new byte[Limit + LanWorldPayload.MaxPacketOverhead + 1], Limit);
            AssertRejected(valid, 0); AssertRejected(valid, -1); AssertRejected(valid, Limit + 1);
            AssertRejected(valid, 2);
            Assert.Throws<ArgumentException>(() => LanWorldPayload.Encode(new byte[3], 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => LanWorldPayload.Encode(new byte[1], int.MaxValue));
        }

        [Test]
        public void TruncatedMembersAndCorruptedCompressedDataOrCrcNeverProducePartialJson()
        {
            var raw = new byte[2048]; new System.Random(31).NextBytes(raw);
            var valid = LanWorldPayload.Encode(raw, Limit);
            for (int removed = 1; removed <= 32; removed++) AssertRejected(valid.Take(valid.Length - removed).ToArray(), Limit);
            foreach (int offset in new[] { 4, 6, 7, 16, valid.Length / 2, valid.Length - 8, valid.Length - 1 })
            {
                var packet = (byte[])valid.Clone(); packet[offset] ^= 0x80;
                AssertRejected(packet, Limit);
            }
            var wrongDeclared = (byte[])valid.Clone(); WriteUInt32(wrongDeclared, 0, (uint)raw.Length - 1);
            AssertRejected(wrongDeclared, Limit);
        }

        [Test]
        public void ForgedCompressedExpansionIsRejectedAtTheClaimedBound()
        {
            var bomb = new byte[Limit + 10000];
            for (int i = 0; i < bomb.Length; i++) bomb[i] = (byte)'x';
            // Deliberately construct an envelope that Encode refuses: compressed bytes are
            // small, but their decoded data exceed both the claimed size and the protocol cap.
            byte[] packet;
            using (var output = new MemoryStream())
            {
                output.Write(new byte[4], 0, 4);
                using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true)) gzip.Write(bomb, 0, bomb.Length);
                packet = output.ToArray();
            }
            WriteUInt32(packet, 0, 32);
            WriteUInt32(packet, packet.Length - 4, 32); // Defeat the cheap ISIZE mismatch gate.
            Assert.That(packet.Length, Is.LessThan(Limit));
            AssertRejected(packet, Limit);
            Assert.Throws<ArgumentException>(() => LanWorldPayload.Encode(bomb, Limit));
        }

        [Test]
        public void ShortOrExtraDecodedOutputCannotPassEvenWithMatchingEnvelopeAndFooterSizes()
        {
            var raw = Encoding.UTF8.GetBytes("{\"world\":\"a complete payload\"}");
            var valid = LanWorldPayload.Encode(raw, Limit);
            foreach (int difference in new[] { -1, 1 })
            {
                var packet = (byte[])valid.Clone();
                WriteUInt32(packet, 0, (uint)(raw.Length + difference));
                WriteUInt32(packet, packet.Length - 4, (uint)(raw.Length + difference));
                AssertRejected(packet, Limit);
            }
        }

        static void AssertRejected(byte[] packet, int limit)
        {
            Assert.That(LanWorldPayload.TryDecode(packet, limit, out var decoded), Is.False);
            Assert.That(decoded, Is.Null, "A rejected compressed envelope must expose no partial frame.");
        }

        static void WriteUInt32(byte[] bytes, int offset, uint value)
        { for (int index = 0; index < 4; index++) bytes[offset + index] = (byte)(value >> (index * 8)); }
    }
}
