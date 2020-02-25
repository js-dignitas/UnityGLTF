using System.Collections;
using System.IO;
using System.Threading.Tasks;
using GLTF;
using GLTF.Schema;
namespace UnityGLTF.Loader
{
	public interface ILoader
	{
		Task<Stream> LoadStream(string relativeFilePath);

		Stream LoadStreamSync(string jsonFilePath);

		//Stream LoadedStream { get; }

        bool GiveBack(Stream stream);

        void Clear();

		bool HasSyncLoadMethod { get; }
	}
}
