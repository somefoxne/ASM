using BundleCompression.Lzma;
using BundleCompression.Oodle;
using System;
using System.IO;
using System.Text.RegularExpressions;
using K4os.Compression.LZ4;
using ZstdSharp;

namespace AssetStudio
{
    public static class BundleDecompressionHelper
    {
        private static readonly Decompressor ZstdDecompressor = new Decompressor();
        private static readonly string MsgPattern = @"\. ";

        public static MemoryStream DecompressLzmaStream(MemoryStream inStream)
        {
            return SevenZipLzma.DecompressStream(inStream);
        }

        public static long DecompressLzmaStream(Stream compressedStream, Stream decompressedStream, long compressedSize, long decompressedSize, ref string errorMsg)
        {
            var numWrite = -1L;
            try
            {
                numWrite = SevenZipLzma.DecompressStream(compressedStream, decompressedStream, compressedSize, decompressedSize);
            }
            catch (Exception e)
            {
                Logger.Debug(e.ToString());
                errorMsg = $"({Regex.Split(e.Message, MsgPattern, RegexOptions.CultureInvariant)[0]})";
            }
            return numWrite;
        }

        public static int DecompressBlock(CompressionType type, Span<byte> srcBuffer, Span<byte> dstBuffer, ref string errorMsg)
        {
            var numWrite = -1;
            try
            {
                switch (type)
                {
                    case CompressionType.Lz4:
                    case CompressionType.Lz4HC:
                        numWrite = LZ4Codec.Decode(srcBuffer, dstBuffer);
                        break;
                    case CompressionType.Lzham:
                        numWrite = DecompressArknightsBlock(srcBuffer, dstBuffer, ref errorMsg);
                        break;
                    case CompressionType.Zstd:
                        numWrite = ZstdDecompressor.Unwrap(srcBuffer, dstBuffer);
                        break;
                    case CompressionType.Oodle:
                        numWrite = OodleLZ.Decompress(srcBuffer, dstBuffer);
                        break;
                    default:
                        throw new NotSupportedException();
                }
            }
            catch (Exception e)
            {
                Logger.Debug(e.ToString());
                errorMsg = $"({Regex.Split(e.Message, MsgPattern, RegexOptions.CultureInvariant)[0]})";
            }
            return numWrite;
        }

        public static int DecompressArknightsBlock(Span<byte> compressedData, Span<byte> uncompressedSpan, ref string errorMsg)
        {
            var compressedSize = compressedData.Length;
            var uncompressedSize = uncompressedSpan.Length;
            try
            {
                int ip = 0;
                int dataCnt = 0;
                while (true)
                {
                    // Restore token.
                    byte tokenBelied = compressedData[ip];
                    byte realHi4 = (byte)(tokenBelied & 0x0F);
                    byte realLo4 = (byte)((tokenBelied >> 4) & 0x0F);
                    byte token = (byte)((realHi4 << 4) | realLo4);
                    compressedData[ip] = token;
                    ip++;

                    // Get literal length.
                    int literalLength = realHi4;
                    if (realHi4 == 0x0F)
                    {
                        while (true)
                        {
                            byte b = compressedData[ip];
                            ip++;
                            literalLength += b;
                            if (b != 0xFF)
                                break;
                        }
                    }

                    // Skip literals.
                    ip += literalLength;
                    dataCnt += literalLength;

                    if (uncompressedSize - dataCnt < 12) // MFLIMIT end of block 
                    {
                        break;
                    }

                    // Restore offset.
                    byte realHi8 = compressedData[ip];
                    byte realLo8 = compressedData[ip + 1];
                    compressedData[ip] = realLo8;
                    compressedData[ip + 1] = realHi8;
                    ip += 2;

                    // Get match length.
                    int matchLength = realLo4 + 4; // MINMATCH
                    if (realLo4 == 0x0F)
                    {
                        while (true)
                        {
                            byte b = compressedData[ip];
                            ip++;
                            matchLength += b;
                            if (b != 0xFF)
                                break;
                        }
                    }

                    dataCnt += matchLength;
                }

                return LZ4Codec.Decode(compressedData, uncompressedSpan);
            }
            catch (Exception e)
            {
                Logger.Debug(e.ToString());
                errorMsg = $"({Regex.Split(e.Message, MsgPattern, RegexOptions.CultureInvariant)[0]})";
                return -1;
            }
        }
    }
}
