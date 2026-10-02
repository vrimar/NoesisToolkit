using System.Runtime.CompilerServices;
using Noesis;
using Noesis.Interactivity;
using NoesisToolkit.Mvvm;

namespace NoesisToolkit.Equivalence.Tests;

public sealed class AtHost : ContentControl { }

public sealed class AtBehavior : Behavior<FrameworkElement>
{
    public static bool ReadNative;
    public static int Detaches;
    public static int ReadElement;

    protected override void OnDetaching()
    {
        Detaches++;
        if ((ReadNative ? AssociatedObject : AttachedObjects.AssociatedObjectOf(this)) is not null)
            ReadElement++;
    }
}

[NotInParallel("Noesis")]
public sealed class AttachedObjectTeardownTests
{
    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Update")]
    static extern void ReleasePending(
        [UnsafeAccessorType("Noesis.Extend, Noesis.GUI")] object? extend
    );

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void ShowAndDrop()
    {
        var host = new AtHost { Width = 40, Height = 40 };
        Interaction.GetBehaviors(host).Add(new AtBehavior());
        NoesisRuntime.Show(host);
    }

    static (List<string> Errors, int Detaches, int ReadElement) TearDown(bool readNative)
    {
        var errors = new List<string>();
        Log.SetLogCallback(
            (level, _, message) =>
            {
                if (level == LogLevel.Error)
                    errors.Add(message);
            }
        );
        Error.SetUnhandledCallback(exception => errors.Add(exception.ToString()));

        try
        {
            AtBehavior.ReadNative = readNative;
            AtBehavior.Detaches = 0;
            AtBehavior.ReadElement = 0;
            ShowAndDrop();
            for (var i = 0; i < 4; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                ReleasePending(null);
            }
        }
        finally
        {
            Log.SetLogCallback(null);
            Error.SetUnhandledCallback(null);
        }

        return (errors, AtBehavior.Detaches, AtBehavior.ReadElement);
    }

    [Test]
    public async Task A_behavior_detached_by_a_teardown_reads_its_element_without_an_error()
    {
        NoesisRuntime.Start();

        var native = TearDown(readNative: true);
        var toolkit = TearDown(readNative: false);

        await Assert.That(native.Detaches).IsGreaterThan(0);
        await Assert.That(native.Errors).Contains("Extend already removed");
        await Assert.That(toolkit.Detaches).IsGreaterThan(0);
        await Assert.That(toolkit.ReadElement).IsEqualTo(0);
        await Assert.That(toolkit.Errors).IsEmpty();
    }

    [Test]
    public async Task An_attached_behavior_reads_the_element_it_is_attached_to()
    {
        NoesisRuntime.Start();

        var managed = new AtHost();
        var onManaged = new AtBehavior();
        Interaction.GetBehaviors(managed).Add(onManaged);
        var native = new Border();
        var onNative = new AtBehavior();
        Interaction.GetBehaviors(native).Add(onNative);
        var view = NoesisRuntime.Show(managed, native);

        await Assert.That(AttachedObjects.AssociatedObjectOf(onManaged)).IsSameReferenceAs(managed);
        await Assert.That(AttachedObjects.AssociatedObjectOf(onNative)).IsSameReferenceAs(native);
        await Assert.That(AttachedObjects.AssociatedObjectOf(new AtBehavior())).IsNull();
        GC.KeepAlive(view);
    }
}
