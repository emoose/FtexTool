using System;
using System.IO;
using System.Text;

namespace FtexTool.Ftexs
{
    public class FtexsFileChunk
    {
        public const int IndexSize = 8;
        private const int OffsetBitMask = 0xFFFF;

        public ushort CompressedChunkSize
        {
            get { return Convert.ToUInt16(CompressedChunkData.Length); }
        }

        public ushort ChunkSize
        {
            get { return Convert.ToUInt16(ChunkData.Length); }
        }

        public uint Offset { get; set; }
        public byte[] ChunkData { get; private set; }
        public byte[] CompressedChunkData { get; private set; }

        public static FtexsFileChunk ReadFtexsFileChunk(Stream inputStream, bool absoluteOffset, bool flipEndian = false)
        {
            FtexsFileChunk result = new FtexsFileChunk();
            result.Read(inputStream, absoluteOffset, flipEndian);
            return result;
        }

        public void Read(Stream inputStream, bool absoluteOffset, bool flipEndian = false)
        {
            X360Reader reader = new X360Reader(inputStream, Encoding.Default, true, flipEndian);
            ushort compressedChunkSize = reader.ReadUInt16();
            ushort decompressedChunkSize = reader.ReadUInt16();
            Offset = reader.ReadUInt32();

            long indexEndPosition = reader.BaseStream.Position;

            if (absoluteOffset)
            {
                reader.BaseStream.Position = Offset;
            }
            else
            {
                // HACK: result.Offset could be 0x80000008
                reader.BaseStream.Position = indexEndPosition + (Offset & OffsetBitMask) - IndexSize;
            }

            byte[] data = reader.ReadBytes(compressedChunkSize);
            bool dataCompressed = compressedChunkSize != decompressedChunkSize;
            SetData(data, dataCompressed, decompressedChunkSize);

            reader.BaseStream.Position = indexEndPosition;
        }

        public void Write(Stream outputStream, bool flipEndian)
        {
            X360Writer writer = new X360Writer(outputStream, Encoding.Default, true, flipEndian);
            writer.Write(CompressedChunkSize);
            writer.Write(ChunkSize);
            writer.Write(Offset);
        }

        public void WriteData(Stream outputStream, bool writeCompressedData)
        {
            BinaryWriter writer = new BinaryWriter(outputStream, Encoding.Default, true);
            if (writeCompressedData)
            {
                writer.Write(CompressedChunkData);
            }
            else
            {
                writer.Write(ChunkData);
            }
        }

        public void SetData(byte[] chunkData, bool compressed, long decompressedSize)
        {
            if (compressed)
            {
                CompressedChunkData = chunkData;
                try
                {
                    ChunkData = ZipUtility.Inflate(chunkData);
                }
                catch (Exception)
                {
                    // BUG: Smaller TPP mipmaps fail to load at them moment.
                    // This catch block allows unpacking of the textures, but should
                    // be removed once the unpacking issue has been resolved.
                    ChunkData = new byte[decompressedSize];
                }
            }
            else
            {
                CompressedChunkData = ZipUtility.Deflate(chunkData);
                ChunkData = chunkData;
            }
        }
    }
}
