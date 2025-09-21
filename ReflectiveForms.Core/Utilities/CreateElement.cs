// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using CrossCloudKit.Utilities.Common;

namespace ReflectiveForms.Core.Utilities;

public class CreateElement(Func<Type, IElement?> typed, Func<string, IElement> nonTyped)
{
    private Func<Type, IElement?> Typed { get; set; } = typed;
    private Func<string, IElement> NonTyped { get; set; } = nonTyped;

    public T Invoke<T>() where T : IElement
    {
        return (T)(Typed(typeof(T)) ?? throw new InvalidOperationException($"Failed to create {typeof(T).Name}"));
    }

    public IElement Invoke(string tag)
    {
        return NonTyped(tag);
    }

    public static readonly Dictionary<IHtmlDocument, CreateElement> Cached = new(new ReferenceEqualityComparer<IHtmlDocument>());
}

public static class CreateElementExtensions
{
    public static CreateElement AsCreateElement(this IHtmlDocument? document)
    {
        ArgumentNullException.ThrowIfNull(document);

        CreateElement? result;

        lock (CreateElement.Cached)
        {
            if (CreateElement.Cached.TryGetValue(document, out result))
            {
                return result;
            }
        }

        var m = typeof(DocumentExtensions).GetMethod("CreateElement");
        result = new CreateElement(
            elementType => m == null ? null : (IElement)m.MakeGenericMethod(elementType).Invoke(null, [document]).NotNull(),
            document.CreateElement);

        lock (CreateElement.Cached)
        {
            CreateElement.Cached[document] = result;
        }

        return result;
    }
}
