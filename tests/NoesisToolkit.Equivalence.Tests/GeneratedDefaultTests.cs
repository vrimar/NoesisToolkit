using Noesis;
using NoesisToolkit.Equivalence.Tests.Elsewhere;
using NoesisToolkit.Mvvm;

namespace NoesisToolkit.Equivalence.Tests
{
    public sealed partial class Loose : Control
    {
        [DependencyProperty(0)]
        internal partial double Spin { get; private set; }

        [DependencyProperty(Heading.West, nameof(Turns.OnTurned))]
        public partial Heading Way { get; set; }
    }

    [NotInParallel("Noesis")]
    public sealed class GeneratedDefaultTests
    {
        [Test]
        public async Task A_default_written_loosely_registers_as_the_property_s_own_type()
        {
            NoesisRuntime.Start();

            var loose = new Loose();

            await Assert.That(loose.Spin).IsEqualTo(0.0);
            await Assert.That(loose.Way).IsEqualTo(Heading.West);

            NoesisRuntime.Show(loose);
            var turns = Turns.Count;
            loose.Way = Heading.North;
            await Assert.That(Turns.Count).IsEqualTo(turns + 1);
        }
    }
}

namespace NoesisToolkit.Equivalence.Tests.Elsewhere
{
    public enum Heading
    {
        North,
        West,
    }

    public static class Turns
    {
        public static int Count;

        public static void OnTurned(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
            Count++;
    }
}
