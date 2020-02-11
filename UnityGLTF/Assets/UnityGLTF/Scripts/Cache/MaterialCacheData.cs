using GLTF.Schema;
using UnityEngine;

namespace UnityGLTF.Cache
{
	public class MaterialCacheData
	{
		public Material UnityMaterial { get; set; }
		public Material UnityMaterialWithVertexColor { get; set; }
		public GLTFMaterial GLTFMaterial { get; set; }

		public Material GetContents(bool useVertexColors)
		{
			return useVertexColors ? UnityMaterialWithVertexColor : UnityMaterial;
		}

		/// <summary>
		/// Unloads the materials in this cache.
		/// </summary>
		public void Unload()
		{
			if (UnityMaterial != null)
			{
                if (Application.isEditor)
                {
                    Object.DestroyImmediate(UnityMaterial);
                }
                else
                {
                    Object.Destroy(UnityMaterial);
                }

            }

			if (UnityMaterialWithVertexColor != null)
			{
                if (Application.isEditor)
                {
                    Object.DestroyImmediate(UnityMaterialWithVertexColor);
                }
                else
                {
                    Object.Destroy(UnityMaterialWithVertexColor);
                }
            }
		}
	}
}
