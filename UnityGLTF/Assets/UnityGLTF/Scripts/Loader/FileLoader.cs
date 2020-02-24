using System.IO;
using GLTF;
using UnityEngine;
using System;
using System.Collections;
using System.Threading.Tasks;

namespace UnityGLTF.Loader
{
	public class FileLoader : ILoader
	{
		private string _rootDirectoryPath;

		public bool HasSyncLoadMethod { get; private set; }

        public bool GiveBack(Stream stream)
        {
            stream.Dispose();
            stream = null;
            return true;
        }

        public void Clear()
        {
            // nothing
        }

		public FileLoader(string rootDirectoryPath)
		{
			_rootDirectoryPath = rootDirectoryPath;
			HasSyncLoadMethod = true;
		}

		public Task<Stream> LoadStream(string gltfFilePath)
		{
			if (gltfFilePath == null)
			{
				throw new ArgumentNullException("gltfFilePath");
			}

			return LoadFileStream(_rootDirectoryPath, gltfFilePath);
		}

		private Task<Stream> LoadFileStream(string rootPath, string fileToLoad)
		{
			string pathToLoad = Path.Combine(rootPath, fileToLoad);
			if (!File.Exists(pathToLoad))
			{
				throw new FileNotFoundException("Buffer file not found", pathToLoad);
			}

			return Task.Run<Stream>(() => { return File.OpenRead(pathToLoad); });
		}

		public Stream LoadStreamSync(string gltfFilePath)
 	    {
 	        if (gltfFilePath == null)
 	        {
 	            throw new ArgumentNullException("gltfFilePath");
 	        }
 
 	        return LoadFileStreamSync(_rootDirectoryPath, gltfFilePath);
 	    }
 
 	    private Stream LoadFileStreamSync(string rootPath, string fileToLoad)
 	    {
 	        string pathToLoad = Path.Combine(rootPath, fileToLoad);
 	        if (!File.Exists(pathToLoad))
 	        {
 	            throw new FileNotFoundException("Buffer file not found", pathToLoad);
 	        }
 
 	        return File.OpenRead(pathToLoad);
 	    }
	}
}
