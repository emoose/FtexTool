using System.IO;
using System.Text;

namespace FtexTool.Ftex
{
    public class FtexFileMipMapInfo
    {
        public int Offset { get; set; }
        public int DecompressedFileSize { get; set; }
        public int CompressedFileSize { get; set; }
        public byte Index { get; set; }
        public byte FtexsFileNumber { get; set; }
        public short ChunkCount { get; set; }

        public static FtexFileMipMapInfo ReadFtexFileMipMapInfo(X360Reader reader)
        {
            FtexFileMipMapInfo result = new FtexFileMipMapInfo();
            result.Read(reader);
            return result;
        }

        public void Read(X360Reader reader)
        {
            Offset = reader.ReadInt32();
            DecompressedFileSize = reader.ReadInt32();
            CompressedFileSize = reader.ReadInt32();
            Index = reader.ReadByte();
            FtexsFileNumber = reader.ReadByte();
            ChunkCount = reader.ReadInt16();
        }

        public void Write(X360Writer writer)
        {
            writer.Write(Offset);
            writer.Write(DecompressedFileSize);
            writer.Write(CompressedFileSize);
            writer.Write(Index);
            writer.Write(FtexsFileNumber);
            writer.Write(ChunkCount);
        }
    }
}
