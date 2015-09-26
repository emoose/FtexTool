using System;
using System.Collections.Generic;
using System.Linq;
using FtexTool.Dds;
using FtexTool.Dds.Enum;
using FtexTool.Ftex;
using FtexTool.Ftex.Enum;
using FtexTool.Ftexs;
using System.IO;

namespace FtexTool
{
    internal static class FtexDdsConverter
    {
        public static DdsFile ConvertToDds(FtexFile file, bool deswizzle)
        {
            DdsFile result = new DdsFile
            {
                Header = new DdsFileHeader
                {
                    Size = DdsFileHeader.DefaultHeaderSize,
                    Flags = DdsFileHeaderFlags.Texture | DdsFileHeaderFlags.MipMap,
                    Height = file.Height,
                    Width = file.Width,
                    Depth = file.Depth,
                    MipMapCount = file.MipMapCount,
                    Caps = DdsSurfaceFlags.Texture | DdsSurfaceFlags.MipMap
                }
            };

            result.Data = file.Data;

            switch (file.PixelFormatType)
            {
                case 0:
                    result.Header.PixelFormat = DdsPixelFormat.DdsPfA8R8G8B8();
                    result.Header.Flags = result.Header.Flags | DdsFileHeaderFlags.Volume;
                    if (deswizzle)
                        result.Data = ByteSwap16(Deswizzle(result.Data, result.Header.Width, result.Header.Height, result.Header.MipMapCount, "RGBA8"));

                    break;
                case 1:
                    result.Header.PixelFormat = DdsPixelFormat.DdsLuminance();
                    break;
                case 2:
                    result.Header.PixelFormat = DdsPixelFormat.DdsPfDxt1();
                    if (deswizzle)
                        result.Data = ByteSwap16(Deswizzle(result.Data, result.Header.Width, result.Header.Height, result.Header.MipMapCount, "DXT1"));

                    break;
                case 3:
                    result.Header.PixelFormat = DdsPixelFormat.DdsPfDxt3(); // HACK: This is just a guess. The value isn't used in the Ground Zeroes files. 
                    if (deswizzle)
                        result.Data = ByteSwap16(Deswizzle(result.Data, result.Header.Width, result.Header.Height, result.Header.MipMapCount, "DXT3"));

                    break;
                case 4:
                    result.Header.PixelFormat = DdsPixelFormat.DdsPfDxt5();
                    if (deswizzle)
                        result.Data = ByteSwap16(Deswizzle(result.Data, result.Header.Width, result.Header.Height, result.Header.MipMapCount, "DXT5"));

                    break;
                default:
                    throw new NotImplementedException(String.Format("Unknown PixelFormatType {0}", file.PixelFormatType));
            }

            return result;
        }

        public static byte[] ByteSwap16(byte[] raw)
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter writer = new BinaryWriter(ms);
            for (int i = 0; i < raw.Length; i += 2)
            {
                byte low = raw[i];
                byte high = raw[i + 1];
                ushort swapped = (ushort)((low << 8) | high);
                writer.Write(swapped);
            }
            return ms.ToArray();
        }

        public static byte[] Deswizzle(byte[] data, int width, int height, int numMipMaps, string pixelFormatName)
        {
            PixelFormatInfo info = new PixelFormatInfo();
            bool found = false;
            foreach (PixelFormatInfo i in PixelFormatInfos)
            {
                if (i.Name == pixelFormatName)
                {
                    found = true;
                    info = i;
                    break;
                }
            }

            if (!found)
                return data;

            int bytesPerBlock = info.BytesPerBlock;
            int curAddr = 0;
            for (int i = 0; i < numMipMaps; i++)
            {
                int width1 = Align(width, info.X360AlignX);
                int height1 = Align(height, info.X360AlignY);

                int size = (width1 / info.BlockSizeX) * (height1 / info.BlockSizeY) * bytesPerBlock;

                byte[] mipMapData = new byte[size];
                Array.Copy(data, curAddr, mipMapData, 0, size);
                mipMapData = UntileCompressedX360Texture(mipMapData, width1, width, height1, info.BlockSizeX, info.BlockSizeY, info.BytesPerBlock);
                Array.Copy(mipMapData, 0, data, curAddr, size);

                curAddr += size;
                width /= 2;
                height /= 2;
            }

            return data;
        }

        public static byte[] UntileX360Texture(byte[] data, int tiledWidth, int originalWidth, int height, int blockSizeX, int blockSizeY, int bytesPerBlock)
        {
            MemoryStream ms = new MemoryStream(data);
            MemoryStream output = new MemoryStream(data.Length);
            output.SetLength(data.Length);
            BinaryReader reader = new BinaryReader(ms);
            BinaryWriter writer = new BinaryWriter(output);

            int blockWidth = tiledWidth / blockSizeX;
            int originalBlockWidth = originalWidth / blockSizeX;
            int blockHeight = height / blockSizeY;
            int logBpp = appLog2(bytesPerBlock);

            for (int y = 0; y < blockHeight; y++)
            {
                for (int x = 0; x < originalBlockWidth; x++)
                {
                    int addr = GetTiledOffset(x, y, blockWidth, logBpp);

                    int sy = addr / blockWidth;
                    int sx = addr % blockWidth;

                    int y2 = y * blockSizeY;
                    int y3 = sy * blockSizeY;
                    for (int y1 = 0; y1 < blockSizeY; y1++, y2++, y3++)
                    {
                        // copy line of blockSizeX pixels
                        int x2 = x * blockSizeX;
                        int x3 = sx * blockSizeX;
                        int dstAddr = y2 * originalWidth + x2;
                        int srcAddr = y3 * tiledWidth + x3;
                        reader.BaseStream.Position = srcAddr;
                        byte[] data1 = reader.ReadBytes(blockSizeX);
                        writer.BaseStream.Position = dstAddr;
                        writer.Write(data1);
                    }
                }
            }
            return output.ToArray();
        }
        public static byte[] UntileCompressedX360Texture(byte[] data, int tiledWidth, int originalWidth, int height, int blockSizeX, int blockSizeY, int bytesPerBlock)
        {
            MemoryStream ms = new MemoryStream(data);
            MemoryStream output = new MemoryStream(data.Length);
            output.SetLength(data.Length);
            BinaryReader reader = new BinaryReader(ms);
            BinaryWriter writer = new BinaryWriter(output);

            int blockWidth = tiledWidth / blockSizeX;
            int originalBlockWidth = originalWidth / blockSizeX;
            int blockHeight = height / blockSizeY;
            int logBpp = appLog2(bytesPerBlock);

            for (int y = 0; y < blockHeight; y++)
            {
                for (int x = 0; x < originalBlockWidth; x++)
                {
                    int addr = GetTiledOffset(x, y, blockWidth, logBpp);

                    int sy = addr / blockWidth;
                    int sx = addr % blockWidth;

                    int dstAddr = (y * originalBlockWidth + x) * bytesPerBlock;
                    int srcAddr = (sy * blockWidth + sx) * bytesPerBlock;

                    reader.BaseStream.Position = srcAddr;
                    byte[] data1 = reader.ReadBytes(bytesPerBlock);
                    writer.BaseStream.Position = dstAddr;
                    writer.Write(data1);
                }
            }
            return output.ToArray();
        }


        static uint Align(uint ptr, uint alignment)
        {
            return ((ptr + alignment - 1) & ~(alignment - 1));
        }
        static int Align(int ptr, int alignment)
        {
            return ((ptr + alignment - 1) & ~(alignment - 1));
        }


        static int appLog2(int n)
        {
            int r;
            int n2 = n;
            for (r = -1; n2 != 0; n2 >>= 1, r++)
            { /*empty*/ }
            return r;
        }

        // Input:
        //		x/y		coordinate of block
        //		width	width of image in blocks
        //		logBpb	log2(bytesPerBlock)
        // Reference:
        //		XGAddress2DTiledOffset() from XDK
        static int GetTiledOffset(int x, int y, int width, int logBpb)
        {
            int alignedWidth = Align(width, 32);
            // top bits of coordinates
            int macro = ((x >> 5) + (y >> 5) * (alignedWidth >> 5)) << (logBpb + 7);
            // lower bits of coordinates (result is 6-bit value)
            int micro = ((x & 7) + ((y & 0xE) << 2)) << logBpb;
            // mix micro/macro + add few remaining x/y bits
            int offset = macro + ((micro & ~0xF) << 1) + (micro & 0xF) + ((y & 1) << 4);
            // mix bits again
            return (((offset & ~0x1FF) << 3) +					// upper bits (offset bits [*-9])
                    ((y & 16) << 7) +							// next 1 bit
                    ((offset & 0x1C0) << 2) +					// next 3 bits (offset bits [8-6])
                    (((((y & 8) >> 2) + (x >> 3)) & 3) << 6) +	// next 2 bits
                    (offset & 0x3F)								// lower 6 bits (offset bits [5-0])
                    ) >> logBpb;
        }




        struct PixelFormatInfo
        {
            public int BlockSizeX;
            public int BlockSizeY;
            public int BytesPerBlock;
            public int X360AlignX;
            public int X360AlignY;
            public string Name;

            public PixelFormatInfo(string name, int blockSizeX, int blockSizeY, int bytesPerBlock, int x360AlignX, int x360AlignY)
            {
                Name = name;
                BlockSizeX = blockSizeX;
                BlockSizeY = blockSizeY;
                BytesPerBlock = bytesPerBlock;
                X360AlignX = x360AlignX;
                X360AlignY = x360AlignY;
            }
        }

        static PixelFormatInfo[] PixelFormatInfos = {
                                                             new PixelFormatInfo("P8", 1, 1, 1, 0, 0),
                                                             new PixelFormatInfo("G8", 1, 1, 1, 64, 64),
                                                             new PixelFormatInfo("RGB8", 1, 1, 3, 0, 0),
                                                             new PixelFormatInfo("RGBA8", 1, 1, 4, 32, 32),
                                                             new PixelFormatInfo("BGRA8", 1, 1, 4, 32, 32),
                                                             new PixelFormatInfo("DXT1", 4, 4, 8, 128, 128),
                                                             new PixelFormatInfo("DXT3", 4, 4, 16, 128, 128),
                                                             new PixelFormatInfo("DXT5", 4, 4, 16, 128, 128),
                                                             new PixelFormatInfo("DXT5", 4, 4, 16, 128, 128),
                                                             new PixelFormatInfo("V8U8", 1, 1, 2, 64, 32),
                                                             new PixelFormatInfo("BC5", 4, 4, 16, 0, 0),
                                                             new PixelFormatInfo("BC7", 4, 4, 16, 0, 0),
                                                             new PixelFormatInfo("A1", 8, 1, 1, 0, 0),
                                                             new PixelFormatInfo("RGBA4", 1, 1, 2, 0, 0)
                                                         };

        public static byte[] Deswizzle(byte[] raw, int width, int height)
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter ew = new BinaryWriter(ms);
            ew.BaseStream.Position = 0;

            int realsize = width * height * 4;
            int lines = realsize / 16;

            int templine = 0;
            int rowcount = 0;
            int offsetby = 0;
            int i = 0;
            int sections = width / 32;
            for (int x = 0; x < lines; x++)
            {


                if (rowcount % 32 == 0 && rowcount != 0)
                {

                    templine = (i + 1) * ((width * 32) / 4);

                    i++;

                }



                if (rowcount % 8 == 0 && rowcount != 0)
                {
                    if (offsetby == 0)
                    {
                        offsetby = 8 * 16;
                    }
                    else
                    {
                        offsetby = 0;
                    }
                }


                if (rowcount == height) break;

                ///get row 1 - 224 pixels(one row) = 7 * 32   
                for (int y = 0; y < sections; y++)
                {

                    //get 32 pixels
                    for (int z = 0; z < 8; z++)
                    {
                        if (z < 4)
                        {
                            int offset = (templine * 16) + ((y * 256) * 16) + ((z * 2) * 16) + offsetby;
                            int oi1 = BitConverter.ToInt32(raw, offset + 0);
                            int oi2 = BitConverter.ToInt32(raw, offset + 4);
                            int oi3 = BitConverter.ToInt32(raw, offset + 8);
                            int oi4 = BitConverter.ToInt32(raw, offset + 12);

                            ew.Write(oi1);
                            ew.Write(oi2);
                            ew.Write(oi3);
                            ew.Write(oi4);
                        }
                        else
                        {
                            int offset = (templine * 16) + ((y * 256) * 16) + ((z * 2) * 16) - offsetby;
                            int oi1 = BitConverter.ToInt32(raw, offset + 0);
                            int oi2 = BitConverter.ToInt32(raw, offset + 4);
                            int oi3 = BitConverter.ToInt32(raw, offset + 8);
                            int oi4 = BitConverter.ToInt32(raw, offset + 12);

                            ew.Write(oi1);
                            ew.Write(oi2);
                            ew.Write(oi3);
                            ew.Write(oi4);
                        }
                    }

                }
                rowcount++;

                if (rowcount == height) break;
                ///get row 2
                for (int y = 0; y < sections; y++)
                {

                    //get 32 pixels
                    for (int z = 0; z < 8; z++)
                    {
                        if (offsetby != 0)
                        {
                        }
                        if (z < 4)
                        {
                            int offset = (templine * 16) + 16 + ((y * 256) * 16) + ((z * 2) * 16) + offsetby;
                            int oi1 = BitConverter.ToInt32(raw, offset + 0);
                            int oi2 = BitConverter.ToInt32(raw, offset + 4);
                            int oi3 = BitConverter.ToInt32(raw, offset + 8);
                            int oi4 = BitConverter.ToInt32(raw, offset + 12);
                            ew.Write(oi1);
                            ew.Write(oi2);
                            ew.Write(oi3);
                            ew.Write(oi4);
                        }
                        else
                        {
                            int offset = (templine * 16) + 16 + ((y * 256) * 16) + ((z * 2) * 16) - offsetby;
                            int oi1 = BitConverter.ToInt32(raw, offset + 0);
                            int oi2 = BitConverter.ToInt32(raw, offset + 4);
                            int oi3 = BitConverter.ToInt32(raw, offset + 8);
                            int oi4 = BitConverter.ToInt32(raw, offset + 12);
                            ew.Write(oi1);
                            ew.Write(oi2);
                            ew.Write(oi3);
                            ew.Write(oi4);
                        }
                    }

                }
                rowcount++;
                templine += 16;
            }
            return ms.ToArray();
        }

        public static FtexFile ConvertToFtex(DdsFile file, FtexTextureType textureType)
        {
            FtexFile result = new FtexFile();
            if (file.Header.PixelFormat.Equals(DdsPixelFormat.DdsPfA8R8G8B8()))
                result.PixelFormatType = 0;
            else if (file.Header.PixelFormat.Equals(DdsPixelFormat.DdsLuminance()))
                result.PixelFormatType = 1;
            else if (file.Header.PixelFormat.Equals(DdsPixelFormat.DdsPfDxt1()))
                result.PixelFormatType = 2;
            else if (file.Header.PixelFormat.Equals(DdsPixelFormat.DdsPfDxt3()))
                result.PixelFormatType = 3; // HACK: This is just a guess. The value isn't used in the Ground Zeroes files. 
            else if (file.Header.PixelFormat.Equals(DdsPixelFormat.DdsPfDxt5()))
                result.PixelFormatType = 4;
            else
                throw new NotImplementedException(String.Format("Unknown PixelFormatType {0}", file.Header.PixelFormat));

            result.Height = Convert.ToInt16(file.Header.Height);
            result.Width = Convert.ToInt16(file.Header.Width);
            result.Depth = Convert.ToInt16(file.Header.Depth);

            var mipMapData = GetMipMapData(file);
            var mipMaps = GetMipMapInfos(mipMapData);
            var ftexsFiles = GetFtexsFiles(mipMaps, mipMapData);
            result.MipMapCount = Convert.ToByte(mipMaps.Count());
            result.NrtFlag = 2;
            result.AddMipMapInfos(mipMaps);
            result.AddFtexsFiles(ftexsFiles);
            result.FtexsFileCount = Convert.ToByte(ftexsFiles.Count());
            result.AdditionalFtexsFileCount = Convert.ToByte(ftexsFiles.Count() - 1);
            result.TextureType = textureType;

            // TODO: Handle the DDS depth flag.
            return result;
        }

        private static List<FtexsFile> GetFtexsFiles(List<FtexFileMipMapInfo> mipMapInfos, List<byte[]> mipMapDatas)
        {
            Dictionary<byte, FtexsFile> ftexsFiles = new Dictionary<byte, FtexsFile>();

            foreach (var mipMapInfo in mipMapInfos)
            {
                if (ftexsFiles.ContainsKey(mipMapInfo.FtexsFileNumber) == false)
                {
                    FtexsFile ftexsFile = new FtexsFile
                    {
                        FileNumber = mipMapInfo.FtexsFileNumber
                    };
                    ftexsFiles.Add(mipMapInfo.FtexsFileNumber, ftexsFile);
                }
            }

            for (int i = 0; i < mipMapInfos.Count; i++)
            {
                FtexFileMipMapInfo mipMapInfo = mipMapInfos[i];
                FtexsFile ftexsFile = ftexsFiles[mipMapInfo.FtexsFileNumber];
                byte[] mipMapData = mipMapDatas[i];
                FtexsFileMipMap ftexsFileMipMap = new FtexsFileMipMap();
                List<FtexsFileChunk> chunks = GetFtexsChunks(mipMapInfo, mipMapData);
                ftexsFileMipMap.AddChunks(chunks);
                ftexsFile.AddMipMap(ftexsFileMipMap);
            }
            return ftexsFiles.Values.ToList();
        }

        private static List<FtexsFileChunk> GetFtexsChunks(FtexFileMipMapInfo mipMapInfo, byte[] mipMapData)
        {
            List<FtexsFileChunk> ftexsFileChunks = new List<FtexsFileChunk>();
            const int maxChunkSize = short.MaxValue;
            int requiredChunks = (int)Math.Ceiling((double)mipMapData.Length / maxChunkSize);
            int mipMapDataOffset = 0;
            for (int i = 0; i < requiredChunks; i++)
            {
                FtexsFileChunk chunk = new FtexsFileChunk();
                int chunkSize = Math.Min(mipMapData.Length - mipMapDataOffset, maxChunkSize);
                byte[] chunkData = new byte[chunkSize];
                Array.Copy(mipMapData, mipMapDataOffset, chunkData, 0, chunkSize);
                chunk.SetData(chunkData, false, chunkSize);
                ftexsFileChunks.Add(chunk);
                mipMapDataOffset += chunkSize;
            }
            return ftexsFileChunks;
        }

        private static List<FtexFileMipMapInfo> GetMipMapInfos(List<byte[]> levelData)
        {
            List<FtexFileMipMapInfo> mipMapsInfos = new List<FtexFileMipMapInfo>();
            for (int i = 0; i < levelData.Count; i++)
            {
                FtexFileMipMapInfo mipMapInfo = new FtexFileMipMapInfo();
                int fileSize = levelData[i].Length;
                mipMapInfo.DecompressedFileSize = fileSize;
                mipMapInfo.Index = Convert.ToByte(i);
                mipMapsInfos.Add(mipMapInfo);
            }

            SetMipMapFileNumber(mipMapsInfos);
            return mipMapsInfos;
        }

        private static void SetMipMapFileNumber(ICollection<FtexFileMipMapInfo> mipMapsInfos)
        {
            if (mipMapsInfos.Count == 1)
            {
                mipMapsInfos.Single().FtexsFileNumber = 1;
            }
            else
            {
                int fileSize = 0;
                foreach (var mipMapInfo in mipMapsInfos.OrderBy(m => m.DecompressedFileSize))
                {
                    fileSize += mipMapInfo.DecompressedFileSize;
                    mipMapInfo.FtexsFileNumber = GetFtexsFileNumber(fileSize);
                }
            }
        }

        private static byte GetFtexsFileNumber(int fileSize)
        {
            // TODO: Find the correct algorithm.
            if (fileSize <= 21872)
                return 1;
            if (fileSize <= 87408)
                return 2;
            if (fileSize <= 349552)
                return 3;
            if (fileSize <= 1398128)
                return 4;
            return 5;
        }

        private static List<byte[]> GetMipMapData(DdsFile file)
        {
            const int minimumWidth = 4;
            const int minimumHeight = 4;
            List<byte[]> mipMapDatas = new List<byte[]>();
            byte[] data = file.Data;
            int dataOffset = 0;
            var width = file.Header.Width;
            var height = file.Header.Height;
            int mipMapsCount = file.Header.Flags.HasFlag(DdsFileHeaderFlags.MipMap) ? file.Header.MipMapCount : 1;
            for (int i = 0; i < mipMapsCount; i++)
            {
                int size = DdsPixelFormat.CalculateImageSize(file.Header.PixelFormat, width, height);
                var buffer = new byte[size];
                Array.Copy(data, dataOffset, buffer, 0, size);
                mipMapDatas.Add(buffer);
                dataOffset += size;
                width = Math.Max(width / 2, minimumWidth);
                height = Math.Max(height / 2, minimumHeight);
            }
            return mipMapDatas;
        }
    }
}
