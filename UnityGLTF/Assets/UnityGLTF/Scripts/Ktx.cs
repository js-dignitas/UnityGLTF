using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityGLTF
{
    class Ktx
    {
        public static bool IsKTX(byte[] data)
        {
            return
                data[0] == 0xAB &&
                data[1] == 0x4B &&
                data[2] == 0x54 &&
                data[3] == 0x58;
        }

        /// <summary>
        /// Mapping the glInternalFormat to Unity TextureFormat
        /// </summary>
        static Dictionary<KtxSharp.GlInternalFormat, TextureFormat> textureFormatMapper = new Dictionary<KtxSharp.GlInternalFormat, TextureFormat>()
        {
            {KtxSharp.GlInternalFormat.GL_COMPRESSED_RGB_S3TC_DXT1_EXT,TextureFormat.DXT1}, // ???
            {KtxSharp.GlInternalFormat.GL_COMPRESSED_RGBA_S3TC_DXT1_EXT,TextureFormat.DXT1}, // ???
            {KtxSharp.GlInternalFormat.GL_COMPRESSED_RGBA_S3TC_DXT3_EXT,TextureFormat.DXT5}, // ???
            {KtxSharp.GlInternalFormat.GL_COMPRESSED_RGBA_S3TC_DXT5_EXT,TextureFormat.DXT5}, // ???
            {KtxSharp.GlInternalFormat.GL_ETC1_RGB8_OES,TextureFormat.ETC_RGB4}, // ???
            {KtxSharp.GlInternalFormat.GL_COMPRESSED_R11_EAC,TextureFormat.EAC_R},
            {KtxSharp.GlInternalFormat.GL_COMPRESSED_SIGNED_R11_EAC,TextureFormat.EAC_R_SIGNED},
            {KtxSharp.GlInternalFormat.GL_COMPRESSED_RG11_EAC,TextureFormat.EAC_RG},
            {KtxSharp.GlInternalFormat.GL_COMPRESSED_SIGNED_RG11_EAC,TextureFormat.EAC_RG_SIGNED},

            {KtxSharp.GlInternalFormat.GL_COMPRESSED_RGB8_ETC2,TextureFormat.ETC2_RGB},
            {KtxSharp.GlInternalFormat.GL_COMPRESSED_SRGB8_ETC2,TextureFormat.ETC2_RGBA8}, // ???
            {KtxSharp.GlInternalFormat.GL_COMPRESSED_RGB8_PUNCHTHROUGH_ALPHA1_ETC2,TextureFormat.ETC2_RGB}, // ???
            {KtxSharp.GlInternalFormat.GL_COMPRESSED_SRGB8_PUNCHTHROUGH_ALPHA1_ETC2,TextureFormat.ETC2_RGBA8}, // ???
            {KtxSharp.GlInternalFormat.GL_COMPRESSED_RGBA8_ETC2_EAC,TextureFormat.ETC2_RGBA8},
            {KtxSharp.GlInternalFormat.GL_COMPRESSED_SRGB8_ALPHA8_ETC2_EAC,TextureFormat.ETC2_RGBA8}, // ???

            {KtxSharp.GlInternalFormat.GL_COMPRESSED_RGBA_ASTC_4x4_KHR,TextureFormat.ASTC_4x4},
            //{KtxSharp.GlInternalFormat.GL_COMPRESSED_RGBA_ASTC_5x4_KHR,TextureFormat.ASTC_5x4},
            {KtxSharp.GlInternalFormat.GL_COMPRESSED_RGBA_ASTC_5x5_KHR,TextureFormat.ASTC_5x5},
            //{KtxSharp.GlInternalFormat.GL_COMPRESSED_RGBA_ASTC_6x5_KHR,TextureFormat.ASTC_6x5},
            {KtxSharp.GlInternalFormat.GL_COMPRESSED_RGBA_ASTC_6x6_KHR,TextureFormat.ASTC_6x6},
            //{KtxSharp.GlInternalFormat.GL_COMPRESSED_RGBA_ASTC_8x5_KHR,TextureFormat.ASTC_8x5},
            //{KtxSharp.GlInternalFormat.GL_COMPRESSED_RGBA_ASTC_8x6_KHR,TextureFormat.ASTC_8x4},
            {KtxSharp.GlInternalFormat.GL_COMPRESSED_RGBA_ASTC_8x8_KHR,TextureFormat.ASTC_8x8},
            //{KtxSharp.GlInternalFormat.GL_COMPRESSED_RGBA_ASTC_10x5_KHR,TextureFormat.ASTC_10x5},
            //{KtxSharp.GlInternalFormat.GL_COMPRESSED_RGBA_ASTC_10x6_KHR,TextureFormat.ASTC_4x4},
            //{KtxSharp.GlInternalFormat.GL_COMPRESSED_RGBA_ASTC_10x8_KHR,TextureFormat.ASTC_4x4},
            {KtxSharp.GlInternalFormat.GL_COMPRESSED_RGBA_ASTC_10x10_KHR,TextureFormat.ASTC_10x10},
            //{KtxSharp.GlInternalFormat.GL_COMPRESSED_RGBA_ASTC_12x10_KHR,TextureFormat.ASTC_12x12},
            {KtxSharp.GlInternalFormat.GL_COMPRESSED_RGBA_ASTC_12x12_KHR,TextureFormat.ASTC_12x12 },


            {KtxSharp.GlInternalFormat.GL_COMPRESSED_SRGB8_ALPHA8_ASTC_4x4_KHR,TextureFormat.ASTC_4x4},
            //{KtxSharp.GlInternalFormat.GL_COMPRESSED_SRGB8_ALPHA8_ASTC_5x4_KHR,TextureFormat.},
            {KtxSharp.GlInternalFormat.GL_COMPRESSED_SRGB8_ALPHA8_ASTC_5x5_KHR,TextureFormat.ASTC_5x5},
            //{KtxSharp.GlInternalFormat.GL_COMPRESSED_SRGB8_ALPHA8_ASTC_6x5_KHR,TextureFormat},
            {KtxSharp.GlInternalFormat.GL_COMPRESSED_SRGB8_ALPHA8_ASTC_6x6_KHR,TextureFormat.ASTC_6x6},
            //{KtxSharp.GlInternalFormat.GL_COMPRESSED_SRGB8_ALPHA8_ASTC_8x5_KHR,TextureFormat},
            //{KtxSharp.GlInternalFormat.GL_COMPRESSED_SRGB8_ALPHA8_ASTC_8x6_KHR,TextureFormat},
            {KtxSharp.GlInternalFormat.GL_COMPRESSED_SRGB8_ALPHA8_ASTC_8x8_KHR,TextureFormat.ASTC_8x8},
            //{KtxSharp.GlInternalFormat.GL_COMPRESSED_SRGB8_ALPHA8_ASTC_10x5_KHR,TextureFormat},
            //{KtxSharp.GlInternalFormat.GL_COMPRESSED_SRGB8_ALPHA8_ASTC_10x6_KHR,TextureFormat},
            //{KtxSharp.GlInternalFormat.GL_COMPRESSED_SRGB8_ALPHA8_ASTC_10x8_KHR,TextureFormat},
            {KtxSharp.GlInternalFormat.GL_COMPRESSED_SRGB8_ALPHA8_ASTC_10x10_KHR,TextureFormat.ASTC_10x10},
            //{KtxSharp.GlInternalFormat.GL_COMPRESSED_SRGB8_ALPHA8_ASTC_12x10_KHR,TextureFormat},
            {KtxSharp.GlInternalFormat.GL_COMPRESSED_SRGB8_ALPHA8_ASTC_12x12_KHR,TextureFormat.ASTC_12x12}
        };

        public static Texture2D LoadTextureKTX(byte[] bytes, bool isLinear, bool gpuOnly)
        {
            if (!IsKTX(bytes))
                throw new Exception("Invalid KTX texture. Unable to read");

            var ktx = KtxSharp.KtxLoader.LoadInput(new System.IO.MemoryStream(bytes));

            if (textureFormatMapper.TryGetValue(ktx.header.glInternalFormat, out TextureFormat textureFormat))
            {
                if (0 != (int)textureFormat)
                {
                    Texture2D texture = new Texture2D((int)ktx.header.pixelWidth, (int)ktx.header.pixelHeight, textureFormat, (int)ktx.header.numberOfMipmapLevels, isLinear);
                    try
                    {
                        texture.LoadRawTextureData(ktx.textureData.textureDataAsRawBytes);
                        texture.Apply(ktx.header.numberOfMipmapLevels > 1, gpuOnly);
                        return texture;
                    }
                    catch(Exception e)
                    {
                        Debug.LogError(e.Message + ", texture size " + ktx.textureData.textureDataAsRawBytes.Length + ", mipmaps: " + ktx.header.numberOfMipmapLevels);
                    }
                }
            }
            return null;
        }
    }
}
