using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityGLTF
{
    struct Astc
    {
        public System.Byte[] magic;
        public System.Byte blockDimX;
        public System.Byte blockDimY;
        public System.Byte blockDimZ;
        public int xsize;
        public int ysize;
        public int zsize;

        public void Read(byte[] bytes)
        {
            int i = 0;
            magic = new byte[4];
            magic[0] = bytes[i]; i += 1;
            magic[1] = bytes[i]; i += 1;
            magic[2] = bytes[i]; i += 1;
            magic[3] = bytes[i]; i += 1;
            blockDimX = bytes[i]; i += 1;
            blockDimY = bytes[i]; i += 1;
            blockDimZ = bytes[i]; i += 1;
            xsize = bytes[i] | bytes[i + 1] << 8 | bytes[i + 2] << 16; i += 3;
            ysize = bytes[i] | bytes[i + 1] << 8 | bytes[i + 2] << 16; i += 3;
            zsize = bytes[i] | bytes[i + 1] << 8 | bytes[i + 2] << 16; i += 3;
        }

        public static bool IsASTC(byte[] bytes)
        {
            return
                bytes[0] == 0x13 &&
                bytes[1] == 0xAB &&
                bytes[2] == 0xA1 &&
                bytes[3] == 0x5C;
        }

        public static Texture2D LoadTexture(byte[] bytes, bool gpuOnly, bool verbose, int maxDim = 32768)
        {
            if (!IsASTC(bytes))
                throw new Exception("Invalid ASTC texture. Unable to read"); 

            Astc astc = new Astc();
            astc.Read(bytes);

            int height = (int)astc.xsize;
            int width = (int)astc.ysize;

            TextureFormat textureFormat = TextureFormat.ASTC_4x4;
            if (astc.blockDimX == 5 && astc.blockDimY == 5)
            {
                textureFormat = TextureFormat.ASTC_5x5;
            }
           // Debug.Log($"Texture {width}x{height}, format: {textureFormat.ToString()}");

            Texture2D texture = new Texture2D(width, height, textureFormat, false);
            texture.LoadRawTextureData(bytes);
            texture.Apply(false, gpuOnly);

            return texture;
        }
    }
}
