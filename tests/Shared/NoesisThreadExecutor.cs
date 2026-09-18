using System.Collections.Concurrent;
using NoesisToolkit.Tests.Shared;
using TUnit.Core;
using TUnit.Core.Executors;

[assembly: TestExecutor(typeof(NoesisThreadExecutor))]
[assembly: HookExecutor(typeof(NoesisThreadExecutor))]

namespace NoesisToolkit.Tests.Shared;

/// <summary>
/// Runs every test and hook on one thread for the process. Noesis records the thread that owns each
/// object and refuses access from any other, so a fixture built under one test and touched under the
/// next logs "a different thread owns it" and, worse, reads native state that is not ours to read —
/// <c>[NotInParallel]</c> serialises tests but still hands each one whichever pool thread is free.
/// </summary>
public sealed class NoesisThreadExecutor : GenericAbstractExecutor
{
    static readonly BlockingCollection<Action> Work = new();
    static readonly Thread Pump = Start();

    public static int PumpThreadId => Pump.ManagedThreadId;

    static Thread Start()
    {
        var thread = new Thread(Drain) { IsBackground = true, Name = "Noesis" };
        thread.Start();
        return thread;
    }

    static void Drain()
    {
        // Without it an `await` inside a test body resumes on the pool, off the owning thread again.
        SynchronizationContext.SetSynchronizationContext(new PumpContext());

        foreach (var work in Work.GetConsumingEnumerable())
            work();
    }

    protected override ValueTask ExecuteAsync(Func<ValueTask> action)
    {
        if (Thread.CurrentThread == Pump)
            return action();

        var completion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        Work.Add(() =>
        {
            ValueTask pending;
            try
            {
                pending = action();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
                return;
            }

            if (pending.IsCompletedSuccessfully)
            {
                completion.SetResult();
                return;
            }

            // Never blocks the pump: a continuation posted back here would have nothing to run it.
            pending
                .AsTask()
                .ContinueWith(
                    task =>
                    {
                        if (task.IsFaulted)
                            completion.SetException(task.Exception!.InnerExceptions);
                        else if (task.IsCanceled)
                            completion.SetCanceled();
                        else
                            completion.SetResult();
                    },
                    TaskContinuationOptions.ExecuteSynchronously
                );
        });

        return new ValueTask(completion.Task);
    }

    sealed class PumpContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) =>
            Work.Add(() => callback(state));

        public override void Send(SendOrPostCallback callback, object? state)
        {
            if (Thread.CurrentThread == Pump)
                callback(state);
            else
                throw new InvalidOperationException("Send would deadlock the Noesis pump thread.");
        }
    }
}
