using System;

namespace NoesisToolkit.Mvvm;

static class Guard
{
    public static T NotNull<T>(T? value, string name)
        where T : class => value ?? throw new ArgumentNullException(name);
}
