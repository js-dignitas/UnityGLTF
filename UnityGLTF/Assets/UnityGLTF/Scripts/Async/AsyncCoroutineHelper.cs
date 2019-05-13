using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
namespace UnityGLTF
{
	public interface IAsyncCoroutineHelper
	{
		Task RunAsTask(IEnumerator coroutine, string name);
		Task YieldOnTimeout(string msg = null);
	}

	public class AsyncCoroutineHelper : MonoBehaviour, IAsyncCoroutineHelper
	{
		public float BudgetPerFrameInSeconds = 0.01f;

		private Queue<CoroutineInfo> _actions = new Queue<CoroutineInfo>();
		private WaitForEndOfFrame _waitForEndOfFrame = new WaitForEndOfFrame();
		private float _timeout;
        public bool verbose = false;
        public float frameOverrunTimeReport = .1f;

		public Task RunAsTask(IEnumerator coroutine, string name)
		{
			TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
			lock (_actions)
			{
				_actions.Enqueue(
					new CoroutineInfo
					{
						Coroutine = coroutine,
						Tcs = tcs,
						Name = name
					}
				);
			}

			return tcs.Task;
		}

		public async Task YieldOnTimeout(string msg = null)
		{
			if (Time.realtimeSinceStartup > _timeout)
			{
                if (verbose)
                {
                    float overrun = (Time.realtimeSinceStartup - _timeout);
                    if (overrun > frameOverrunTimeReport)
                    {
                        Debug.Log("Frame overrun " + overrun + (msg == null ? "" : " at " + msg));
                    }
                }
                int frame = Time.frameCount;
                //await RunAsTask(EmptyYieldEnum(), nameof(EmptyYieldEnum));
                await Task.Delay(TimeSpan.FromMilliseconds(1));
			}
		}

		private void Start()
		{
			_timeout = Time.realtimeSinceStartup + BudgetPerFrameInSeconds;
		}

		private void Update()
		{
			StartCoroutine(ResetFrameTimeout());

			CoroutineInfo ? coroutineInfo = null;

			lock (_actions)
			{
				if (_actions.Count > 0)
				{
					coroutineInfo = _actions.Dequeue();
				}
			}

			if (coroutineInfo != null)
			{
				StartCoroutine(CallMethodOnMainThread(coroutineInfo.Value));
			}
		}

		private IEnumerator CallMethodOnMainThread(CoroutineInfo coroutineInfo)
		{
			yield return coroutineInfo.Coroutine;
			coroutineInfo.Tcs.SetResult(true);
		}

		private IEnumerator EmptyYieldEnum()
		{
			yield break;
		}

		private IEnumerator ResetFrameTimeout()
		{
			yield return _waitForEndOfFrame;
			_timeout = Time.realtimeSinceStartup + BudgetPerFrameInSeconds;
		}

		private struct CoroutineInfo
		{
			public IEnumerator Coroutine;
			public TaskCompletionSource<bool> Tcs;
			public string Name;
		}
	}
}
