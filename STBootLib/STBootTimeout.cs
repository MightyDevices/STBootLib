using System;
using System.Threading;
using System.Threading.Tasks;

namespace STBootLib;

/// <summary>
/// Boot timeout used for canceling.
/// </summary>
public static class STBootTimeout
{
    /// <summary>
    /// Executes a task with timeout.
    /// </summary>
    /// <typeparam name="T">The response type of the task.</typeparam>
    /// <param name="task">The task.</param>
    /// <param name="timeout">The timeout.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the result of the asynchronous operation.</returns>
    public static async Task<T> WithTimeout<T>(this Task<T> task, int timeout)
    {
        var cts = new CancellationTokenSource(timeout);
        var tcs = new TaskCompletionSource<bool>();

        using (cts.Token.Register(s => ((TaskCompletionSource<bool>)s).TrySetResult(true), tcs))
        {
            // timeout occurred? or task finished normally?
            if (task != await Task.WhenAny(task, tcs.Task))
            {
                throw new OperationCanceledException();
            }
        }

        return await task;
    }
}
