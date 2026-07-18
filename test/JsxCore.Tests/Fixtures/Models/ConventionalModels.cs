// Fixtures for the type-source tests, laid out the way a real MVC application would be: view
// models in a "Models" namespace, other things elsewhere.

namespace JsxCore.Tests.Conventional.Models;

/// <summary>A view model found purely by the namespace convention.</summary>
public sealed record OrderModel(string Reference, decimal Total);

/// <summary>A delegate, which cannot describe data.</summary>
public delegate void SomeDelegate(int value);

/// <summary>An attribute, which is not a view model.</summary>
public sealed class SomeAttribute : System.Attribute;

/// <summary>An exception, which is not a view model.</summary>
public sealed class SomeException : System.Exception;

/// <summary>A static helper, which carries no data.</summary>
public static class StaticHelpers
{
    /// <summary>Does nothing useful.</summary>
    public static int Double(int value) => value * 2;
}

/// <summary>Not public, so scanning should skip it.</summary>
internal sealed record InternalModel(string Name);
