// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using AngleSharp.Html;
using AngleSharp.Html.Dom;

namespace ReflectiveForms.Core.Utilities
{
    internal static class HtmlUtility
    {
        internal static bool ConvertHtmlDocumentToHtmlString(
            IHtmlDocument document,
            out string? htmlResult,
            Action<string>? errorMessageAction)
        {
            try
            {
                using var writer = new StringWriter();
                document.ToHtml(writer, new PrettyMarkupFormatter());
                htmlResult = writer.ToString();
            }
            catch (Exception e)
            {
                errorMessageAction?.Invoke($"ConvertHTMLDocumentToHTMLString has failed with: {e.Message}, trace: {e.StackTrace}");
                htmlResult = null;
                return false;
            }
            return true;
        }
    }
}
