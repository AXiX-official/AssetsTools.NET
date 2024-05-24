using System;
using System.Text.RegularExpressions;
using System.Linq;

namespace AssetsTools.NET
{
    public class AssetBundleHeader
    {
        /// <summary>
        /// Magic appearing at the beginning of all bundles. Possible options are:
        /// UnityFS, UnityWeb, UnityRaw, UnityArchive
        /// </summary>
        public string Signature { get; set; }
        /// <summary>
        /// Version of this file.
        /// </summary>
        public uint Version { get; set; }
        /// <summary>
        /// Generation version string. For Unity 5 bundles this is always "5.x.x"
        /// </summary>
        public string GenerationVersion { get; set; }
        /// <summary>
        /// Engine version. This is the specific version string being used. For example, "2019.4.2f1"
        /// </summary>
        public string EngineVersion { get; set; }
        /// <summary>
        /// Parsed engine version.For example, "2019.4.2f1" would be [2019, 4, 2, 1]
        /// </summary>
        public int[] PrasedEngineVersion => ParseVersion();
        /// <summary>
        /// Weather align after header. For version 2019.4.x some of them has Version 6 but with align after header.
        /// </summary>
        public bool NeedAlignAfterHeader { get; set; }

        /// <summary>
        /// Header for bundles with a UnityFS Signature.
        /// </summary>
        public AssetBundleFSHeader FileStreamHeader { get; set; }
        
        public bool DataIsEncrypted { get; private set; }
        
        public AssetBundleFSHeaderFlags Mask { get; private set; }
        
        private bool HasBlockInfoNeedPaddingAtStart { get; set; }

        public void Read(AssetsFileReader reader)
        {
            reader.BigEndian = true;
            Signature = reader.ReadNullTerminated();
            Version = reader.ReadUInt32();
            GenerationVersion = reader.ReadNullTerminated();
            EngineVersion = reader.ReadNullTerminated();
            if (Signature == "UnityFS")
            {
                FileStreamHeader = new AssetBundleFSHeader();
                FileStreamHeader.Read(reader);

                if (Version >= 7)
                {
                    NeedAlignAfterHeader = true;
                }
                else if (PrasedEngineVersion[0] == 2019 && PrasedEngineVersion[1] == 4)// && FileStreamHeader.Flags != AssetBundleFSHeaderFlags.HasDirectoryInfo)
                {
                    long p = reader.Position;
                    long len = 16 - p % 16;
                    byte[] bytes = reader.ReadBytes((int)len);
                    NeedAlignAfterHeader = bytes.All(x => x == 0);
                    reader.Position = p;
                }
                
                if (PrasedEngineVersion[0] < 2020 || //2020 and earlier
                    (PrasedEngineVersion[0] == 2020 && PrasedEngineVersion[1] == 3 && PrasedEngineVersion[2] <= 34) || //2020.3.34 and earlier
                    (PrasedEngineVersion[0] == 2021 && PrasedEngineVersion[1] == 3 && PrasedEngineVersion[2] <= 2) || //2021.3.2 and earlier
                    (PrasedEngineVersion[0] == 2022 && PrasedEngineVersion[1] == 3 && PrasedEngineVersion[2] <= 1)) //2022.3.1 and earlier
                {
                    Mask = AssetBundleFSHeaderFlags.BlockInfoNeedPaddingAtStart;
                    HasBlockInfoNeedPaddingAtStart = false;
                }
                else
                {
                    Mask = AssetBundleFSHeaderFlags.UnityCNEncryption;
                    HasBlockInfoNeedPaddingAtStart = true;
                }

                if ((FileStreamHeader.Flags & Mask) != 0)
                {
                    DataIsEncrypted = true;
                }
            }
            else
            {
                throw new NotSupportedException($"{Signature} signature not supported!");
            }
        }

        public void Write(AssetsFileWriter writer)
        {
            writer.BigEndian = true;
            writer.WriteNullTerminated(Signature);
            writer.Write(Version);
            writer.WriteNullTerminated(GenerationVersion);
            writer.WriteNullTerminated(EngineVersion);
            if (Signature == "UnityFS")
            {
                FileStreamHeader.Write(writer);
            }
            else
            {
                throw new NotSupportedException($"{Signature} signature not supported!");
            }
        }
        
        public long GetBundleInfoOffset()
        {
            if (Signature != "UnityFS")
                throw new NotSupportedException($"{Signature} signature not supported!");

            AssetBundleFSHeaderFlags flags = FileStreamHeader.Flags;
            long totalFileSize = FileStreamHeader.TotalFileSize;
            long compressedSize = FileStreamHeader.CompressedSize;

            if ((flags & AssetBundleFSHeaderFlags.BlockAndDirAtEnd) != 0)
            {
                if (totalFileSize == 0)
                    return -1;
                return totalFileSize - compressedSize;
            }
            else
            {
                long ret = GenerationVersion.Length + EngineVersion.Length + 0x1a;
                if (NeedAlignAfterHeader)
                {
                    if ((flags & AssetBundleFSHeaderFlags.OldWebPluginCompatibility) != 0)
                        return ((ret + 0x0a) + 15) & ~15;
                    else if (DataIsEncrypted)
                        return ((ret + Signature.Length + 1 + 0x46) + 15) & ~15;
                    else
                        return ((ret + Signature.Length + 1) + 15) & ~15;
                }
                else
                {
                    if ((flags & AssetBundleFSHeaderFlags.OldWebPluginCompatibility) != 0)
                        return ret + 0x0a;
                    else if (DataIsEncrypted)
                        return ret + Signature.Length + 1 + 0x46;
                    else
                        return ret + Signature.Length + 1;
                }
            }
        }

        public long GetFileDataOffset()
        {
            if (Signature != "UnityFS")
                throw new NotSupportedException($"{Signature} signature not supported!");

            AssetBundleFSHeaderFlags flags = FileStreamHeader.Flags;
            long compressedSize = FileStreamHeader.CompressedSize;

            long ret = GenerationVersion.Length + EngineVersion.Length + 0x1a;
            if ((flags & AssetBundleFSHeaderFlags.OldWebPluginCompatibility) != 0)
                ret += 0x0a;
            else
                ret += Signature.Length + 1;
            
            if (DataIsEncrypted)
                ret += 0x46;
            if (NeedAlignAfterHeader)
                ret = (ret + 15) & ~15;
            if ((flags & AssetBundleFSHeaderFlags.BlockAndDirAtEnd) == 0)
                ret += compressedSize;
            if (HasBlockInfoNeedPaddingAtStart && (flags & AssetBundleFSHeaderFlags.BlockInfoNeedPaddingAtStart) != 0)
                ret = (ret + 15) & ~15;

            return ret;
        }

        // todo: enum
        public byte GetCompressionType()
        {
            if (Signature != "UnityFS")
                throw new NotSupportedException($"{Signature} signature not supported!");

            return (byte)(FileStreamHeader.Flags & AssetBundleFSHeaderFlags.CompressionMask);
        }
        
        private int[] ParseVersion()
        {
            var versionSplit = Regex.Replace(EngineVersion, @"\D", ".").Split(new[] { "." }, StringSplitOptions.RemoveEmptyEntries);
            return versionSplit.Select(int.Parse).ToArray();
        }
    }
}
