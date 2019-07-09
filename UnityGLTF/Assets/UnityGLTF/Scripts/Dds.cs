using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityGLTF
{
    struct Dds
    {
        public System.UInt32 size;
        public System.UInt32 flags;
        public System.UInt32 height;
        public System.UInt32 width;
        public System.UInt32 pitchOrLinearSize;
        public System.UInt32 depth;
        public System.UInt32 mipmapCount;
        // reserve 44 bytes
        public PixelFormat pixelFormat;
        // caps
        // caps2
        // caps3
        // caps4

        public enum Flags
        {
            Caps = 0x1,
            Height = 0x2,
            Width = 0x4,
            Pitch = 0x8,
            PixelFormat = 0x1000,
            MipMapCount = 0x20000,
            LinearSize = 0x80000,
            Depth = 0x800000
        }

        public bool HasFlag(Flags flag)
        {
            return (flags & (UInt32)flag) == (UInt32)flag;
        }
        public bool HasPixelFormat()
        {
            return HasFlag(Flags.PixelFormat);
        }
        public void Read(byte[] bytes)
        {
            int i = 4;
            size = BitConverter.ToUInt32(bytes, i); i += 4;
            flags = BitConverter.ToUInt32(bytes, i); i += 4;
            height = BitConverter.ToUInt32(bytes, i); i += 4;
            width = BitConverter.ToUInt32(bytes, i); i += 4;
            pitchOrLinearSize = BitConverter.ToUInt32(bytes, i); i += 4;
            depth = BitConverter.ToUInt32(bytes, i); i += 4;
            mipmapCount = BitConverter.ToUInt32(bytes, i); i += 4;
            i += 44;
            pixelFormat = new PixelFormat();
            pixelFormat.Read(bytes, ref i);
        }
        public struct PixelFormat
        {
            public enum Flags
            {
                AlphaPixels = 0x1,
                Alpha = 0x2,
                FourCC = 0x4,
                RGB = 0x40,
                YUV = 0x200,
                Luminance = 0x20000
            }

            public enum FourCC
            {
                DXT1 = 0x31545844,
                DXT3 = 0x33545844,
                DXT5 = 0x35545844
            }

            public System.UInt32 size;
            public System.UInt32 flags;
            public FourCC fourCC;
            public System.UInt32 rgbBitCount;
            public System.UInt32 rBitMask;
            public System.UInt32 gBitMask;
            public System.UInt32 bBitMask;
            public System.UInt32 aBitMask;

            public void Read(byte[] bytes, ref int offset)
            {
                int i = offset;
                size = BitConverter.ToUInt32(bytes, i); i += 4;
                flags = BitConverter.ToUInt32(bytes, i); i += 4;
                fourCC = (FourCC)BitConverter.ToUInt32(bytes, i); i += 4;
                rgbBitCount = BitConverter.ToUInt32(bytes, i); i += 4;
                rBitMask = BitConverter.ToUInt32(bytes, i); i += 4;
                gBitMask = BitConverter.ToUInt32(bytes, i); i += 4;
                bBitMask = BitConverter.ToUInt32(bytes, i); i += 4;
                aBitMask = BitConverter.ToUInt32(bytes, i); i += 4;
            }
            public bool HasFlag(PixelFormat.Flags flag)
            {
                return (flags & (UInt32)flag) == (UInt32)flag;
            }
        };

     

        public static bool IsDDS(byte[] bytes)
        {
            return bytes[4] == 124;
        }

        public static int CalcSize(int width, int height, TextureFormat format)
        {
            int factor = format == TextureFormat.DXT1 ? 8 : 16;
            int size = Math.Max(1, ((width + 3) / 4)) * Math.Max(1, ((height + 3) / 4)) * factor;
            return size;
        }
        public static Texture2D LoadTextureDXT(byte[] ddsBytes, TextureFormat textureFormat, bool isLinear, bool gpuOnly, bool verbose, int maxDim = 32768)
        {
            int skipLevels = 10;
            if (textureFormat != TextureFormat.DXT1 && textureFormat != TextureFormat.DXT5)
                throw new Exception("Invalid TextureFormat. Only DXT1 and DXT5 formats are supported by this method.");

            if (!IsDDS(ddsBytes))
                throw new Exception("Invalid DDS DXTn texture. Unable to read");  //this header byte should be 124 for DDS image files

            Dds dds = new Dds();
            dds.Read(ddsBytes);

            int height = (int)dds.height;
            int width = (int)dds.width;

            skipLevels = Math.Min(skipLevels, (int)dds.mipmapCount - 1);


            if (dds.HasPixelFormat())
            {
                if (dds.pixelFormat.HasFlag(PixelFormat.Flags.FourCC))
                {
                    if(dds.pixelFormat.fourCC == PixelFormat.FourCC.DXT1)
                    {
                        textureFormat = TextureFormat.DXT1;
                    }
                    else if(dds.pixelFormat.fourCC == PixelFormat.FourCC.DXT5)
                    {
                        textureFormat = TextureFormat.DXT5;
                    }
                }
            }

            int mipImageSizeSkip = 0;

            for(int i = 0; i < skipLevels && width > 64 && height > 64; i++)
            
            {
                int mipImageSize = CalcSize(width, height, textureFormat);
                mipImageSizeSkip += mipImageSize;
                height = Math.Max(height >> 1, 1);
                width = Math.Max(width >> 1, 1);
            }

            int DDS_HEADER_SIZE = 128;
            byte[] dxtBytes = new byte[ddsBytes.Length - DDS_HEADER_SIZE - mipImageSizeSkip];
            Buffer.BlockCopy(ddsBytes, DDS_HEADER_SIZE + mipImageSizeSkip, dxtBytes, 0, dxtBytes.Length);

            if (verbose)
            {
                Debug.Log("mipImageSizeSkip: " + mipImageSizeSkip + ", dxtBytes.Length: " + dxtBytes.Length + ", width: " + width);

                if (width * height == 1)
                {
                    Debug.Log("Texture 1x1 dim, size: " + dxtBytes.Length);

                }
            }

            Texture2D texture = new Texture2D(width, height, textureFormat, true);//, isLinear);
            texture.LoadRawTextureData(dxtBytes);
            texture.Apply();
            if (verbose)
            {
                Debug.Log("Image   size  : " + dxtBytes.Length);
                var raw = texture.GetRawTextureData();
                Debug.Log("Texture format:    " + texture.format + "," + texture.graphicsFormat + ", size: " + raw.Length + ",mips: " + texture.mipmapCount + ", bytes: " + raw[0] + " " + raw[1] + " " + raw[2] + " " + raw[3]);
            }

            return texture;
        }
    }
}
