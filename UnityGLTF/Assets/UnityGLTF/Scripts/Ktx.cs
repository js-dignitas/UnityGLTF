using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityGLTF
{

    enum OpenGlInternalFormat
    {
        COMPRESSED_RGB8_ETC2 = 0x9274,
        COMPRESSED_SRGB8_ETC2 = 0x9275,
        COMPRESSED_RGB8_PUNCHTHROUGH_ALPHA1_ETC2 = 0x9276,
        COMPRESSED_SRGB8_PUNCHTHROUGH_ALPHA1_ETC2 = 0x9277,
        COMPRESSED_RGBA8_ETC2_EAC = 0x9278,
        COMPRESSED_SRGB8_ALPHA8_ETC2_EAC = 0x9279,
        COMPRESSED_R11_EAC = 0x9270,
        COMPRESSED_SIGNED_R11_EAC = 0x9271,
        COMPRESSED_RG11_EAC = 0x9272,
        COMPRESSED_SIGNED_RG11_EAC = 0x9273
    }
    struct Ktx
    {
        public System.UInt32 id0;
        public System.UInt32 id1;
        public System.UInt32 id2;
        public System.UInt32 endianness;
        public System.UInt32 glType; // eg UNSIGNED_BYTE
        public System.UInt32 glTypeSize; // should be 1
        public System.UInt32 glFormat; // eg RGB, RGBA
        public System.UInt32 glInternalFormat; // OpenGlInternalFormat
        public System.UInt32 glBaseInternalFormat;
        public System.UInt32 pixelWidth;
        public System.UInt32 pixelHeight;
        public System.UInt32 pixelDepth;
        public System.UInt32 numberOfArrayElements;
        public System.UInt32 numberOfFaces;
        public System.UInt32 numberOfMipmapLevels;
        public System.UInt32 bytesOfKeyValueData;
        public int dataOffset;
        public Ktx(byte[] bytes)
        {
            int i = 0;
            id0 = BitConverter.ToUInt32(bytes, i); i += 4;
            id1 = BitConverter.ToUInt32(bytes, i); i += 4;
            id2 = BitConverter.ToUInt32(bytes, i); i += 4;
            endianness = BitConverter.ToUInt32(bytes, i); i += 4;
            glType = BitConverter.ToUInt32(bytes, i); i += 4;
            glTypeSize = BitConverter.ToUInt32(bytes, i); i += 4;
            glFormat = BitConverter.ToUInt32(bytes, i); i += 4;
            glInternalFormat = BitConverter.ToUInt32(bytes, i); i += 4;
            glBaseInternalFormat = BitConverter.ToUInt32(bytes, i); i += 4;
            pixelWidth = BitConverter.ToUInt32(bytes, i); i += 4;
            pixelHeight = BitConverter.ToUInt32(bytes, i); i += 4;
            pixelDepth = BitConverter.ToUInt32(bytes, i); i += 4;
            numberOfArrayElements = BitConverter.ToUInt32(bytes, i); i += 4;
            numberOfFaces = BitConverter.ToUInt32(bytes, i); i += 4;
            numberOfMipmapLevels = BitConverter.ToUInt32(bytes, i); i += 4;
            bytesOfKeyValueData = BitConverter.ToUInt32(bytes, i); i += 4;

            int y = 0;
            for (y = 0; y < bytesOfKeyValueData;)
            {
                UInt32 size = BitConverter.ToUInt32(bytes, i); y += 4;

                // size of value
                y += (int)size;

                // size of padding
                y += (int)(3 - ((size + 3) % 4));
            }
            i += y;
            dataOffset = i;
        }

        public static bool IsKTX(byte[] bytes)
        {
            return
                bytes[0] == 0xAB &&
                bytes[1] == 0x4B &&
                bytes[2] == 0x54 &&
                bytes[3] == 0x58;
        }

        TextureFormat GetUnityTextureFormat()
        {
            OpenGlInternalFormat internalFormat = (OpenGlInternalFormat)glInternalFormat;
            switch (internalFormat)
            {
                case OpenGlInternalFormat.COMPRESSED_RGB8_ETC2:
                    return TextureFormat.ETC2_RGB;
                case OpenGlInternalFormat.COMPRESSED_RGBA8_ETC2_EAC:
                    return TextureFormat.ETC2_RGBA8;
                default:
                    return (TextureFormat)0;
            }
        }


        public static Texture2D LoadTextureKTX(byte[] bytes, bool isLinear, bool gpuOnly)
        {
            if (!IsKTX(bytes))
                throw new Exception("Invalid KTX texture. Unable to read");

            Ktx header = new Ktx(bytes);

            byte[] ktxBytes = new byte[bytes.Length - header.dataOffset];
            Buffer.BlockCopy(bytes, header.dataOffset, ktxBytes, 0, ktxBytes.Length);

            TextureFormat textureFormat = (TextureFormat)0;
            switch ((OpenGlInternalFormat)header.glInternalFormat)
            {
                case OpenGlInternalFormat.COMPRESSED_RGB8_ETC2:
                    textureFormat = TextureFormat.ETC2_RGB;
                    break;
                case OpenGlInternalFormat.COMPRESSED_RGBA8_ETC2_EAC:
                    textureFormat = TextureFormat.ETC2_RGBA8;
                    break;
                default:
                    break;
            }
            if (0 != (int)textureFormat)
            {
                Texture2D texture = new Texture2D((int)header.pixelWidth, (int)header.pixelHeight, textureFormat, false, isLinear);

                texture.LoadRawTextureData(ktxBytes);

                texture.Apply(false, gpuOnly);

                return texture;
            }
            return null;
        }
    }
}
