using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
namespace UnityGLTF
{

    public class AsyncCoroutineHelper : MonoBehaviour
	{
		public float BudgetPerFrameInSeconds = 0.01f;

        private WaitForEndOfFrame _waitForEndOfFrame = new WaitForEndOfFrame();

        public bool verbose = false;
        public float frameOverrunTimeReport = .1f;
        private float _timeout;

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
                _timeout = Time.realtimeSinceStartup + BudgetPerFrameInSeconds;
            }
		}

        private void Start()
        {
            _timeout = Time.realtimeSinceStartup + BudgetPerFrameInSeconds;

            StartCoroutine(ResetFrameTimeout());
        }

        private IEnumerator ResetFrameTimeout()
        {
            while (true)
            {
                yield return _waitForEndOfFrame;
                _timeout = Time.realtimeSinceStartup + BudgetPerFrameInSeconds;
            }
        }
    }
}
