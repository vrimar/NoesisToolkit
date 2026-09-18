namespace NoesisToolkit.Tests.Shared;

public class NoesisThreadPinningTests
{
    [Test]
    public async Task Every_test_runs_on_the_thread_that_owns_the_Noesis_objects()
    {
        await Assert
            .That(Environment.CurrentManagedThreadId)
            .IsEqualTo(NoesisThreadExecutor.PumpThreadId)
            .Because(
                "the assembly-level TestExecutor has to put every test on the one pump thread"
            );
    }
}
