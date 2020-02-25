using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
#if WINDOWS_UWP_IGNORE_THIS
using Windows.Web.Http;
using Windows.Security;
using Windows.Storage.Streams;
#else
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
#endif
using System.Threading;
using System.Threading.Tasks;

namespace UnityGLTF.Loader
{
    public class WebRequestLoader : ILoader
    {
        Queue<MemoryStream> streamPool = new Queue<MemoryStream>();
        HashSet<MemoryStream> streamSet = new HashSet<MemoryStream>();
        HashSet<MemoryStream> created = new HashSet<MemoryStream>();
        //public Stream LoadedStream { get { return stream; } }

        public bool HasSyncLoadMethod => false;

        private readonly HttpClient httpClient = new HttpClient();
        private Uri baseAddress;

        public string BaseAddress
        {
            set { baseAddress = new Uri(value); }
            get { return baseAddress.ToString(); }
        }

        public bool Verbose { set; get; }

        public WebRequestLoader(string rootUri)
        {
#if !WINDOWS_UWP_IGNORE_THIS
            ServicePointManager.ServerCertificateValidationCallback = ValidateServerCertificate;
#endif
            BaseAddress = rootUri;

        }

        MemoryStream GetNewStream(int size)
        {
            if (streamPool.Count > 0)
            {
                MemoryStream stream = streamPool.Dequeue();
                streamSet.Remove(stream);
                stream.Seek(0, SeekOrigin.Begin);
                stream.SetLength(0);
                if (stream.Capacity < size)
                {
                    stream.Capacity = size;
                }
                return stream;
            }
            else
            {
                MemoryStream stream = new MemoryStream(size);
                created.Add(stream);
                return stream;
            }
        }
        public bool GiveBack(Stream stream)
        {
            if (stream != null && stream is MemoryStream)
            {
                var mStream = (MemoryStream)stream;
                if (!streamSet.Contains(mStream) && created.Contains(mStream))
                {
                    streamPool.Enqueue(mStream);
                    streamSet.Add(mStream);
                    //UnityEngine.Debug.Log($"Giving back a stream, {mStream.Capacity}, streamSet:{streamSet.Count}, created:{created.Count}");
                    return true;
                }
            }
            return false;
        }
        public void Clear()
        {
            try
            {
                while(streamPool.Count > 0)
                {
                    streamPool.Dequeue().Dispose();
                }
                streamSet.Clear();
                created.Clear();
            }
            catch(Exception e)
            {
                //
            }
        }

        public Uri FullPath(string file)
        {
            return new Uri(baseAddress, file);
        }

        async Task<HttpResponseMessage> GetFile(string path)
        {
            HttpResponseMessage response;
            try
            {
#if WINDOWS_UWP_IGNORE_THIS
                response = await httpClient.GetAsync(new Uri(baseAddress, updatedPath));
#else
                var tokenSource = new CancellationTokenSource(30000);
                response = await httpClient.GetAsync(new Uri(baseAddress, path), tokenSource.Token);
#endif
            }
            catch (TaskCanceledException)
            {
#if WINDOWS_UWP_IGNORE_THIS
                throw new Exception("Connection timeout");
#else
                throw new HttpRequestException("Connection timeout");
#endif
            }
            return response;
        }
        public async Task<Stream> LoadStream(string gltfFilePath)
        {
            if (gltfFilePath == null)
            {
                throw new ArgumentNullException(nameof(gltfFilePath));
            }

            if (Verbose)
            {
                UnityEngine.Debug.Log("Downloading " + gltfFilePath);
            }

            MemoryStream stream = null;

            var response = await GetFile(gltfFilePath);

            if (response.IsSuccessStatusCode)
            {
                // HACK: Download the whole file before returning the stream
                // Ideally the parsers would wait for data to be available, but they don't.
                int size = (int?)response.Content.Headers.ContentLength + 1024 ?? 5000;

                stream = GetNewStream(size);
                if (Verbose)
                {
                    UnityEngine.Debug.Log("Downloaded " + gltfFilePath + ", size " + response.Content.Headers.ContentLength);
                }
                int startCap = stream.Capacity;
#if WINDOWS_UWP_IGNORE_THIS
            await response.Content.WriteToStreamAsync(LoadedStream.AsOutputStream());
#else
                await response.Content.CopyToAsync(stream);
#endif
            }
            response.Dispose();
            return stream;
        }

        public Stream LoadStreamSync(string jsonFilePath)
        {
            throw new NotImplementedException();
        }

#if !WINDOWS_UWP_IGNORE_THIS
        // enables HTTPS support
        // https://answers.unity.com/questions/50013/httpwebrequestgetrequeststream-https-certificate-e.html
        private static bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors errors)
        {
            bool isOk = true;
            // If there are errors in the certificate chain, look at each error to determine the cause.
            if (errors != SslPolicyErrors.None)
            {
                for (int i = 0; i<chain.ChainStatus.Length; i++)
                {
                    if (chain.ChainStatus[i].Status != X509ChainStatusFlags.RevocationStatusUnknown)
                    {
                        chain.ChainPolicy.RevocationFlag = X509RevocationFlag.EntireChain;
                        chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
                        chain.ChainPolicy.UrlRetrievalTimeout = new TimeSpan(0, 1, 0);
                        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllFlags;
                        bool chainIsValid = chain.Build((X509Certificate2)certificate);
                        if (!chainIsValid)
                        {
                            isOk = false;
                        }
                    }
                }
            }

            return isOk;
        }
#endif
    }
}
