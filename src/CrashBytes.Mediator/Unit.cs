namespace CrashBytes.Mediator;

/// <summary>
/// Represents a void return for requests that produce no value. Used as the
/// response type for <see cref="IRequest"/>.
/// </summary>
public readonly struct Unit : IEquatable<Unit>
{
    /// <summary>The single value of <see cref="Unit"/>.</summary>
    public static readonly Unit Value = default;

    /// <summary>A pre-completed task that returns <see cref="Value"/>.</summary>
    public static readonly Task<Unit> Task = System.Threading.Tasks.Task.FromResult(Value);

    /// <summary>All <see cref="Unit"/> values are equal.</summary>
    public bool Equals(Unit other) => true;

    /// <summary>All <see cref="Unit"/> values are equal; non-Unit values are never equal.</summary>
    public override bool Equals(object? obj) => obj is Unit;

    /// <summary>Always returns 0 — there is only one value.</summary>
    public override int GetHashCode() => 0;

    /// <summary>Returns the canonical text representation, "()".</summary>
    public override string ToString() => "()";

    /// <summary>Equality operator. Always returns <c>true</c>.</summary>
    public static bool operator ==(Unit left, Unit right) => true;

    /// <summary>Inequality operator. Always returns <c>false</c>.</summary>
    public static bool operator !=(Unit left, Unit right) => false;
}
