using System;
using System.Text;
using System.Linq;
using System.Security.Cryptography;

namespace AssetsTools.NET
{
    public class AssetBundleUnityCN
    {
        private const string Signature = "#$unity3dchina!@";

        private ICryptoTransform Encryptor;
        
        uint value;

        private byte[] InfoBytes;
        private byte[] InfoKey;

        private byte[] SignatureBytes;
        private byte[] SignatureKey;

        public byte[] Index = new byte[0x10];
        public byte[] Sub = new byte[0x10];

        public AssetBundleUnityCN(AssetsFileReader reader)
        {
            value = reader.ReadUInt32();

            InfoBytes = reader.ReadBytes(0x10);
            InfoKey = reader.ReadBytes(0x10);
            reader.Position += 1;

            SignatureBytes = reader.ReadBytes(0x10);
            SignatureKey = reader.ReadBytes(0x10);
            reader.Position += 1;
        }

        private void Init()
        {
            DecryptKey(SignatureKey, SignatureBytes);

            var str = Encoding.UTF8.GetString(SignatureBytes);
            if (str != Signature)
            {
                throw new Exception($"Invalid Signature, Expected {Signature} but found {str} instead");
            }
            
            DecryptKey(InfoKey, InfoBytes);

            InfoBytes = ToUInt4Array(InfoBytes);
            Array.Copy(InfoBytes, 0, Index, 0, 0x10);
            var subBytes = new byte[0x10];
            Array.Copy(InfoBytes, 0x10, subBytes, 0, 0x10);
            for (var i = 0; i < subBytes.Length; i++)
            {
                var idx = (i % 4 * 4) + (i / 4);
                Sub[idx] = subBytes[i];
            }
        }
        
        private static byte[] ToUInt4Array(byte[] source) => ToUInt4Array(source, 0, source.Length);
        
        private static byte[] ToUInt4Array(byte[] source, int offset, int size)
        {
            var buffer = new byte[size * 2];
            for (var i = 0; i < size; i++)
            {
                var idx = i * 2;
                buffer[idx] = (byte)(source[offset + i] >> 4);
                buffer[idx + 1] = (byte)(source[offset + i] & 0xF);
            }
            return buffer;
        }

        public bool SetKey(string key)
        {
            try
            {
                using var aes = Aes.Create();
                aes.Mode = CipherMode.ECB;
                aes.Key = FromHexString(key);

                Encryptor = aes.CreateEncryptor();
            }
            catch (Exception e)
            {
                Console.WriteLine($"[UnityCN] Invalid key !!\n{e.Message}");
                return false;
            }
            Init();
            return true;
        }
        
        private static byte[] FromHexString(string hex)
        {
            var bytes = new byte[hex.Length / 2];
            for (var i = 0; i < bytes.Length; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
        }

        public void DecryptBlock(byte[] bytes, int size, int index)
        {
            var offset = 0;
            while (offset < size)
            {
                Decrypt(bytes, ref offset, index++, size);
            }
        }

        private void DecryptKey(byte[] key, byte[] data)
        {
            if (Encryptor != null)
            {
                key = Encryptor.TransformFinalBlock(key, 0, key.Length);
                for (int i = 0; i < 0x10; i++)
                    data[i] ^= key[i];
            }
        }

        private int DecryptByte(byte[] bytes, ref int offset, ref int index)
        {
            var b = Sub[((index >> 2) & 3) + 4] + Sub[index & 3] + Sub[((index >> 4) & 3) + 8] + Sub[((byte)index >> 6) + 12];
            bytes[offset] = (byte)((Index[bytes[offset] & 0xF] - b) & 0xF | 0x10 * (Index[bytes[offset] >> 4] - b));
            b = bytes[offset];
            offset++;
            index++;
            return b;
        }
        private void Decrypt(byte[] bytes, ref int offset, int index, int size)
        {
            var curByte = DecryptByte(bytes, ref offset, ref index);
            var byteHigh = curByte >> 4;
            var byteLow = curByte & 0xF;

            if (byteHigh == 0xF)
            {
                int b;
                do
                {
                    b = DecryptByte(bytes, ref offset, ref index);
                    byteHigh += b;
                } while (b == 0xFF);
            }

            offset += byteHigh;

            if (offset < size)
            {
                DecryptByte(bytes, ref offset, ref index);
                DecryptByte(bytes, ref offset, ref index);
                if (byteLow == 0xF)
                {
                    int b;
                    do
                    {
                        b = DecryptByte(bytes, ref offset, ref index);
                    } while (b == 0xFF);
                }
            }
        }
    }
}