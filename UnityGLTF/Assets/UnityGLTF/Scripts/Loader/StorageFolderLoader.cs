#if WINDOWS_UWP
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;
using System;
using System.Collections;

namespace UnityGLTF.Loader
{
    public class StorageFolderLoader : ILoader
    {
        private StorageFolder _rootFolder;

        public bool HasSyncLoadMethod => false;

        public StorageFolderLoader(StorageFolder rootFolder)
        {
            _rootFolder = rootFolder;
        }

        public bool GiveBack(Stream stream)
        {
            stream.Dispose();
            stream = null;
            return true;
        }
        public void Clear()
        {
        }

        public Task<Stream> LoadStream(string gltfFilePath)
        {
            if (gltfFilePath == null)
            {
                throw new ArgumentNullException("gltfFilePath");
            }
            
            return LoadStorageFile(gltfFilePath);
        }

        public Stream LoadStreamSync(string gltfFilePath)
        {
            throw new NotImplementedException();
        }


        public async Task<Stream> LoadStorageFile(string path)
        {
            StorageFolder parentFolder = _rootFolder;
            string fileName = Path.GetFileName(path);
            if (path != fileName)
            {
                string folderToLoad = path.Substring(0, path.Length - fileName.Length);
                parentFolder = await _rootFolder.GetFolderAsync(folderToLoad);
            }

            StorageFile bufferFile = await parentFolder.GetFileAsync(fileName);
            return await bufferFile.OpenStreamForReadAsync();
        }
    }
}
#endif
