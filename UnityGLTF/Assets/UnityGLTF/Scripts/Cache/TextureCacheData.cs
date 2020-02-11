using GLTF.Schema;
using UnityEngine;

namespace UnityGLTF.Cache
{
	public class TextureCacheData
	{
		public GLTFTexture TextureDefinition;
		public Texture Texture;
        public bool AutoDestroy = true;

		/// <summary>
		/// Unloads the textures in this cache.
		/// </summary>
		public void Unload()
		{
            if (AutoDestroy)
            {
                if (Application.isEditor)
                {
                    Object.DestroyImmediate(Texture);
                }
                else
                {
                    Object.Destroy(Texture);
                }
            }
		}
	}
}
