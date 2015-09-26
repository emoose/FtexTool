using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FtexTool.Ftex.Enum;
using FtexTool.Ftexs;

namespace FtexTool.Ftex
{
    public class FtexFile
    {
        private const int MagicNumber1 = 0x58455446; // FTEX 85 EB 01 40
        private const int MagicNumber2 = 0x4001EB85;
        private const int MagicNumber3 = 0x01000001;
        private const int OneInt32 = 1;
        private const int ZeroInt32 = 0;
        private const byte OneByte = 1;
        private const byte ZeroByte = 0;
        private readonly Dictionary<int, FtexsFile> _ftexsFiles;
        private readonly List<FtexFileMipMapInfo> _mipMapInfos;

        public FtexFile()
        {
            _ftexsFiles = new Dictionary<int, FtexsFile>();
            _mipMapInfos = new List<FtexFileMipMapInfo>();
            Hash = new byte[16];
        }

        public short PixelFormatType { get; set; }
        public short Width { get; set; }
        public short Height { get; set; }
        public short Depth { get; set; }
        public byte MipMapCount { get; set; }
        // Flags 
        // 0x0 when file ends with _nrt 
        // 0x2 else
        public byte NrtFlag { get; set; }
        // Flags
        // 0 or 17
        public short UnknownFlags { get; set; }
        public FtexTextureType TextureType { get; set; }
        public byte FtexsFileCount { get; set; }
        public byte AdditionalFtexsFileCount { get; set; }
        public byte[] Hash { get; set; }

        public bool FlipEndian { get; set; }
        public IEnumerable<FtexFileMipMapInfo> MipMapInfos
        {
            get { return _mipMapInfos; }
        }

        public IEnumerable<FtexsFile> FtexsFiles
        {
            get { return _ftexsFiles.Values; }
        }

        public byte[] Data
        {
            get
            {
                MemoryStream stream = new MemoryStream();
                foreach (var ftexsFile in FtexsFiles.Reverse())
                {
                    byte[] ftexsFileData = ftexsFile.Data;
                    stream.Write(ftexsFileData, 0, ftexsFileData.Length);
                }
                return stream.ToArray();
            }
        }

        public void AddFtexsFile(FtexsFile ftexsFile)
        {
            _ftexsFiles.Add(ftexsFile.FileNumber, ftexsFile);
        }

        public void AddFtexsFiles(IEnumerable<FtexsFile> ftexsFiles)
        {
            foreach (var ftexsFile in ftexsFiles)
            {
                AddFtexsFile(ftexsFile);
            }
        }

        public bool TryGetFtexsFile(int fileNumber, out FtexsFile ftexsFile)
        {
            return _ftexsFiles.TryGetValue(fileNumber, out ftexsFile);
        }

        public static FtexFile ReadFtexFile(Stream inputStream)
        {
            FtexFile result = new FtexFile();
            result.Read(inputStream);
            return result;
        }

        private void Read(Stream inputStream)
        {
            X360Reader reader = new X360Reader(inputStream, Encoding.Default, true, false);
            uint magicNumber = reader.ReadUInt32();
            FlipEndian = magicNumber == 0x46544558;
            reader.BaseStream.Position -= 4;
            reader.FlipEndian = FlipEndian;

            magicNumber = reader.ReadUInt32();
            if (magicNumber != MagicNumber1)
                return;

            magicNumber = reader.ReadUInt32();
            if (magicNumber != MagicNumber2)
                return;

            PixelFormatType = reader.ReadInt16();
            Width = reader.ReadInt16();
            Height = reader.ReadInt16();
            Depth = reader.ReadInt16();
            MipMapCount = reader.ReadByte();
            NrtFlag = reader.ReadByte();
            UnknownFlags = reader.ReadInt16();

            reader.FlipEndian = false;

            if (reader.ReadInt32() != OneInt32)
                return;
            if (reader.ReadInt32() != ZeroInt32)
                return;

            TextureType = (FtexTextureType) reader.ReadInt32();
            FtexsFileCount = reader.ReadByte();
            AdditionalFtexsFileCount = reader.ReadByte();
            if (reader.ReadInt16() != 0)
                return;

            if (reader.ReadInt32() != ZeroInt32)
                return;
            if (reader.ReadInt32() != ZeroInt32)
                return;
            if (reader.ReadInt32() != ZeroInt32)
                return;

            Hash = reader.ReadBytes(16);

            reader.FlipEndian = FlipEndian;
            for (int i = 0; i < MipMapCount; i++)
            {
                FtexFileMipMapInfo fileMipMapInfo = FtexFileMipMapInfo.ReadFtexFileMipMapInfo(reader);
                AddMipMapInfo(fileMipMapInfo);
            }
        }

        private void AddMipMapInfo(FtexFileMipMapInfo fileMipMapInfo)
        {
            _mipMapInfos.Add(fileMipMapInfo);
        }

        public void AddMipMapInfos(IEnumerable<FtexFileMipMapInfo> mipMapInfos)
        {
            foreach (var mipMapInfo in mipMapInfos)
            {
                AddMipMapInfo(mipMapInfo);
            }
        }

        public void Write(Stream outputStream, bool flipEndian = false)
        {
            X360Writer writer = new X360Writer(outputStream, Encoding.Default, true, flipEndian);
            writer.Write(MagicNumber1);
            writer.Write(MagicNumber2);
            writer.Write(PixelFormatType);
            writer.Write(Width);
            writer.Write(Height);
            writer.Write(Depth);
            writer.Write(MipMapCount);
            writer.Write(NrtFlag);
            writer.Write(UnknownFlags);
            writer.FlipEndian = false;
            writer.Write(OneInt32);
            writer.Write(ZeroInt32);
            writer.Write(Convert.ToInt32(TextureType));
            writer.Write(FtexsFileCount);
            writer.Write(AdditionalFtexsFileCount);
            writer.Write(ZeroByte);
            writer.Write(ZeroByte);
            writer.Write(ZeroInt32);
            writer.Write(ZeroInt32);
            writer.Write(ZeroInt32);
            writer.Write(Hash);
            writer.FlipEndian = flipEndian;
            foreach (var mipMap in MipMapInfos)
            {
                mipMap.Write(writer);
            }
        }

        public void UpdateOffsets()
        {
            int mipMapIndex = 0;
            foreach (var ftexsFile in FtexsFiles)
            {
                foreach (var ftexsFileMipMap in ftexsFile.MipMaps)
                {
                    FtexFileMipMapInfo ftexMipMapInfo = MipMapInfos.ElementAt(mipMapIndex);
                    ftexMipMapInfo.CompressedFileSize = ftexsFileMipMap.CompressedDataSize;
                    ftexMipMapInfo.ChunkCount = Convert.ToByte(ftexsFileMipMap.Chunks.Count());
                    ftexMipMapInfo.Offset = ftexsFileMipMap.Offset;
                    ++mipMapIndex;
                }
            }
        }
    }
}
