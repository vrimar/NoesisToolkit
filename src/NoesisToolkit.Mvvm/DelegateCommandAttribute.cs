using System;

namespace NoesisToolkit.Mvvm;

/// <summary>
/// Marks a method as the body of a bindable command. The generator adds a
/// <c>{MethodName}Command</c> property to the containing partial type, backed by
/// <see cref="DelegateCommand"/> for a <see langword="void"/> method or
/// <see cref="AsyncDelegateCommand"/> for one returning <see cref="System.Threading.Tasks.ValueTask"/>.
/// </summary>
/// <remarks>
/// The method may take at most one parameter, which becomes the command parameter. The containing
/// type must be <see langword="partial"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class DelegateCommandAttribute : Attribute { }
