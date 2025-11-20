using System;
using System.Threading.Tasks;

namespace KioskApp
{
    /// <summary>
    /// Extension methods for DispatcherQueue
    /// </summary>
    public static class DispatcherQueueExtensions
    {
        /// <summary>
        /// Enqueue a callback asynchronously and wait for it to complete
        /// </summary>
        public static Task EnqueueAsync(this Microsoft.UI.Dispatching.DispatcherQueue dispatcher, Action callback)
        {
            var tcs = new TaskCompletionSource<bool>();
            if (!dispatcher.TryEnqueue(() =>
            {
                try
                {
                    callback();
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }))
            {
                tcs.SetException(new InvalidOperationException("Failed to enqueue to dispatcher"));
            }
            return tcs.Task;
        }
    }
}
