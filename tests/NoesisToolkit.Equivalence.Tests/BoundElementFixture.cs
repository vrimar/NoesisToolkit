using Noesis;

namespace NoesisToolkit.Equivalence.Tests;

// Does not build in its constructor: the parsed side of the comparison loads this same type, which
// would otherwise re-enter the compiled build.
public partial class BoundElementFixture : UserControl { }
