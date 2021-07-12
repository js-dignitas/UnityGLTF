using GLTF;
using GLTF.Extensions;
using GLTF.Schema;
using GLTF.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
#if !WINDOWS_UWP_IGNORE_THIS
using System.Threading;
#endif
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using UnityGLTF.Cache;
using UnityGLTF.Extensions;
using UnityGLTF.Loader;
using UnityEngine.Profiling;
using Matrix4x4 = GLTF.Math.Matrix4x4;
using Object = UnityEngine.Object;
using Unity.Collections;
using UnityEngine.Events;
#if !WINDOWS_UWP_IGNORE_THIS
using ThreadPriority = System.Threading.ThreadPriority;
#endif
using WrapMode = UnityEngine.WrapMode;

namespace UnityGLTF
{
    public class GlobalTextureCacheData
    {
        public int refCount = 0;
        public string key;
        public Texture2D tex;
    }

    public class GltfGlobalCache
    {
        // Backup for AssetCache
        Dictionary<string, GlobalTextureCacheData> keyToTex = new Dictionary<string, GlobalTextureCacheData>();
        // For removing ref count
        Dictionary<Texture2D, GlobalTextureCacheData> textureToRefCount = new Dictionary<Texture2D, GlobalTextureCacheData>();

        public void Clear()
        {
            keyToTex.Clear();
            textureToRefCount.Clear();
        }

        public bool Contains(string key)
        {
            return keyToTex.ContainsKey(key);
        }
        public Texture2D GetTexture(string key)
        {
            lock(this)
            {
                GlobalTextureCacheData data;
                if (keyToTex.TryGetValue(key, out data))
                {
                    data.refCount++;
                    //Debug.Log("Texture " + key + ": " + data.refCount);
                    return data.tex;
                }
                return null;
            }
        }

        public Texture2D Add(string key, Texture2D tex)
        {
            lock (this)
            {
                GlobalTextureCacheData data;
                if (!keyToTex.TryGetValue(key, out data))
                {
                    data = new GlobalTextureCacheData();
                    data.tex = tex;
                    data.key = key;
                    data.refCount = 1;
                    keyToTex.Add(key, data);
                    textureToRefCount.Add(tex, data);
                    //Debug.Log("Texture " + key + ": " + data.refCount);
                }
                return data.tex;
            }
        }

        public void RemoveRef(Texture2D tex)
        {
            lock (this)
            {
                GlobalTextureCacheData data;
                if (textureToRefCount.TryGetValue(tex, out data))
                {
                    data.refCount--;
                    if (data.refCount == 0)
                    {
                        keyToTex.Remove(data.key);
                        textureToRefCount.Remove(tex);
                        Object.Destroy(tex);
                    }
                    //Debug.Log("Texture " + data.key + ": " + data.refCount);
                }
            }
        }
    }


    public struct MeshConstructionData
	{
		public MeshPrimitive Primitive { get; set; }
		public Dictionary<string, AttributeAccessor> MeshAttributes { get; set; }
	}

	public class UnityMeshData
	{
		public Vector3[] Vertices;
		public Vector3[] Normals;
		public Vector2[] Uv1;
		public Vector2[] Uv2;
		public Vector2[] Uv3;
		public Vector2[] Uv4;
		public Color[] Colors;
		public int[] Triangles;
		public Vector4[] Tangents;
		public BoneWeight[] BoneWeights;
	}

	/// <summary>
	/// Converts gltf animation data to unity
	/// </summary>
	public delegate float[] ValuesConvertion(NumericArray data, int frame);

    public class GLTFSceneImporter : IDisposable
    {
        public enum ColliderType
        {
            None,
            Box,
            Mesh,
            MeshConvex
        }

        /// <summary>
        /// Maximum LOD
        /// </summary>
        public int MaximumLod = 300;

        /// <summary>
        /// Timeout for certain threading operations
        /// </summary>
        public int Timeout = 8;

        private bool _isMultithreaded;

        public float downloadingTime = 0;
        public float processingTime = 0;
        public float activateTime = 0;

        /// <summary>
        /// Use Multithreading or not.
        /// In editor, this is always false. This is to prevent a freeze in editor (noticed in Unity versions 2017.x and 2018.x)
        /// </summary>
        public bool IsMultithreaded
        {
            get
            {
                return _isMultithreaded;
            }
            set
            {
                _isMultithreaded = value;
            }
        }

        /// <summary>
        /// The parent transform for the created GameObject
        /// </summary>
        public Transform SceneParent { get; set; }

        /// <summary>
        /// The last created object
        /// </summary>
        public GameObject CreatedObject { get; private set; }

        /// <summary>
        /// Adds colliders to primitive objects when created
        /// </summary>
        public ColliderType Collider { get; set; }

        /// <summary>
        /// Override for the shader to use on created materials
        /// </summary>
        public string CustomShaderName { get; set; }

        public Material CustomMaterial { get; set; }
        public Material CustomMaterialUnlit { get; set; }
        /// <summary>
        /// Override for the shader to use on created materials with AlphaTest set to MASK
        /// </summary>
        public string CustomAlphaTestShaderName { get; set; }
        public Material CustomAlphaTestMaterial { get; set; }
        public Material CustomAlphaTestMaterialUnlit { get; set; }

        /// <summary>
        /// Whether to keep a CPU-side copy of the mesh after upload to GPU (for example, in case normals/tangents need recalculation)
        /// </summary>
        public bool KeepCPUCopyOfMesh = true;

        /// <summary>
        /// Whether to keep a CPU-side copy of the texture after upload to GPU
        /// </summary>
        /// <remaks>
        /// This is is necessary when a texture is used with different sampler states, as Unity doesn't allow setting
        /// of filter and wrap modes separately form the texture object. Setting this to false will omit making a copy
        /// of a texture in that case and use the original texture's sampler state wherever it's referenced; this is
        /// appropriate in cases such as the filter and wrap modes being specified in the shader instead
        /// </remaks>
        public bool KeepCPUCopyOfTexture = true;

        /// <summary>
        /// When screen coverage is above threashold and no LOD mesh cull the object
        /// </summary>
        public bool CullFarLOD = false;

        protected struct GLBStream
        {
            public Stream Stream;
            public long StartPosition;
        }

        protected AsyncCoroutineHelper _asyncCoroutineHelper;

        protected GameObject _lastLoadedScene;
        protected readonly GLTFMaterial DefaultMaterial = new GLTFMaterial();
        protected MaterialCacheData _defaultLoadedMaterial = null;

        protected string _gltfFileName;
        protected GLBStream _gltfStream;
        protected GLTFRoot _gltfRoot;
        protected AssetCache _assetCache;
        protected ILoader _loader;
        protected bool _isRunning = false;
        public GltfGlobalCache globalCache;
        public bool startCollidersEnabled = true;
        public bool useMeshRenderers = true;
        public bool doTextureCompression = true;
        public bool verbose = false;
        public bool mipmapping = true;
        public int maxImageDim = 32768;
        public int mipmapSkip = 0;

        /// <summary>
        /// Creates a GLTFSceneBuilder object which will be able to construct a scene based off a url
        /// </summary>
        /// <param name="gltfFileName">glTF file relative to data loader path</param>
        /// <param name="externalDataLoader">Loader to load external data references</param>
        /// <param name="asyncCoroutineHelper">Helper to load coroutines on a seperate thread</param>
        public GLTFSceneImporter(string gltfFileName, ILoader externalDataLoader, AsyncCoroutineHelper asyncCoroutineHelper) : this(externalDataLoader, asyncCoroutineHelper)
        {
            _gltfFileName = gltfFileName;
        }

        public GLTFSceneImporter(GLTFRoot rootNode, ILoader externalDataLoader, AsyncCoroutineHelper asyncCoroutineHelper, Stream gltfStream = null) : this(externalDataLoader, asyncCoroutineHelper)
        {
            _gltfRoot = rootNode;
            _loader = externalDataLoader;
            if (gltfStream != null)
            {
                _gltfStream = new GLBStream { Stream = gltfStream, StartPosition = gltfStream.Position };
            }
        }

        private GLTFSceneImporter(ILoader externalDataLoader, AsyncCoroutineHelper asyncCoroutineHelper)
        {
            _loader = externalDataLoader;
            _asyncCoroutineHelper = asyncCoroutineHelper;
        }

        public void Dispose()
        {
            if (_assetCache != null)
            {
                Cleanup();
            }
            _loader?.GiveBack(_gltfStream.Stream);
        }

        public GameObject LastLoadedScene
        {
            get { return _lastLoadedScene; }
        }

        public GLTFRoot GLTFRoot
        {
            get { return _gltfRoot; }
        }

        /// <summary>
        /// Loads a glTF Scene into the LastLoadedScene field
        /// </summary>
        /// <param name="sceneIndex">The scene to load, If the index isn't specified, we use the default index in the file. Failing that we load index 0.</param>
        /// <param name="showSceneObj"></param>
        /// <param name="onLoadComplete">Callback function for when load is completed</param>
        /// <returns></returns>
        public async Task LoadSceneAsync(int sceneIndex = -1, bool showSceneObj = true, Action<GameObject, ExceptionDispatchInfo> onLoadComplete = null)
        {
            try
            {
                //Profiler.BeginThreadProfiling("ThreadGroup", "GLTF");
                //Profiler.BeginSample("GLTF.LoadSceneAsync");
                lock (this)
                {
                    if (_isRunning)
                    {
                        throw new GLTFLoadException("Cannot call LoadScene while GLTFSceneImporter is already running");
                    }

                    _isRunning = true;
                }

                if (_gltfRoot == null)
                {
                    await LoadJson(_gltfFileName);
                }

                if (_assetCache == null)
                {
                    _assetCache = new AssetCache(_gltfRoot, _loader);
                }

                await _LoadScene(sceneIndex, showSceneObj);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                onLoadComplete?.Invoke(null, ExceptionDispatchInfo.Capture(ex));
            }
            finally
            {
                lock (this)
                {
                    _isRunning = false;
                }
                //Profiler.EndSample();
            }

            onLoadComplete?.Invoke(LastLoadedScene, null);
        }

        public IEnumerator LoadScene(int sceneIndex = -1, bool showSceneObj = true, Action<GameObject, ExceptionDispatchInfo> onLoadComplete = null)
        {
            return LoadSceneAsync(sceneIndex, showSceneObj, onLoadComplete).AsCoroutine();
        }

        /// <summary>
        /// Loads a node tree from a glTF file into the LastLoadedScene field
        /// </summary>
        /// <param name="nodeIndex">The node index to load from the glTF</param>
        /// <returns></returns>
        public async Task LoadNodeAsync(int nodeIndex)
        {
            try
            {
                lock (this)
                {
                    if (_isRunning)
                    {
                        throw new GLTFLoadException("Cannot call LoadNode while GLTFSceneImporter is already running");
                    }

                    _isRunning = true;
                }

                if (_gltfRoot == null)
                {
                    await LoadJson(_gltfFileName);
                }

                if (_assetCache == null)
                {
                    _assetCache = new AssetCache(_gltfRoot, _loader);
                }

                await _LoadNode(nodeIndex);
                CreatedObject = _assetCache.NodeCache[nodeIndex];
                InitializeGltfTopLevelObject();
            }
            finally
            {
                lock (this)
                {
                    _isRunning = false;
                }
            }
        }

        /// <summary>
        /// Load a Material from the glTF by index
        /// </summary>
        /// <param name="materialIndex"></param>
        /// <returns></returns>
        public virtual async Task<Material> LoadMaterialAsync(int materialIndex)
        {
            try
            {
                lock (this)
                {
                    if (_isRunning)
                    {
                        throw new GLTFLoadException("Cannot CreateTexture while GLTFSceneImporter is already running");
                    }

                    _isRunning = true;
                }

                if (_gltfRoot == null)
                {
                    await LoadJson(_gltfFileName);
                }

                if (materialIndex < 0 || materialIndex >= _gltfRoot.Materials.Count)
                {
                    throw new ArgumentException($"There is no material for index {materialIndex}");
                }

                if (_assetCache == null)
                {
                    _assetCache = new AssetCache(_gltfRoot, _loader);
                }

                if (_assetCache.MaterialCache[materialIndex] == null)
                {
                    var def = _gltfRoot.Materials[materialIndex];
                    await ConstructMaterialImageBuffers(def);
                    await ConstructMaterial(def, materialIndex);

                }
            }
            finally
            {
                lock (this)
                {
                    _isRunning = false;
                }
            }

            return _assetCache.MaterialCache[materialIndex].UnityMaterialWithVertexColor;
        }

        /// <summary>
        /// Initializes the top-level created node by adding an instantiated GLTF object component to it,
        /// so that it can cleanup after itself properly when destroyed
        /// </summary>
        private void InitializeGltfTopLevelObject()
        {
            InstantiatedGLTFObject instantiatedGltfObject = CreatedObject.AddComponent<InstantiatedGLTFObject>();
            instantiatedGltfObject.CachedData = new RefCountedCacheData
            {
                MaterialCache = _assetCache.MaterialCache,
                MeshCache = _assetCache.MeshCache,
                TextureCache = _assetCache.TextureCache,
                globalCache = globalCache
            };
            for (int i = 0; i < _assetCache.MeshCache.Length; i++)
            {
                for (int j = 0; j < _assetCache.MeshCache[i].Length; j++)
                {
                    _assetCache.MeshCache[i][j].MeshAttributes.Clear();
                }
            }
            for (int i = 0; i < _assetCache.MaterialCache.Length; i++)
            {
                if (_assetCache.MaterialCache[i] != null)
                {
                    _assetCache.MaterialCache[i].GLTFMaterial = null;
                }
            }
            for (int i = 0; i < _assetCache.TextureCache.Length; i++)
            {
                if (_assetCache.TextureCache[i] != null)
                {
                    _assetCache.TextureCache[i].TextureDefinition = null;
                }
            }
        }

        private async Task ConstructBufferData(Node node)
        {
            //Profiler.BeginSample("GLTF.ConstructBufferData");
            MeshId mesh = node.Mesh;
            if (mesh != null)
            {
                if (mesh.Value.Primitives != null)
                {
                    await ConstructMeshAttributes(mesh.Value, mesh);
                }
            }

            if (node.Children != null)
            {
                foreach (NodeId child in node.Children)
                {
                    await ConstructBufferData(child.Value);
                }
            }

            const string msft_LODExtName = MSFT_LODExtensionFactory.EXTENSION_NAME;
            MSFT_LODExtension lodsextension = null;
            if (_gltfRoot.ExtensionsUsed != null
                && _gltfRoot.ExtensionsUsed.Contains(msft_LODExtName)
                && node.Extensions != null
                && node.Extensions.ContainsKey(msft_LODExtName))
            {
                lodsextension = node.Extensions[msft_LODExtName] as MSFT_LODExtension;
                if (lodsextension != null && lodsextension.MeshIds.Count > 0)
                {
                    for (int i = 0; i < lodsextension.MeshIds.Count; i++)
                    {
                        int lodNodeId = lodsextension.MeshIds[i];
                        await ConstructBufferData(_gltfRoot.Nodes[lodNodeId]);
                    }
                }
            }
            //Profiler.EndSample();
        }

        private async Task ConstructMeshAttributes(GLTFMesh mesh, MeshId meshId)
        {
            int meshIdIndex = meshId.Id;

            if (_assetCache.MeshCache[meshIdIndex] == null)
            {
                _assetCache.MeshCache[meshIdIndex] = new MeshCacheData[mesh.Primitives.Count];
            }

            for (int i = 0; i < mesh.Primitives.Count; ++i)
            {
                MeshPrimitive primitive = mesh.Primitives[i];
                if (_assetCache.MeshCache[meshIdIndex][i] == null)
                {
                    _assetCache.MeshCache[meshIdIndex][i] = new MeshCacheData();
                }

                if (_assetCache.MeshCache[meshIdIndex][i].MeshAttributes.Count == 0)
                {
                    await ConstructMeshAttributes(primitive, meshIdIndex, i);

                    if (useMeshRenderers)
                    {
                        if (primitive.Material != null)
                        {
                            // Delaying this call
                            //await ConstructMaterialImageBuffers(primitive.Material.Value);
                        }
                    }
                }
            }
        }

        protected string GetTextureName(TextureId textureId)
        {
            int sourceId = GetTextureSourceId(textureId.Value);

            GLTFImage image = _gltfRoot.Images[sourceId];

            // we only load the streams if not a base64 uri, meaning the data is in the uri
            if (image?.Uri != null && !URIHelper.IsBase64Uri(image.Uri))
            {
                return image.Uri;
            }
            return "";
        }
        protected async Task ConstructImageBuffer(GLTFTexture texture, int textureIndex)
        {
            int sourceId = GetTextureSourceId(texture);
            if (_assetCache.ImageStreamCache[sourceId] == null)
            {
                GLTFImage image = _gltfRoot.Images[sourceId];

                // we only load the streams if not a base64 uri, meaning the data is in the uri
                if (image.Uri != null && !URIHelper.IsBase64Uri(image.Uri))
                {
                    string fullPath = FullPath(image.Uri);
                    bool inGlobalCache = (globalCache != null && globalCache.Contains(fullPath));

                    Stream stream = null;
                    try
                    {
                        if (!inGlobalCache)
                        {
                            //float startTime = Time.time;
                            stream = await _loader.LoadStream(fullPath);
                            //this.downloadingTime += Time.time - startTime;
                        }
                        else
                        {
                            // increase ref count
                            globalCache.GetTexture(fullPath);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                    if (!inGlobalCache)
                    {
                        // If stream is null, make an empty non readable stream to mark it as attempted
                        if (stream == null)
                        {
                            Debug.Log("LoadStream came back null");
                            stream = new MemoryStream(0);
                            stream.Dispose();
                        }
                        _assetCache.ImageStreamCache[sourceId] = stream;
                    }
                }
                else if (image.Uri == null && image.BufferView != null && _assetCache.BufferCache[image.BufferView.Value.Buffer.Id] == null)
                {
                    int bufferIndex = image.BufferView.Value.Buffer.Id;
                    await ConstructBuffer(_gltfRoot.Buffers[bufferIndex], bufferIndex);
                }
            }

            _assetCache.TextureCache[textureIndex] = new TextureCacheData
            {
                TextureDefinition = texture
            };
        }

        protected IEnumerator WaitUntilEnum(WaitUntil waitUntil)
        {
            yield return waitUntil;
        }

        public static string GetFileFromUri(System.Uri uri)
        {
            return uri.Segments[uri.Segments.Length - 1];
        }

        public static Uri GetDirectoryName(System.Uri uri)
        {
            return new Uri(uri.AbsoluteUri.Remove(uri.AbsoluteUri.Length - GetFileFromUri(uri).Length - uri.Query.Length));
        }

        Uri baseUrl = null;

        private async Task LoadJson(string jsonFilePath)
        {
            //Profiler.BeginSample("GLTF.LoadJson");
            float startTime = Time.time;

            _gltfStream.Stream = await _loader.LoadStream(jsonFilePath);

            if (_gltfStream.Stream == null)
            {
                Debug.Log("Error loading " + jsonFilePath);
            }
            _gltfStream.StartPosition = 0;

            this.downloadingTime += Time.time - startTime;

            this.baseUrl = GetDirectoryName(new Uri(jsonFilePath));

            await Task.Run(() => GLTFParser.ParseJson(_gltfStream.Stream, out _gltfRoot, _gltfStream.StartPosition));
            //Profiler.EndSample();

        }

        public string FullPath(string path)
        {
            return new Uri(baseUrl, path).AbsoluteUri;
        }
        private static void RunCoroutineSync(IEnumerator streamEnum)
        {
            var stack = new Stack<IEnumerator>();
            stack.Push(streamEnum);
            while (stack.Count > 0)
            {
                var enumerator = stack.Pop();
                if (enumerator.MoveNext())
                {
                    stack.Push(enumerator);
                    var subEnumerator = enumerator.Current as IEnumerator;
                    if (subEnumerator != null)
                    {
                        stack.Push(subEnumerator);
                    }
                }
            }
        }

        private async Task _LoadNode(int nodeIndex)
        {
            //Profiler.BeginSample("GLTF.LoadNode");
            if (nodeIndex >= _gltfRoot.Nodes.Count)
            {
                throw new ArgumentException("nodeIndex is out of range");
            }

            Node nodeToLoad = _gltfRoot.Nodes[nodeIndex];

            if (!IsMultithreaded)
            {
                await ConstructBufferData(nodeToLoad);
            }
            else
            {
                await Task.Run(() => ConstructBufferData(nodeToLoad));
            }

            await ConstructNode(nodeToLoad, nodeIndex);
            //Profiler.EndSample();
        }

        /// <summary>
        /// Creates a scene based off loaded JSON. Includes loading in binary and image data to construct the meshes required.
        /// </summary>
        /// <param name="sceneIndex">The bufferIndex of scene in gltf file to load</param>
        /// <returns></returns>
        protected async Task _LoadScene(int sceneIndex = -1, bool showSceneObj = true)
        {
            GLTFScene scene;

            if (sceneIndex >= 0 && sceneIndex < _gltfRoot.Scenes.Count)
            {
                scene = _gltfRoot.Scenes[sceneIndex];
            }
            else
            {
                scene = _gltfRoot.GetDefaultScene();
            }

            if (scene == null)
            {
                throw new GLTFLoadException("No default scene in gltf file.");
            }

            float startTime = Time.time;

            await ConstructScene(scene, showSceneObj);
            if(!Application.isPlaying) { return; }

            this.processingTime += Time.time - startTime;

            if (SceneParent != null)
            {
                CreatedObject.transform.SetParent(SceneParent, false);
                CreatedObject.SetActive(true);
            }

            _lastLoadedScene = CreatedObject;
        }

        public class TimerData
        {
            public string label;
            public System.Diagnostics.Stopwatch sw;
        }
        Queue<TimerData> TimerDataPool = new Queue<TimerData>();
        public TimerData StartTimer(string label)
        {
            if (this.verbose)
            {
                if (TimerDataPool.Count > 0)
                {
                    var timerData = TimerDataPool.Dequeue();
                    timerData.sw.Restart();
                    timerData.label = label;
                    return timerData;
                }
                else
                {
                    TimerData timerData = new TimerData()
                    {
                        sw = System.Diagnostics.Stopwatch.StartNew(),
                        label = label
                    };
                    return timerData;
                }
            }
            else
            {
                return null;
            }
        }
        public void StopTimer(TimerData timerData)
        {
            if (timerData != null)
            {
                Debug.Log($"{Thread.CurrentThread.Name},{timerData.sw.Elapsed.TotalMilliseconds}ms: " + timerData.label);
                TimerDataPool.Enqueue(timerData);
            }
        }
        protected async Task ConstructBuffer(GLTFBuffer buffer, int bufferIndex)
        {
            var sw = StartTimer("ConstructBuffer");
            if (buffer.Uri == null)
            {
                _assetCache.BufferCache[bufferIndex] = ConstructBufferFromGLB(bufferIndex);
            }
            else
            {
                Stream bufferDataStream = null;
                var uri = buffer.Uri;

                byte[] bufferData;
                URIHelper.TryParseBase64(uri, out bufferData);
                if (bufferData != null)
                {
                    bufferDataStream = new MemoryStream(bufferData, 0, bufferData.Length, false, true);
                }
                else
                {
                    float startTime = Time.time;
                    bufferDataStream = await _loader.LoadStream(FullPath(buffer.Uri));
                    this.downloadingTime += Time.time - startTime;
                }

                _assetCache.BufferCache[bufferIndex] = new BufferCacheData
                {
                    Stream = bufferDataStream
                };
            }
            StopTimer(sw);
        }

        protected async Task ConstructImage(GLTFTexture texture, int textureIndex, GLTFImage image, int imageCacheIndex, bool markGpuOnly, bool isLinear)
        {
            if (_assetCache.ImageCache[imageCacheIndex] == null)
            {
                Stream stream = null;
                if (image.Uri == null)
                {
                    var bufferView = image.BufferView.Value;
                    var data = new byte[bufferView.ByteLength];

                    BufferCacheData bufferContents = _assetCache.BufferCache[bufferView.Buffer.Id];
                    bufferContents.Stream.Position = bufferView.ByteOffset + bufferContents.ChunkOffset;
                    stream = new SubStream(bufferContents.Stream, 0, data.Length);
                }
                else
                {
                    string uri = image.Uri;

                    byte[] bufferData;
                    URIHelper.TryParseBase64(uri, out bufferData);
                    if (bufferData != null)
                    {
                        stream = new MemoryStream(bufferData, 0, bufferData.Length, false, true);
                    }
                    else
                    {
                        stream = _assetCache.ImageStreamCache[imageCacheIndex];
                        if (stream == null)
                        {
                            // Still need to download this
                            await ConstructImageBuffer(texture, textureIndex);
                            // Now get it from the cache, if failed, it will be a non readable Stream
                            stream = _assetCache.ImageStreamCache[imageCacheIndex];
                        }
                        if (stream != null && !stream.CanRead)
                        {
                            stream = null;
                        }
                    }
                }

                {
                    int frame = Time.frameCount;
                    if (_asyncCoroutineHelper != null) await _asyncCoroutineHelper.YieldOnTimeout("create stream");
                    if (Time.frameCount != frame)
                    {
                        //Debug.Log("Frame moved " + (Time.frameCount - frame));
                    }
                }
                if (globalCache != null || stream != null)
                {
                    await ConstructUnityTexture(stream, markGpuOnly, isLinear, image, imageCacheIndex);

                    // Don't need this stream anymore
                    _loader.GiveBack(stream);
                    _assetCache.ImageStreamCache[imageCacheIndex] = null;
                }
            }
        }

        class TextureRef
        {
            public Texture2D texture;
        }

        static bool HasJpegHeader(byte[] buffer)
        {
            var soi = BitConverter.ToUInt16(buffer, 0);
            var marker = BitConverter.ToUInt16(buffer, 2);
            return soi == 0xd8ff && (marker & 0xe0ff) == 0xe0ff;
        }

        static bool IsBASIS(byte[] buffer, int offset)
        {
            return
                buffer[offset + 0] == 0x73 &&
                buffer[offset + 1] == 0x42;
        }

        async Task<Texture2D> LoadBASIS(byte[] buffer, int offset, int length, bool markGpuOnly, bool isLinear)
        {
            var basisTex = new KtxUnity.BasisUniversalTexture();
            Texture2D texture = null;

            using (var na = new NativeArray<byte>(buffer, KtxUnity.KtxNativeInstance.defaultAllocator))
            {
                // This will spawn a coroutine and then call our handler
                var result = await basisTex.LoadFromBytes(na, isLinear);
                texture = result.texture;

                // if timeouted out, then print an error
                if (texture == null)
                {
                    Debug.LogError("The semaphore for the basis transcoder did not get released.");
                }

            }
            return texture;
        }
        async Task ConstructUnityTextureFromBytes(byte[] buffer, int offset, int length, bool markGpuOnly, bool isLinear, GLTFImage image, TextureRef textureRef)
        {
            Texture2D texture = null;
            if (IsBASIS(buffer, offset))
            {
                texture = await LoadBASIS(buffer, offset, length, markGpuOnly, isLinear);
            }
            else if (Dds.IsDDS(buffer, offset))
            {
                var format = TextureFormat.DXT1;
                texture = Dds.LoadTextureDXT(buffer, offset, length, format, isLinear, markGpuOnly, verbose, maxImageDim, mipmapSkip);
                if (_asyncCoroutineHelper != null) await _asyncCoroutineHelper.YieldOnTimeout("LoadTextureDXT");
            }
            else if (Ktx.IsKTX(buffer))
            {
                texture = Ktx.LoadTextureKTX(buffer, isLinear, markGpuOnly);
                if (_asyncCoroutineHelper != null) await _asyncCoroutineHelper.YieldOnTimeout("LoadTextureKTX");
                if (texture == null)
                {
                    Debug.Log("Last image error was for " + image.Uri);
                }
            }
            else if (Astc.IsASTC(buffer))
            {
                texture = Astc.LoadTexture(buffer, markGpuOnly, verbose);
                if (_asyncCoroutineHelper != null) await _asyncCoroutineHelper.YieldOnTimeout("LoadTextureKTX");
            }


            if (texture == null)
            {
                TextureFormat format = TextureFormat.RGBA32;

                if (HasJpegHeader(buffer))
                {
                    format = TextureFormat.RGB24;
                }

                texture = new Texture2D(0, 0, format, mipmapping, isLinear);
                texture.LoadImage(buffer, markGpuOnly && !doTextureCompression);
                if (verbose)
                {
                    Debug.Log("Image   size  : " + buffer.Length);
                    var raw = texture.GetRawTextureData();
                    Debug.Log("Texture format:    " + texture.format + ", size: " + raw.Length + ",mips: " + texture.mipmapCount + ", bytes: " + raw[0] + " " + raw[1] + " " + raw[2] + " " + raw[3]);
                }
                if (_asyncCoroutineHelper != null) await _asyncCoroutineHelper.YieldOnTimeout("LoadImage");
                if (doTextureCompression)
                {
                    texture.Compress(true);
                }
            }
            textureRef.texture = texture;
        }

        protected virtual async Task ConstructUnityTexture(Stream stream, bool markGpuOnly, bool isLinear, GLTFImage image, int imageCacheIndex)
        {
            Texture2D texture = null;
            string fullPath = FullPath(image.Uri);
            if (image.Uri != null && globalCache != null)
            {
                texture = globalCache.GetTexture(fullPath);
                if (texture != null && stream == null)
                {
                    // Remove the ref count created earlier to hold the texture in the cache before it got to this code
                    globalCache.RemoveRef(texture);
                }
            }

            if (texture == null)
            {

                if (stream == null)
                {
                    texture = new Texture2D(2, 2);
                }
                else
                {
                    if (stream is MemoryStream)
                    {
                        MemoryStream memoryStream = stream as MemoryStream;
                        {
                            // Avoid creating yet another byte array. Use the internal bufer of the stream
                            byte[] buffer = memoryStream.GetBuffer();

                            var texRef = new TextureRef();
                            await ConstructUnityTextureFromBytes(buffer, 0, (int)memoryStream.Length, markGpuOnly, isLinear, image, texRef);
                            texture = texRef.texture;
                        }
                    }
                    else
                    {
                        byte[] buffer = new byte[stream.Length];

                        // todo: potential optimization is to split stream read into multiple frames (or put it on a thread?)
                        if (stream.Length > int.MaxValue)
                        {
                            throw new Exception("Stream is larger than can be copied into byte array");
                        }
                        stream.Read(buffer, 0, (int)stream.Length);

                        int frame = Time.frameCount;
                        if (_asyncCoroutineHelper != null) await _asyncCoroutineHelper.YieldOnTimeout("Stream read");
                        if (Time.frameCount != frame)
                        {
                            //Debug.Log("Frame moved " + (Time.frameCount - frame));
                        }

                        var texRef = new TextureRef();
                        await ConstructUnityTextureFromBytes(buffer, 0, buffer.Length, markGpuOnly, isLinear, image, texRef);
                        texture = texRef.texture;
                    }
                }

                if (image.Uri != null && globalCache != null)
                {
                    Object.DontDestroyOnLoad(texture);
                    texture.name = fullPath;
                    var textureActual = globalCache.Add(fullPath, texture);
                    if (textureActual != texture)
                    {
                        Texture2D.Destroy(texture);
                        texture = textureActual;
                    }
                }
            }

            _assetCache.ImageCache[imageCacheIndex] = texture;
        }

        protected virtual async Task ConstructMeshAttributes(MeshPrimitive primitive, int meshID, int primitiveIndex)
        {
            var sw = StartTimer("ConstructMeshAttributes");
            if (_assetCache.MeshCache[meshID][primitiveIndex].MeshAttributes.Count == 0)
            {
                Dictionary<string, AttributeAccessor> attributeAccessors = new Dictionary<string, AttributeAccessor>(primitive.Attributes.Count + 1);
                foreach (var attributePair in primitive.Attributes)
                {
                    BufferId bufferIdPair = attributePair.Value.Value.BufferView.Value.Buffer;
                    GLTFBuffer buffer = bufferIdPair.Value;
                    int bufferId = bufferIdPair.Id;

                    // on cache miss, load the buffer
                    if (_assetCache.BufferCache[bufferId] == null)
                    {
                        await ConstructBuffer(buffer, bufferId);
                    }

                    AttributeAccessor attributeAccessor = new AttributeAccessor
                    {
                        AccessorId = attributePair.Value,
                        Stream = _assetCache.BufferCache[bufferId].Stream,
                        Offset = (uint)_assetCache.BufferCache[bufferId].ChunkOffset
                    };

                    attributeAccessors[attributePair.Key] = attributeAccessor;
                }

                if (primitive.Indices != null)
                {
                    int bufferId = primitive.Indices.Value.BufferView.Value.Buffer.Id;

                    if (_assetCache.BufferCache[bufferId] == null)
                    {
                        await ConstructBuffer(primitive.Indices.Value.BufferView.Value.Buffer.Value, bufferId);
                    }

                    AttributeAccessor indexBuilder = new AttributeAccessor
                    {
                        AccessorId = primitive.Indices,
                        Stream = _assetCache.BufferCache[bufferId].Stream,
                        Offset = (uint)_assetCache.BufferCache[bufferId].ChunkOffset
                    };

                    attributeAccessors[SemanticProperties.INDICES] = indexBuilder;
                }

                var sw2 = StartTimer("BuildMeshAttributes");
                GLTFHelpers.BuildMeshAttributes(ref attributeAccessors);
                StopTimer(sw2);

                TransformAttributes(ref attributeAccessors);
                _assetCache.MeshCache[meshID][primitiveIndex].MeshAttributes = attributeAccessors;
            }
            StopTimer(sw);
        }


        protected void TransformAttributes(ref Dictionary<string, AttributeAccessor> attributeAccessors)
        {
            var sw = StartTimer("TransformAttributes");
            // Flip vectors and triangles to the Unity coordinate system.
            if (attributeAccessors.ContainsKey(SemanticProperties.POSITION))
            {
                AttributeAccessor attributeAccessor = attributeAccessors[SemanticProperties.POSITION];
                SchemaExtensions.ConvertVector3CoordinateSpace(ref attributeAccessor, SchemaExtensions.CoordinateSpaceConversionScale);
            }
            if (attributeAccessors.ContainsKey(SemanticProperties.INDICES))
            {
                AttributeAccessor attributeAccessor = attributeAccessors[SemanticProperties.INDICES];
                SchemaExtensions.FlipFaces(ref attributeAccessor);
            }
            if (attributeAccessors.ContainsKey(SemanticProperties.NORMAL))
            {
                AttributeAccessor attributeAccessor = attributeAccessors[SemanticProperties.NORMAL];
                SchemaExtensions.ConvertVector3CoordinateSpace(ref attributeAccessor, SchemaExtensions.CoordinateSpaceConversionScale);
            }
            // TexCoord goes from 0 to 3 to match GLTFHelpers.BuildMeshAttributes
            for (int i = 0; i < 4; i++)
            {
                if (attributeAccessors.ContainsKey(SemanticProperties.TexCoord(i)))
                {
                    AttributeAccessor attributeAccessor = attributeAccessors[SemanticProperties.TexCoord(i)];
                    SchemaExtensions.FlipTexCoordArrayV(ref attributeAccessor);
                }
            }
            if (attributeAccessors.ContainsKey(SemanticProperties.TANGENT))
            {
                AttributeAccessor attributeAccessor = attributeAccessors[SemanticProperties.TANGENT];
                SchemaExtensions.ConvertVector4CoordinateSpace(ref attributeAccessor, SchemaExtensions.TangentSpaceConversionScale);
            }
            StopTimer(sw);
        }

        #region Animation
        static string RelativePathFrom(Transform self, Transform root)
        {
            var path = new List<String>();
            for (var current = self; current != null; current = current.parent)
            {
                if (current == root)
                {
                    return String.Join("/", path.ToArray());
                }

                path.Insert(0, current.name);
            }

            throw new Exception("no RelativePath");
        }

        protected virtual void BuildAnimationSamplers(GLTFAnimation animation, int animationId)
        {
            // look up expected data types
            var typeMap = new Dictionary<int, string>();
            foreach (var channel in animation.Channels)
            {
                typeMap[channel.Sampler.Id] = channel.Target.Path.ToString();
            }

            var samplers = _assetCache.AnimationCache[animationId].Samplers;
            var samplersByType = new Dictionary<string, List<AttributeAccessor>>
            {
                {"time", new List<AttributeAccessor>(animation.Samplers.Count)}
            };

            for (var i = 0; i < animation.Samplers.Count; i++)
            {
                // no sense generating unused samplers
                if (!typeMap.ContainsKey(i))
                {
                    continue;
                }

                var samplerDef = animation.Samplers[i];

                // set up input accessors
                BufferCacheData bufferCacheData = _assetCache.BufferCache[samplerDef.Input.Value.BufferView.Value.Buffer.Id];
                AttributeAccessor attributeAccessor = new AttributeAccessor
                {
                    AccessorId = samplerDef.Input,
                    Stream = bufferCacheData.Stream,
                    Offset = bufferCacheData.ChunkOffset
                };

                samplers[i].Input = attributeAccessor;
                samplersByType["time"].Add(attributeAccessor);

                // set up output accessors
                bufferCacheData = _assetCache.BufferCache[samplerDef.Output.Value.BufferView.Value.Buffer.Id];
                attributeAccessor = new AttributeAccessor
                {
                    AccessorId = samplerDef.Output,
                    Stream = bufferCacheData.Stream,
                    Offset = bufferCacheData.ChunkOffset
                };

                samplers[i].Output = attributeAccessor;

                if (!samplersByType.ContainsKey(typeMap[i]))
                {
                    samplersByType[typeMap[i]] = new List<AttributeAccessor>();
                }

                samplersByType[typeMap[i]].Add(attributeAccessor);
            }

            // populate attributeAccessors with buffer data
            GLTFHelpers.BuildAnimationSamplers(ref samplersByType);
        }

        protected void SetAnimationCurve(
            AnimationClip clip,
            string relativePath,
            string[] propertyNames,
            NumericArray input,
            NumericArray output,
            InterpolationType mode,
            Type curveType,
            ValuesConvertion getConvertedValues)
        {

            var channelCount = propertyNames.Length;
            var frameCount = input.AsFloats.Length;

            // copy all the key frame data to cache
            Keyframe[][] keyframes = new Keyframe[channelCount][];
            for (var ci = 0; ci < channelCount; ++ci)
            {
                keyframes[ci] = new Keyframe[frameCount];
            }

            for (var i = 0; i < frameCount; ++i)
            {
                var time = input.AsFloats[i];

                var values = getConvertedValues(output, i);

                for (var ci = 0; ci < channelCount; ++ci)
                {
                    keyframes[ci][i] = new Keyframe(time, values[ci]);
                }
            }

            for (var ci = 0; ci < channelCount; ++ci)
            {
                // set interpolcation for each keyframe
                SetCurveMode(keyframes[ci], mode);
                // copy all key frames data to animation curve and add it to the clip
                AnimationCurve curve = new AnimationCurve();
                curve.keys = keyframes[ci];
                clip.SetCurve(relativePath, curveType, propertyNames[ci], curve);
            }
        }

        protected AnimationClip ConstructClip(Transform root, GameObject[] nodes, int animationId)
        {
            GLTFAnimation animation = _gltfRoot.Animations[animationId];

            AnimationCacheData animationCache = _assetCache.AnimationCache[animationId];
            if (animationCache == null)
            {
                animationCache = new AnimationCacheData(animation.Samplers.Count);
                _assetCache.AnimationCache[animationId] = animationCache;
            }
            else if (animationCache.LoadedAnimationClip != null)
            {
                return animationCache.LoadedAnimationClip;
            }

            // unpack accessors
            BuildAnimationSamplers(animation, animationId);

            // init clip
            AnimationClip clip = new AnimationClip
            {
                name = animation.Name ?? string.Format("animation:{0}", animationId)
            };
            _assetCache.AnimationCache[animationId].LoadedAnimationClip = clip;

            // needed because Animator component is unavailable at runtime
            clip.legacy = true;

            foreach (AnimationChannel channel in animation.Channels)
            {
                AnimationSamplerCacheData samplerCache = animationCache.Samplers[channel.Sampler.Id];
                Transform node = nodes[channel.Target.Node.Id].transform;
                string relativePath = RelativePathFrom(node, root);

                NumericArray input = samplerCache.Input.AccessorContent,
                    output = samplerCache.Output.AccessorContent;

                string[] propertyNames;

                switch (channel.Target.Path)
                {
                    case GLTFAnimationChannelPath.translation:
                        propertyNames = new string[] { "localPosition.x", "localPosition.y", "localPosition.z" };

                        SetAnimationCurve(clip, relativePath, propertyNames, input, output,
                                          samplerCache.Interpolation, typeof(Transform),
                                          (data, frame) => {
                                              var position = data.AsVec3s[frame].ToUnityVector3Convert();
                                              return new float[] { position.x, position.y, position.z };
                                          });
                        break;

                    case GLTFAnimationChannelPath.rotation:
                        propertyNames = new string[] { "localRotation.x", "localRotation.y", "localRotation.z", "localRotation.w" };

                        SetAnimationCurve(clip, relativePath, propertyNames, input, output,
                                          samplerCache.Interpolation, typeof(Transform),
                                          (data, frame) => {
                                              var rotation = data.AsVec4s[frame];
                                              var quaternion = new GLTF.Math.Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W).ToUnityQuaternionConvert();
                                              return new float[] { quaternion.x, quaternion.y, quaternion.z, quaternion.w };
                                          });

                        break;

                    case GLTFAnimationChannelPath.scale:
                        propertyNames = new string[] { "localScale.x", "localScale.y", "localScale.z" };

                        SetAnimationCurve(clip, relativePath, propertyNames, input, output,
                                          samplerCache.Interpolation, typeof(Transform),
                                          (data, frame) => {
                                              var scale = data.AsVec3s[frame].ToUnityVector3Raw();
                                              return new float[] { scale.x, scale.y, scale.z };
                                          });
                        break;

                    case GLTFAnimationChannelPath.weights:
                        // TODO: add support for blend shapes/morph targets

                        // var primitives = channel.Target.Node.Value.Mesh.Value.Primitives;
                        // var targetCount = primitives[0].Targets.Count;
                        // for (int primitiveIndex = 0; primitiveIndex < primitives.Count; primitiveIndex++)
                        // {
                        // 	for (int targetIndex = 0; targetIndex < targetCount; targetIndex++)
                        // 	{
                        //
                        // 		//clip.SetCurve(primitiveObjPath, typeof(SkinnedMeshRenderer), "blendShape." + targetIndex, curves[targetIndex]);
                        // 	}
                        // }
                        break;

                    default:
                        Debug.LogWarning("Cannot read GLTF animation path");
                        break;
                } // switch target type
            } // foreach channel

            clip.EnsureQuaternionContinuity();
            return clip;
        }

        public static void SetCurveMode(Keyframe[] keyframes, InterpolationType mode)
        {
            for (int i = 0; i < keyframes.Length; ++i)
            {
                float intangent = 0;
                float outtangent = 0;
                bool intangent_set = false;
                bool outtangent_set = false;
                Vector2 point1;
                Vector2 point2;
                Vector2 deltapoint;
                var key = keyframes[i];

                if (i == 0)
                {
                    intangent = 0; intangent_set = true;
                }

                if (i == keyframes.Length - 1)
                {
                    outtangent = 0; outtangent_set = true;
                }
                switch (mode)
                {
                    case InterpolationType.STEP:
                        {
                            intangent = 0;
                            outtangent = float.PositiveInfinity;
                        }
                        break;
                    case InterpolationType.LINEAR:
                        {
                            if (!intangent_set)
                            {
                                point1.x = keyframes[i - 1].time;
                                point1.y = keyframes[i - 1].value;
                                point2.x = keyframes[i].time;
                                point2.y = keyframes[i].value;

                                deltapoint = point2 - point1;

                                intangent = deltapoint.y / deltapoint.x;
                            }
                            if (!outtangent_set)
                            {
                                point1.x = keyframes[i].time;
                                point1.y = keyframes[i].value;
                                point2.x = keyframes[i + 1].time;
                                point2.y = keyframes[i + 1].value;

                                deltapoint = point2 - point1;

                                outtangent = deltapoint.y / deltapoint.x;
                            }
                        }
                        break;
                    //use default unity curve
                    case InterpolationType.CUBICSPLINE:
                        break;
                    case InterpolationType.CATMULLROMSPLINE:
                        break;
                    default:
                        break;
                }


                key.inTangent = intangent;
                key.outTangent = outtangent;
            }
        }
        #endregion

        CustomSampler sampler = CustomSampler.Create("GLTFSampler");
        protected virtual async Task ConstructScene(GLTFScene scene, bool showSceneObj)
        {
            sampler.GetRecorder().enabled = true;
            if(!Application.isPlaying) { return; }
            var sceneObj = new GameObject(string.IsNullOrEmpty(scene.Name) ? ("GLTFScene") : scene.Name);
            sceneObj.transform.SetParent(SceneParent, false);
            //sceneObj.hideFlags = HideFlags.DontSaveInEditor;
            sceneObj.SetActive(showSceneObj);

            //Transform[] nodeTransforms = new Transform[scene.Nodes.Count];
            for (int i = 0; i < scene.Nodes.Count; ++i)
            {
                NodeId node = scene.Nodes[i];
                await _LoadNode(node.Id);
                if(!Application.isPlaying) { return; }
                GameObject nodeObj = _assetCache.NodeCache[node.Id];
                nodeObj.transform.SetParent(sceneObj.transform, false);
                nodeObj.SetActive(true);
                //nodeTransforms[i] = nodeObj.transform;
            }

            if (_gltfRoot.Animations != null && _gltfRoot.Animations.Count > 0)
            {
                // create the AnimationClip that will contain animation data
                Animation animation = sceneObj.AddComponent<Animation>();
                for (int i = 0; i < _gltfRoot.Animations.Count; ++i)
                {
                    AnimationClip clip = ConstructClip(sceneObj.transform, _assetCache.NodeCache, i);

                    clip.wrapMode = WrapMode.Loop;

                    animation.AddClip(clip, clip.name);
                    if (i == 0)
                    {
                        animation.clip = clip;
                    }
                }
            }

            CreatedObject = sceneObj;
            InitializeGltfTopLevelObject();
        }


        protected virtual async Task ConstructNode(Node node, int nodeIndex)
        {
            if (_assetCache.NodeCache[nodeIndex] != null)
            {
                return;
            }

            if(!Application.isPlaying) { return; }
            var nodeObj = new GameObject(string.IsNullOrEmpty(node.Name) ? ("GLTFNode" + nodeIndex) : node.Name);
            nodeObj.transform.SetParent(SceneParent, false);
            // If we're creating a really large node, we need it to not be visible in partial stages. So we hide it while we create it
            nodeObj.SetActive(false);
            //nodeObj.hideFlags = HideFlags.DontSaveInEditor;

            Vector3 position;
            Quaternion rotation;
            Vector3 scale;
            node.GetUnityTRSProperties(out position, out rotation, out scale);

            // Save the 64 bit floating point matrix on the Unity GameObject using the GLTFNodeMatrix component
            if (node.Matrix != GLTF.Math.Matrix4x4.Identity)
            {
                nodeObj.AddComponent<GLTFNodeMatrix>().Matrix = node.Matrix; 
            }

            if (verbose) Debug.Log("gltf node " + node.Name + ":\n" + node.Matrix + "\n");
            nodeObj.transform.localPosition = position;
            nodeObj.transform.localRotation = rotation;
            nodeObj.transform.localScale = scale;
            if (verbose) Debug.Log("unity node " + nodeObj.name + ": pos: " + position + ", euler:" + rotation.eulerAngles + ", scale: " + scale + "\n");

            if (node.Mesh != null)
            {
                await ConstructMesh(node.Mesh.Value, nodeObj.transform, node.Mesh.Id, node.Skin != null ? node.Skin.Value : null);
            }
            /* TODO: implement camera (probably a flag to disable for VR as well)
			if (camera != null)
			{
				GameObject cameraObj = camera.Value.Create();
				cameraObj.transform.parent = nodeObj.transform;
			}
			*/

            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    // todo blgross: replace with an iterartive solution
                    await ConstructNode(child.Value, child.Id);
                    GameObject childObj = _assetCache.NodeCache[child.Id];
                    childObj.transform.SetParent(nodeObj.transform, false);
                    childObj.SetActive(true);
                }
            }
            if(!Application.isPlaying) { return; }
            nodeObj.SetActive(false);
            _assetCache.NodeCache[nodeIndex] = nodeObj;

            const string msft_LODExtName = MSFT_LODExtensionFactory.EXTENSION_NAME;
            MSFT_LODExtension lodsextension = null;
            if (_gltfRoot.ExtensionsUsed != null
                && _gltfRoot.ExtensionsUsed.Contains(msft_LODExtName)
                && node.Extensions != null
                && node.Extensions.ContainsKey(msft_LODExtName))
            {
                lodsextension = node.Extensions[msft_LODExtName] as MSFT_LODExtension;
                if (lodsextension != null && lodsextension.MeshIds.Count > 0)
                {
                    int lodCount = lodsextension.MeshIds.Count + 1;
                    if (!CullFarLOD)
                    {
                        //create a final lod with the mesh as the last LOD in file
                        lodCount += 1;
                    }
                    LOD[] lods = new LOD[lodsextension.MeshIds.Count + 2];
                    List<double> lodCoverage = lodsextension.GetLODCoverage(node);

                    if(!Application.isPlaying) { return; }
                    var lodGroupNodeObj = new GameObject(string.IsNullOrEmpty(node.Name) ? ("GLTFNode_LODGroup" + nodeIndex) : node.Name);
                    //lodGroupNodeObj.hideFlags = HideFlags.DontSaveInEditor;
                    lodGroupNodeObj.SetActive(false);
                    nodeObj.transform.SetParent(lodGroupNodeObj.transform, false);
                    nodeObj.SetActive(true);
                    MeshRenderer[] childRenders = nodeObj.GetComponentsInChildren<MeshRenderer>();
                    lods[0] = new LOD(GetLodCoverage(lodCoverage, 0), childRenders);

                    LODGroup lodGroup = lodGroupNodeObj.AddComponent<LODGroup>();
                    for (int i = 0; i < lodsextension.MeshIds.Count; i++)
                    {
                        int lodNodeId = lodsextension.MeshIds[i];
                        await ConstructNode(_gltfRoot.Nodes[lodNodeId], lodNodeId);
                        int lodIndex = i + 1;
                        GameObject lodNodeObj = _assetCache.NodeCache[lodNodeId];
                        lodNodeObj.transform.SetParent(lodGroupNodeObj.transform, false);
                        childRenders = lodNodeObj.GetComponentsInChildren<MeshRenderer>();
                        lods[lodIndex] = new LOD(GetLodCoverage(lodCoverage, lodIndex), childRenders);
                    }

                    if (!CullFarLOD)
                    {
                        //use the last mesh as the LOD
                        lods[lodsextension.MeshIds.Count + 1] = new LOD(0, childRenders);
                    }

                    lodGroup.SetLODs(lods);
                    lodGroup.RecalculateBounds();
                    lodGroupNodeObj.SetActive(true);
                    _assetCache.NodeCache[nodeIndex] = lodGroupNodeObj;
                }
            }

        }

        float GetLodCoverage(List<double> lodcoverageExtras, int lodIndex)
        {
            if (lodcoverageExtras != null && lodIndex < lodcoverageExtras.Count)
            {
                return (float)lodcoverageExtras[lodIndex];
            }
            else
            {
                return 1.0f / (lodIndex + 2);
            }
        }

        private bool NeedsSkinnedMeshRenderer(MeshPrimitive primitive, Skin skin)
        {
            return HasBones(skin) || HasBlendShapes(primitive);
        }

        private bool HasBones(Skin skin)
        {
            return skin != null;
        }

        private bool HasBlendShapes(MeshPrimitive primitive)
        {
            return primitive.Targets != null;
        }

        protected virtual async Task SetupBones(Skin skin, MeshPrimitive primitive, SkinnedMeshRenderer renderer, GameObject primitiveObj, Mesh curMesh)
        {
            var boneCount = skin.Joints.Count;
            Transform[] bones = new Transform[boneCount];

            int bufferId = skin.InverseBindMatrices.Value.BufferView.Value.Buffer.Id;
            AttributeAccessor attributeAccessor = new AttributeAccessor
            {
                AccessorId = skin.InverseBindMatrices,
                Stream = _assetCache.BufferCache[bufferId].Stream,
                Offset = _assetCache.BufferCache[bufferId].ChunkOffset
            };

            GLTFHelpers.BuildBindPoseSamplers(ref attributeAccessor);

            Matrix4x4[] gltfBindPoses = attributeAccessor.AccessorContent.AsMatrix4x4s;
            UnityEngine.Matrix4x4[] bindPoses = new UnityEngine.Matrix4x4[skin.Joints.Count];

            for (int i = 0; i < boneCount; i++)
            {
                if (_assetCache.NodeCache[skin.Joints[i].Id] == null)
                {
                    await ConstructNode(_gltfRoot.Nodes[skin.Joints[i].Id], skin.Joints[i].Id);
                }
                bones[i] = _assetCache.NodeCache[skin.Joints[i].Id].transform;
                bindPoses[i] = gltfBindPoses[i].ToUnityMatrix4x4Convert();
            }

            renderer.rootBone = _assetCache.NodeCache[skin.Skeleton.Id].transform;
            curMesh.bindposes = bindPoses;
            renderer.bones = bones;
        }

        private BoneWeight[] CreateBoneWeightArray(Vector4[] joints, Vector4[] weights, int vertCount)
        {
            NormalizeBoneWeightArray(weights);

            BoneWeight[] boneWeights = new BoneWeight[vertCount];
            for (int i = 0; i < vertCount; i++)
            {
                boneWeights[i].boneIndex0 = (int)joints[i].x;
                boneWeights[i].boneIndex1 = (int)joints[i].y;
                boneWeights[i].boneIndex2 = (int)joints[i].z;
                boneWeights[i].boneIndex3 = (int)joints[i].w;

                boneWeights[i].weight0 = weights[i].x;
                boneWeights[i].weight1 = weights[i].y;
                boneWeights[i].weight2 = weights[i].z;
                boneWeights[i].weight3 = weights[i].w;
            }

            return boneWeights;
        }

        /// <summary>
        /// Ensures each bone weight influences applied to the vertices add up to 1
        /// </summary>
        /// <param name="weights">Bone weight array</param>
        private void NormalizeBoneWeightArray(Vector4[] weights)
        {
            for (int i = 0; i < weights.Length; i++)
            {
                var weightSum = (weights[i].x + weights[i].y + weights[i].z + weights[i].w);

                if (!Mathf.Approximately(weightSum, 0))
                {
                    weights[i] /= weightSum;
                }
            }
        }

        protected virtual async Task ConstructMesh(GLTFMesh mesh, Transform parent, int meshId, Skin skin)
        {
            if (_assetCache.MeshCache[meshId] == null)
            {
                _assetCache.MeshCache[meshId] = new MeshCacheData[mesh.Primitives.Count];
            }

            for (int i = 0; i < mesh.Primitives.Count; ++i)
            {
                var primitive = mesh.Primitives[i];
                int materialIndex = primitive.Material != null ? primitive.Material.Id : -1;

                await ConstructMeshPrimitive(primitive, meshId, i, materialIndex);

                //var primitiveObj = new GameObject("Primitive");
                GameObject primitiveObj;
                if (mesh.Primitives.Count == 1)
                {
                    primitiveObj = parent.gameObject;
                }
                else
                {
                    if(!Application.isPlaying) { return; }
                    primitiveObj = new GameObject("Primitive");
                    primitiveObj.transform.SetParent(SceneParent, false);
                    primitiveObj.SetActive(false);
                    //primitiveObj.hideFlags = HideFlags.DontSaveInEditor;
                }

                Mesh curMesh = _assetCache.MeshCache[meshId][i].LoadedMesh;

                if (useMeshRenderers)
                {
                    MaterialCacheData materialCacheData =
                        materialIndex >= 0 ? _assetCache.MaterialCache[materialIndex] : _defaultLoadedMaterial;

                    Material material = materialCacheData.GetContents(primitive.Attributes.ContainsKey(SemanticProperties.Color(0)));

                    if (NeedsSkinnedMeshRenderer(primitive, skin))
                    {
                        var skinnedMeshRenderer = primitiveObj.AddComponent<SkinnedMeshRenderer>();
                        skinnedMeshRenderer.material = material;
                        skinnedMeshRenderer.quality = SkinQuality.Auto;
                        // TODO: add support for blend shapes/morph targets
                        //if (HasBlendShapes(primitive))
                        //	SetupBlendShapes(primitive);
                        if (HasBones(skin))
                        {
                            await SetupBones(skin, primitive, skinnedMeshRenderer, primitiveObj, curMesh);
                        }

                        skinnedMeshRenderer.sharedMesh = curMesh;
                    }
                    else
                    {
                        var meshRenderer = primitiveObj.AddComponent<MeshRenderer>();
                        meshRenderer.material = material;
                    }

                    MeshFilter meshFilter = primitiveObj.AddComponent<MeshFilter>();
                    meshFilter.sharedMesh = curMesh;
                }

                UnityEngine.Collider collider = null;
                switch (Collider)
                {
                    case ColliderType.Box:
                        var boxCollider = primitiveObj.AddComponent<BoxCollider>();
                        collider = boxCollider;
                        boxCollider.center = curMesh.bounds.center;
                        boxCollider.size = curMesh.bounds.size;
                        break;
                    case ColliderType.Mesh:
                        var meshCollider = primitiveObj.AddComponent<MeshCollider>();
                        collider = meshCollider;

                        meshCollider.cookingOptions =
                            MeshColliderCookingOptions.CookForFasterSimulation
                            | MeshColliderCookingOptions.EnableMeshCleaning
                            | MeshColliderCookingOptions.WeldColocatedVertices
#if UNITY_2019_3_OR_NEWER
                            | MeshColliderCookingOptions.UseFastMidphase
#endif
                            ;
                        meshCollider.sharedMesh = curMesh;
                        break;
                    case ColliderType.MeshConvex:
                        var meshConvexCollider = primitiveObj.AddComponent<MeshCollider>();
                        collider = meshConvexCollider;
                        meshConvexCollider.sharedMesh = curMesh;
                        meshConvexCollider.convex = true;
                        break;
                }

                if (collider != null)
                {
                    collider.enabled = startCollidersEnabled;
                }

                if (mesh.Primitives.Count > 1)
                {
                    primitiveObj.transform.SetParent(parent, false);
                    float startTime = Time.time;
                    primitiveObj.SetActive(true);
                    this.activateTime += Time.time - startTime;
                }
                //_assetCache.MeshCache[meshId][i].PrimitiveGO = primitiveObj;
            }
        }


        protected virtual async Task ConstructMeshPrimitive(MeshPrimitive primitive, int meshID, int primitiveIndex, int materialIndex)
        {
            if (_assetCache.MeshCache[meshID][primitiveIndex] == null)
            {
                _assetCache.MeshCache[meshID][primitiveIndex] = new MeshCacheData();
            }
            if (_assetCache.MeshCache[meshID][primitiveIndex].LoadedMesh == null)
            {
                var meshAttributes = _assetCache.MeshCache[meshID][primitiveIndex].MeshAttributes;
                var meshConstructionData = new MeshConstructionData
                {
                    Primitive = primitive,
                    MeshAttributes = meshAttributes
                };

                UnityMeshData unityMeshData = null;
                if (IsMultithreaded)
                {
                    await Task.Run(() => unityMeshData = ConvertAccessorsToUnityTypes(meshConstructionData));
                }
                else
                {
                    unityMeshData = ConvertAccessorsToUnityTypes(meshConstructionData);
                }

                await ConstructUnityMesh(meshConstructionData, meshID, primitiveIndex, unityMeshData);
            }

            bool shouldUseDefaultMaterial = primitive.Material == null;

            if (useMeshRenderers)
            {
                GLTFMaterial materialToLoad = shouldUseDefaultMaterial ? DefaultMaterial : primitive.Material.Value;
                if ((shouldUseDefaultMaterial && _defaultLoadedMaterial == null) ||
                    (!shouldUseDefaultMaterial && _assetCache.MaterialCache[materialIndex] == null))
                {
                    // TODO: Thinking about putting the buffer construction here to reduce the number of streams 
                    // that need to stay open when there are many textures
                    await ConstructMaterialImageBuffers(materialToLoad);

                    try
                    {
                        await ConstructMaterial(materialToLoad, shouldUseDefaultMaterial ? -1 : materialIndex);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }
        }

        protected UnityMeshData ConvertAccessorsToUnityTypes(MeshConstructionData meshConstructionData)
        {
            // todo optimize: There are multiple copies being performed to turn the buffer data into mesh data. Look into reducing them
            MeshPrimitive primitive = meshConstructionData.Primitive;
            Dictionary<string, AttributeAccessor> meshAttributes = meshConstructionData.MeshAttributes;

            int vertexCount = (int)primitive.Attributes[SemanticProperties.POSITION].Value.Count;

            return new UnityMeshData
            {
                Vertices = primitive.Attributes.ContainsKey(SemanticProperties.POSITION)
                    ? meshAttributes[SemanticProperties.POSITION].AccessorContent.AsVertices.ToUnityVector3Raw()
                    : null,

                Normals = primitive.Attributes.ContainsKey(SemanticProperties.NORMAL)
                    ? meshAttributes[SemanticProperties.NORMAL].AccessorContent.AsNormals.ToUnityVector3Raw()
                    : null,

                Uv1 = primitive.Attributes.ContainsKey(SemanticProperties.TexCoord(0))
                    ? meshAttributes[SemanticProperties.TexCoord(0)].AccessorContent.AsTexcoords.ToUnityVector2Raw()
                    : null,

                Uv2 = primitive.Attributes.ContainsKey(SemanticProperties.TexCoord(1))
                    ? meshAttributes[SemanticProperties.TexCoord(1)].AccessorContent.AsTexcoords.ToUnityVector2Raw()
                    : null,

                Uv3 = primitive.Attributes.ContainsKey(SemanticProperties.TexCoord(2))
                    ? meshAttributes[SemanticProperties.TexCoord(2)].AccessorContent.AsTexcoords.ToUnityVector2Raw()
                    : null,

                Uv4 = primitive.Attributes.ContainsKey(SemanticProperties.TexCoord(3))
                    ? meshAttributes[SemanticProperties.TexCoord(3)].AccessorContent.AsTexcoords.ToUnityVector2Raw()
                    : null,

                Colors = primitive.Attributes.ContainsKey(SemanticProperties.Color(0))
                    ? meshAttributes[SemanticProperties.Color(0)].AccessorContent.AsColors.ToUnityColorRaw()
                    : null,

                Triangles = primitive.Indices != null
                    ? meshAttributes[SemanticProperties.INDICES].AccessorContent.AsUInts.ToIntArrayRaw()
                    : MeshPrimitive.GenerateTriangles(vertexCount),

                Tangents = primitive.Attributes.ContainsKey(SemanticProperties.TANGENT)
                    ? meshAttributes[SemanticProperties.TANGENT].AccessorContent.AsTangents.ToUnityVector4Raw()
                    : null,

                BoneWeights = meshAttributes.ContainsKey(SemanticProperties.Weight(0)) && meshAttributes.ContainsKey(SemanticProperties.Joint(0))
                    ? CreateBoneWeightArray(meshAttributes[SemanticProperties.Joint(0)].AccessorContent.AsVec4s.ToUnityVector4Raw(),
                    meshAttributes[SemanticProperties.Weight(0)].AccessorContent.AsVec4s.ToUnityVector4Raw(), vertexCount)
                    : null
            };
        }

        List<Task> tasks = new List<Task>(8);
        List<int> taskTextureIds = new List<int>(8);
        void AddConstructImageBufferTask(GLTFTexture texture, int textureIndex)
        {
            if (!taskTextureIds.Contains(textureIndex))
            {
                tasks.Add(ConstructImageBuffer(texture, textureIndex));
                taskTextureIds.Add(textureIndex);
            }
        }

		protected virtual async Task ConstructMaterialImageBuffers(GLTFMaterial def)
		{
            tasks.Clear();
            taskTextureIds.Clear();

			if (def.PbrMetallicRoughness != null)
			{
				var pbr = def.PbrMetallicRoughness;

				if (pbr.BaseColorTexture != null)
				{
					var textureId = pbr.BaseColorTexture.Index;
                    AddConstructImageBufferTask(textureId.Value, textureId.Id);
				}
				if (pbr.MetallicRoughnessTexture != null)
				{
					var textureId = pbr.MetallicRoughnessTexture.Index;

                    AddConstructImageBufferTask(textureId.Value, textureId.Id);
				}
			}

			if (def.CommonConstant != null)
			{
				if (def.CommonConstant.LightmapTexture != null)
				{
					var textureId = def.CommonConstant.LightmapTexture.Index;

                    AddConstructImageBufferTask(textureId.Value, textureId.Id);
				}
			}

			if (def.NormalTexture != null)
			{
				var textureId = def.NormalTexture.Index;
				tasks.Add(ConstructImageBuffer(textureId.Value, textureId.Id));
			}

			if (def.OcclusionTexture != null)
			{
				var textureId = def.OcclusionTexture.Index;

				if (!(def.PbrMetallicRoughness != null
						&& def.PbrMetallicRoughness.MetallicRoughnessTexture != null
						&& def.PbrMetallicRoughness.MetallicRoughnessTexture.Index.Id == textureId.Id))
				{
                    AddConstructImageBufferTask(textureId.Value, textureId.Id);
				}
			}

			if (def.EmissiveTexture != null)
			{
				var textureId = def.EmissiveTexture.Index;
                AddConstructImageBufferTask(textureId.Value, textureId.Id);
			}

			// pbr_spec_gloss extension
			const string specGlossExtName = KHR_materials_pbrSpecularGlossinessExtensionFactory.EXTENSION_NAME;
			if (def.Extensions != null && def.Extensions.ContainsKey(specGlossExtName))
			{
				var specGlossDef = (KHR_materials_pbrSpecularGlossinessExtension)def.Extensions[specGlossExtName];
				if (specGlossDef.DiffuseTexture != null)
				{
					var textureId = specGlossDef.DiffuseTexture.Index;
                    AddConstructImageBufferTask(textureId.Value, textureId.Id);
				}

				if (specGlossDef.SpecularGlossinessTexture != null)
				{
					var textureId = specGlossDef.SpecularGlossinessTexture.Index;
                    AddConstructImageBufferTask(textureId.Value, textureId.Id);
				}
			}

			foreach(var task in tasks)
            {
                await task;
            }
		}

        protected async Task ConstructUnityMesh(MeshConstructionData meshConstructionData, int meshId, int primitiveIndex, UnityMeshData unityMeshData)
        {
            MeshPrimitive primitive = meshConstructionData.Primitive;
            int vertexCount = (int)primitive.Attributes[SemanticProperties.POSITION].Value.Count;
            bool hasNormals = unityMeshData.Normals != null;

            {
                if (_asyncCoroutineHelper != null) await _asyncCoroutineHelper.YieldOnTimeout("Before mesh");
            }
            Mesh mesh = new Mesh
            {

#if UNITY_2017_3_OR_NEWER
                indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
#endif
            };

            mesh.vertices = unityMeshData.Vertices;
            {
                if (_asyncCoroutineHelper != null) await _asyncCoroutineHelper.YieldOnTimeout("mesh.vertices");
            }
            if (useMeshRenderers)
            {
                mesh.normals = unityMeshData.Normals;
                {
                    if (_asyncCoroutineHelper != null) await _asyncCoroutineHelper.YieldOnTimeout("mesh.normals");
                }
                mesh.uv = unityMeshData.Uv1;
                {
                    if (_asyncCoroutineHelper != null) await _asyncCoroutineHelper.YieldOnTimeout("mesh.uv");
                }
                mesh.uv2 = unityMeshData.Uv2;
                {
                    if (_asyncCoroutineHelper != null) await _asyncCoroutineHelper.YieldOnTimeout("mesh.uv2");
                }
                mesh.uv3 = unityMeshData.Uv3;
                {
                    if (_asyncCoroutineHelper != null) await _asyncCoroutineHelper.YieldOnTimeout("mesh.uv3");
                }
                mesh.uv4 = unityMeshData.Uv4;
                {
                    if (_asyncCoroutineHelper != null) await _asyncCoroutineHelper.YieldOnTimeout("mesh.uv4");
                }
                mesh.colors = unityMeshData.Colors;
                {
                    if (_asyncCoroutineHelper != null) await _asyncCoroutineHelper.YieldOnTimeout("mesh.colors");
                }
            }
			mesh.triangles = unityMeshData.Triangles;
            {
                if (_asyncCoroutineHelper != null) await _asyncCoroutineHelper.YieldOnTimeout("mesh.triangles");
            }
			mesh.tangents = unityMeshData.Tangents;
            {
                if (_asyncCoroutineHelper != null) await _asyncCoroutineHelper.YieldOnTimeout("mesh.tangents");
            }
			mesh.boneWeights = unityMeshData.BoneWeights;
            {
                if (_asyncCoroutineHelper != null) await _asyncCoroutineHelper.YieldOnTimeout("mesh.boneWeights");
            }

			if (!hasNormals)
			{
				mesh.RecalculateNormals();
			}

			if (!KeepCPUCopyOfMesh)
			{
				mesh.UploadMeshData(true);
			}
			_assetCache.MeshCache[meshId][primitiveIndex].LoadedMesh = mesh;
		}

		bool IsTexturePng(TextureInfo textureInfo)
		{
			if (textureInfo != null)
			{
				return GetTextureName(textureInfo.Index).EndsWith(".png");
			}
			else
			{
				return false;
			}
		}

		string GetShaderName(GLTF.Schema.AlphaMode alphaMode)
		{
			return alphaMode == AlphaMode.MASK ? CustomAlphaTestShaderName : CustomShaderName;
		}
		Material GetMaterial(GLTF.Schema.AlphaMode alphaMode, bool unlit)
        {
            if (unlit)
            {
                return alphaMode == AlphaMode.MASK ? CustomAlphaTestMaterialUnlit : CustomMaterialUnlit;
            }
            else
            {
                return alphaMode == AlphaMode.MASK ? CustomAlphaTestMaterial : CustomMaterial;
            }
        }

        class SpecGlossMapWithMaterial : SpecGlossMap
        {
            public SpecGlossMapWithMaterial(Material m, int MaxLOD = 1000) : base(m, MaxLOD) { }

            public override IUniformMap Clone()
            {
                var copy = new SpecGlossMapWithMaterial(new Material(_material));
                base.Copy(copy);
                return copy;
            }
        }
        class MetalRoughMapWithMaterial : MetalRoughMap
        {
            public MetalRoughMapWithMaterial(Material m, int MaxLOD = 1000) : base(m, MaxLOD) { }

            public override IUniformMap Clone()
            {
                var copy = new MetalRoughMapWithMaterial(new Material(_material));
                base.Copy(copy);
                return copy;
            }
        }
        protected virtual async Task ConstructMaterial(GLTFMaterial def, int materialIndex)
		{
			IUniformMap mapper;
			const string specGlossExtName = KHR_materials_pbrSpecularGlossinessExtensionFactory.EXTENSION_NAME;

            bool unlit = def.Extensions != null && def.Extensions.ContainsKey("KHR_materials_unlit");
			if (_gltfRoot.ExtensionsUsed != null && _gltfRoot.ExtensionsUsed.Contains(specGlossExtName)
				&& def.Extensions != null && def.Extensions.ContainsKey(specGlossExtName))
			{
				// If AlphaMode is not set, try to use the texture to decide if its MASK or OPAQUE
				if (def.AlphaMode == AlphaMode.NOT_SET)
				{
					var specGloss = def.Extensions[specGlossExtName] as KHR_materials_pbrSpecularGlossinessExtension;
					def.AlphaMode = IsTexturePng(specGloss?.DiffuseTexture) ? AlphaMode.MASK : AlphaMode.OPAQUE;
				}

                Material mat = GetMaterial(def.AlphaMode, unlit);
                if (mat != null)
                {
                    mapper = new SpecGlossMapWithMaterial(new Material(mat), MaximumLod);
                }
                else
                {
                    // Pick the shader based on AlphaMode
                    string shaderName = GetShaderName(def.AlphaMode);

                    if (!string.IsNullOrEmpty(shaderName))
                    {
                        mapper = new SpecGlossMap(shaderName, MaximumLod);
                    }
                    else
                    {
                        mapper = new SpecGlossMap(MaximumLod);
                    }
                }
			}
			else
			{
				// If AlphaMode is not set, try to use the texture to decide if its MASK or OPAQUE
				if (def.AlphaMode == AlphaMode.NOT_SET)
				{
					def.AlphaMode = IsTexturePng(def.PbrMetallicRoughness?.BaseColorTexture) ? AlphaMode.MASK : AlphaMode.OPAQUE;
				}

                Material mat = GetMaterial(def.AlphaMode, unlit);
                if (mat != null)
                {
                    mapper = new MetalRoughMapWithMaterial(new Material(mat), MaximumLod);
                }
                else
                {
                    // Pick the shader based on AlphaMode
                    string shaderName = GetShaderName(def.AlphaMode);

                    if (!string.IsNullOrEmpty(shaderName))
                    {
                        mapper = new MetalRoughMap(shaderName, MaximumLod);
                    }
                    else
                    {
                        mapper = new MetalRoughMap(MaximumLod);
                    }
                }
			}

			mapper.Material.name = def.Name;
			mapper.AlphaMode = def.AlphaMode;
			mapper.DoubleSided = def.DoubleSided;

			var mrMapper = mapper as IMetalRoughUniformMap;
			if (def.PbrMetallicRoughness != null && mrMapper != null)
			{
				var pbr = def.PbrMetallicRoughness;

				mrMapper.BaseColorFactor = pbr.BaseColorFactor.ToUnityColorRaw();

				if (pbr.BaseColorTexture != null)
				{
					TextureId textureId = pbr.BaseColorTexture.Index;
					await ConstructTexture(textureId.Value, textureId.Id, !KeepCPUCopyOfTexture, false);
                    {
                        mrMapper.BaseColorTexture = _assetCache.TextureCache[textureId.Id].Texture;
                        mrMapper.BaseColorTexCoord = pbr.BaseColorTexture.TexCoord;

                        var ext = GetTextureTransform(pbr.BaseColorTexture);
                        if (ext != null)
                        {
                            mrMapper.BaseColorXOffset = ext.Offset.ToUnityVector2Raw();
                            mrMapper.BaseColorXRotation = ext.Rotation;
                            mrMapper.BaseColorXScale = ext.Scale.ToUnityVector2Raw();
                            mrMapper.BaseColorXTexCoord = ext.TexCoord;
                        }
                    }
                }

                mrMapper.MetallicFactor = pbr.MetallicFactor;

				if (pbr.MetallicRoughnessTexture != null)
				{
					TextureId textureId = pbr.MetallicRoughnessTexture.Index;
					await ConstructTexture(textureId.Value, textureId.Id, !KeepCPUCopyOfTexture, true);
                    {
                        mrMapper.MetallicRoughnessTexture = _assetCache.TextureCache[textureId.Id].Texture;
                        mrMapper.MetallicRoughnessTexCoord = pbr.MetallicRoughnessTexture.TexCoord;

                        var ext = GetTextureTransform(pbr.MetallicRoughnessTexture);
                        if (ext != null)
                        {
                            mrMapper.MetallicRoughnessXOffset = ext.Offset.ToUnityVector2Raw();
                            mrMapper.MetallicRoughnessXRotation = ext.Rotation;
                            mrMapper.MetallicRoughnessXScale = ext.Scale.ToUnityVector2Raw();
                            mrMapper.MetallicRoughnessXTexCoord = ext.TexCoord;
                        }
                    }
				}

				mrMapper.RoughnessFactor = pbr.RoughnessFactor;
			}

			var sgMapper = mapper as ISpecGlossUniformMap;
            if (sgMapper != null && def.Extensions.ContainsKey(specGlossExtName))
			{
				var specGloss = def.Extensions[specGlossExtName] as KHR_materials_pbrSpecularGlossinessExtension;

				sgMapper.DiffuseFactor = specGloss.DiffuseFactor.ToUnityColorRaw();

				if (specGloss.DiffuseTexture != null)
				{
					TextureId textureId = specGloss.DiffuseTexture.Index;
					await ConstructTexture(textureId.Value, textureId.Id, !KeepCPUCopyOfTexture, false);
					sgMapper.DiffuseTexture = _assetCache.TextureCache[textureId.Id].Texture;
					sgMapper.DiffuseTexCoord = specGloss.DiffuseTexture.TexCoord;

					var ext = GetTextureTransform(specGloss.DiffuseTexture);
					if (ext != null)
					{
						sgMapper.DiffuseXOffset = ext.Offset.ToUnityVector2Raw();
						sgMapper.DiffuseXRotation = ext.Rotation;
						sgMapper.DiffuseXScale = ext.Scale.ToUnityVector2Raw();
						sgMapper.DiffuseXTexCoord = ext.TexCoord;
					}
				}

				sgMapper.SpecularFactor = specGloss.SpecularFactor.ToUnityVector3Raw();
				sgMapper.GlossinessFactor = specGloss.GlossinessFactor;

				if (specGloss.SpecularGlossinessTexture != null)
				{
					TextureId textureId = specGloss.SpecularGlossinessTexture.Index;
					await ConstructTexture(textureId.Value, textureId.Id, !KeepCPUCopyOfTexture, false);
					sgMapper.SpecularGlossinessTexture = _assetCache.TextureCache[textureId.Id].Texture;

					var ext = GetTextureTransform(specGloss.SpecularGlossinessTexture);
					if (ext != null)
					{
						sgMapper.SpecularGlossinessXOffset = ext.Offset.ToUnityVector2Raw();
						sgMapper.SpecularGlossinessXRotation = ext.Rotation;
						sgMapper.SpecularGlossinessXScale = ext.Scale.ToUnityVector2Raw();
						sgMapper.SpecularGlossinessXTexCoord = ext.TexCoord;
					}
				}
			}

			if (def.NormalTexture != null)
			{
				TextureId textureId = def.NormalTexture.Index;
				await ConstructTexture(textureId.Value, textureId.Id, !KeepCPUCopyOfTexture, true);
				mapper.NormalTexture = _assetCache.TextureCache[textureId.Id].Texture;
				mapper.NormalTexCoord = def.NormalTexture.TexCoord;
				mapper.NormalTexScale = def.NormalTexture.Scale;

				var ext = GetTextureTransform(def.NormalTexture);
				if (ext != null)
				{
					mapper.NormalXOffset = ext.Offset.ToUnityVector2Raw();
					mapper.NormalXRotation = ext.Rotation;
					mapper.NormalXScale = ext.Scale.ToUnityVector2Raw();
					mapper.NormalXTexCoord = ext.TexCoord;
				}
			}

			if (def.OcclusionTexture != null)
			{
				mapper.OcclusionTexStrength = def.OcclusionTexture.Strength;
				TextureId textureId = def.OcclusionTexture.Index;
				await ConstructTexture(textureId.Value, textureId.Id, !KeepCPUCopyOfTexture, true);
				mapper.OcclusionTexture = _assetCache.TextureCache[textureId.Id].Texture;

				var ext = GetTextureTransform(def.OcclusionTexture);
				if (ext != null)
				{
					mapper.OcclusionXOffset = ext.Offset.ToUnityVector2Raw();
					mapper.OcclusionXRotation = ext.Rotation;
					mapper.OcclusionXScale = ext.Scale.ToUnityVector2Raw();
					mapper.OcclusionXTexCoord = ext.TexCoord;
				}
			}

			if (def.EmissiveTexture != null)
			{
				TextureId textureId = def.EmissiveTexture.Index;
				await ConstructTexture(textureId.Value, textureId.Id, !KeepCPUCopyOfTexture, false);
				mapper.EmissiveTexture = _assetCache.TextureCache[textureId.Id].Texture;
				mapper.EmissiveTexCoord = def.EmissiveTexture.TexCoord;

				var ext = GetTextureTransform(def.EmissiveTexture);
				if (ext != null)
				{
					mapper.EmissiveXOffset = ext.Offset.ToUnityVector2Raw();
					mapper.EmissiveXRotation = ext.Rotation;
					mapper.EmissiveXScale = ext.Scale.ToUnityVector2Raw();
					mapper.EmissiveXTexCoord = ext.TexCoord;
				}
			    mapper.EmissiveFactor = def.EmissiveFactor.ToUnityColorRaw();
			}

			var vertColorMapper = mapper.Clone();

			vertColorMapper.VertexColorsEnabled = true;

			MaterialCacheData materialWrapper = new MaterialCacheData
			{
				UnityMaterial = mapper.Material,
				UnityMaterialWithVertexColor = vertColorMapper.Material,
				GLTFMaterial = def
			};

			if (materialIndex >= 0)
			{
				_assetCache.MaterialCache[materialIndex] = materialWrapper;
			}
			else
			{
				_defaultLoadedMaterial = materialWrapper;
			}
		}


		protected virtual int GetTextureSourceId(GLTFTexture texture)
		{
			return texture.Source.Id;
		}
/*
		/// <summary>
		/// Creates a texture from a glTF texture
		/// </summary>
		/// <param name="texture">The texture to load</param>
		/// <param name="textureIndex">The index in the texture cache</param>
		/// <param name="markGpuOnly">Whether the texture is GPU only, instead of keeping a CPU copy</param>
		/// <param name="isLinear">Whether the texture is linear rather than sRGB</param>
		/// <returns>The loading task</returns>
		public virtual async Task LoadTextureAsync(GLTFTexture texture, int textureIndex, bool markGpuOnly, bool isLinear)
		{
			try
			{
				lock (this)
				{
					if (_isRunning)
					{
						throw new GLTFLoadException("Cannot CreateTexture while GLTFSceneImporter is already running");
					}

					_isRunning = true;
				}

				if (_gltfRoot == null)
				{
					await LoadJson(_gltfFileName);
				}

				if (_assetCache == null)
				{
					_assetCache = new AssetCache(_gltfRoot);
				}

				await ConstructImageBuffer(texture, textureIndex);
				await ConstructTexture(texture, textureIndex, markGpuOnly, isLinear);
			}
			finally
			{
				lock (this)
				{
					_isRunning = false;
				}
			}
		}

		public virtual Task LoadTextureAsync(GLTFTexture texture, int textureIndex, bool isLinear)
		{
			return LoadTextureAsync(texture, textureIndex, !KeepCPUCopyOfTexture, isLinear);
		}
*/
		/// <summary>
		/// Gets texture that has been loaded from CreateTexture
		/// </summary>
		/// <param name="textureIndex">The texture to get</param>
		/// <returns>Created texture</returns>
		public virtual Texture GetTexture(int textureIndex)
		{
			if (_assetCache == null)
			{
				throw new GLTFLoadException("Asset cache needs initialized before calling GetTexture");
			}

			if (_assetCache.TextureCache[textureIndex] == null)
			{
				return null;
			}

			return _assetCache.TextureCache[textureIndex].Texture;
		}

		protected virtual async Task ConstructTexture(GLTFTexture texture, int textureIndex,
			bool markGpuOnly, bool isLinear)
		{
			if (_assetCache.TextureCache[textureIndex] == null)
            {
                _assetCache.TextureCache[textureIndex] = new TextureCacheData
                {
                    TextureDefinition = null
                };
            }
			if (_assetCache.TextureCache[textureIndex].Texture == null)
			{
				int sourceId = GetTextureSourceId(texture);
				GLTFImage image = _gltfRoot.Images[sourceId];
				await ConstructImage(texture, textureIndex, image, sourceId, markGpuOnly, isLinear);

				var source = _assetCache.ImageCache[sourceId];
				FilterMode desiredFilterMode;
				TextureWrapMode desiredWrapMode;

				if (texture.Sampler != null)
				{
					var sampler = texture.Sampler.Value;
					switch (sampler.MagFilter)
					{
						case MagFilterMode.None:
                        case MagFilterMode.Nearest:
							desiredFilterMode = FilterMode.Point;
							break;
						case MagFilterMode.Linear:
                            desiredFilterMode = FilterMode.Bilinear;
							break;
						default:
							Debug.LogWarning("Unsupported Sampler.MinFilter: " + sampler.MinFilter);
							desiredFilterMode = FilterMode.Trilinear;
							break;
					}

					switch (sampler.WrapS)
					{
						case GLTF.Schema.WrapMode.ClampToEdge:
							desiredWrapMode = TextureWrapMode.Clamp;
							break;
						case GLTF.Schema.WrapMode.Repeat:
							desiredWrapMode = TextureWrapMode.Repeat;
							break;
						case GLTF.Schema.WrapMode.MirroredRepeat:
							desiredWrapMode = TextureWrapMode.Mirror;
							break;
						default:
							Debug.LogWarning("Unsupported Sampler.WrapS: " + sampler.WrapS);
							desiredWrapMode = TextureWrapMode.Repeat;
							break;
					}
				}
				else
				{
					desiredFilterMode = FilterMode.Trilinear;
					desiredWrapMode = TextureWrapMode.Repeat;
				}

				var matchSamplerState = source.filterMode == desiredFilterMode && source.wrapMode == desiredWrapMode;
				if (matchSamplerState || markGpuOnly)
				{
                    source.wrapMode = desiredWrapMode;
                    source.filterMode = desiredFilterMode;
					_assetCache.TextureCache[textureIndex].Texture = source;

					if (!matchSamplerState)
					{
						//Debug.LogWarning($"Ignoring sampler; filter mode: source {source.filterMode}, desired {desiredFilterMode}; wrap mode: source {source.wrapMode}, desired {desiredWrapMode}");
					}
				}
				else
				{
					var unityTexture = Object.Instantiate(source);
					unityTexture.filterMode = desiredFilterMode;
					unityTexture.wrapMode = desiredWrapMode;

					_assetCache.TextureCache[textureIndex].Texture = unityTexture;
				}
			}
		}

		protected virtual BufferCacheData ConstructBufferFromGLB(int bufferIndex)
		{
			GLTFParser.SeekToBinaryChunk(_gltfStream.Stream, bufferIndex, _gltfStream.StartPosition);  // sets stream to correct start position
			return new BufferCacheData
			{
				Stream = _gltfStream.Stream,
				ChunkOffset = (uint)_gltfStream.Stream.Position
			};
		}

		protected virtual ExtTextureTransformExtension GetTextureTransform(TextureInfo def)
		{
			IExtension extension;
			if (_gltfRoot.ExtensionsUsed != null &&
				_gltfRoot.ExtensionsUsed.Contains(ExtTextureTransformExtensionFactory.EXTENSION_NAME) &&
				def.Extensions != null &&
				def.Extensions.TryGetValue(ExtTextureTransformExtensionFactory.EXTENSION_NAME, out extension))
			{
				return (ExtTextureTransformExtension)extension;
			}
			else return null;
		}


		/// <summary>
		///	 Get the absolute path to a gltf uri reference.
		/// </summary>
		/// <param name="gltfPath">The path to the gltf file</param>
		/// <returns>A path without the filename or extension</returns>
		protected static string AbsoluteUriPath(string gltfPath)
		{
			var uri = new Uri(gltfPath);
			var partialPath = uri.AbsoluteUri.Remove(uri.AbsoluteUri.Length - uri.Query.Length - uri.Segments[uri.Segments.Length - 1].Length);
			return partialPath;
		}

		/// <summary>
		/// Get the absolute path a gltf file directory
		/// </summary>
		/// <param name="gltfPath">The path to the gltf file</param>
		/// <returns>A path without the filename or extension</returns>
		protected static string AbsoluteFilePath(string gltfPath)
		{
			var fileName = Path.GetFileName(gltfPath);
			var lastIndex = gltfPath.IndexOf(fileName);
			var partialPath = gltfPath.Substring(0, lastIndex);
			return partialPath;
		}

		/// <summary>
		/// Cleans up any undisposed streams after loading a scene or a node.
		/// </summary>
		private void Cleanup()
		{
			_assetCache.Dispose();
			_assetCache = null;
		}



        
    }
}
