// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;

namespace ReflectiveForms.Core.Utilities;

/// <summary>
/// An equality comparer that compares objects for reference equality.
/// </summary>
/// <typeparam name="T">The type of objects to compare.</typeparam>
internal sealed class ReferenceEqualityComparer<T> : IEqualityComparer<T>
    where T : class
{
    /// <summary>
    /// Gets the default instance of the
    /// <see cref="ReferenceEqualityComparer{T}"/> class.
    /// </summary>
    /// <value>A <see cref="ReferenceEqualityComparer"/> instance.</value>
    internal static ReferenceEqualityComparer<T> Instance { get; } = new ReferenceEqualityComparer<T>();

    /// <inheritdoc />
    public bool Equals(T? left, T? right)
    {
        return ReferenceEquals(left, right);
    }

    /// <inheritdoc />
    public int GetHashCode(T value)
    {
        return RuntimeHelpers.GetHashCode(value);
    }
}
