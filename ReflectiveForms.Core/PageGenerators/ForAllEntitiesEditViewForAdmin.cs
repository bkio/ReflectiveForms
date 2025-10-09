// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Net;
using Newtonsoft.Json.Linq;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Utilities.Common;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Operation;
using ReflectiveForms.Core.Utilities;

namespace ReflectiveForms.Core.PageGenerators
{
    internal sealed class ForAllEntitiesEditViewForAdmin(
        string entityName,
        bool requesterUserSuperAdmin,
        bool canUserViewAnEntity,
        bool canUserEditAnEntity,
        bool canUserCreateAnEntity,
        bool canUserDeleteAnEntity,
        int showOnlyByAuthorId,
        List<string>? showOnlyByCategoryNamesNullable,
        List<string>? showOnlyByTagNamesNullable,
        string? sortByNullable)
    {
        private const string Template = """

                                        <html><body>
                                                <style>
                                                    td {
                                                        border-collapse: collapse;
                                                        width: 100%;
                                                    }
                                                    td:first-child {
                                                        padding: 1%;
                                                    }
                                                </style>
                                        </body></html>
                                        """;

        private IHtmlDocument? _fullDocument;
        private IHtmlElement? _containerElement;

        private Dictionary<int, EntityLockState>? _lockStatusForEntityType;

        public async Task<OperationResult<string>> GenerateAsync(CancellationToken cancellationToken)
        {
            var configuration = RfConfiguration.EntityNameToConfiguration[entityName];

            switch (configuration.EntityConfiguration.ShallSupportFrontendEdit)
            {
                case SupportsFrontendEdit.No:
                    return OperationResult<string>.Failure($"Entity type {entityName} is not a candidate to be viewed with an admin frontend.", HttpStatusCode.NotImplemented);
                case SupportsFrontendEdit.ForSuperAdminOnly
                    when !requesterUserSuperAdmin:
                    return OperationResult<string>.Failure($"Forbidden", HttpStatusCode.Forbidden);
                case SupportsFrontendEdit.ForAllAuthorized:
                default:
                    break;
            }

            var peekAllResult = await RfConfiguration.RepositoryService.PeekAllAsync(entityName, cancellationToken);
            if (!peekAllResult.IsSuccessful)
                return OperationResult<string>.Failure(peekAllResult.ErrorMessage, peekAllResult.StatusCode);
            var entities = peekAllResult.Data;

            _fullDocument = await new HtmlParser().ParseDocumentAsync(Template, cancellationToken);
            _containerElement = _fullDocument.CreateElement<IHtmlDivElement>();
            _fullDocument.Body?.AppendChild(_containerElement);

            var lockStatusResult = await EntityLockController.GetAllLockedAsync(entityName, cancellationToken);
            if (!lockStatusResult.IsSuccessful)
                return OperationResult<string>.Failure(lockStatusResult.ErrorMessage, lockStatusResult.StatusCode);
            _lockStatusForEntityType = lockStatusResult.Data.ToDictionary();

            var compileEntityResult = await Compile(entities);
            if (!compileEntityResult.IsSuccessful)
            {
                return OperationResult<string>.Failure($"Compilation failed for {entityName}. Error: {compileEntityResult.ErrorMessage}.", HttpStatusCode.InternalServerError);
            }

            var error = "";
            return HtmlUtility.ConvertHtmlDocumentToHtmlString(
                _fullDocument,
                out var compiled,
                err => error += err + Environment.NewLine)
                ? OperationResult<string>.Success(compiled.NotNull())
                : OperationResult<string>.Failure($"ConvertHTMLDocumentToHTMLString has failed for {entityName}. Error: {error}", HttpStatusCode.InternalServerError);
        }

        private Task<OperationResult<bool>> Compile(JArray entities)
        {
            if (_fullDocument == null || _containerElement == null)
                return Task.FromResult(OperationResult<bool>.Failure($"Internal error. Document/ContainerElement is null.", HttpStatusCode.InternalServerError));

            var x = _fullDocument.AsCreateElement();

            var (_, headerRow, content) = _containerElement
                .CreateRow(x)
                .CreateCol1OnRow(x)
                .CreateCardOnCol(x);

            if (canUserCreateAnEntity)
            {
                headerRow
                    .CreateColFitContentRightAlignedOnRow(x)
                    .AddClasses("mb-3")
                    .CreateButtonOnElement(x, "Add New", "fa-solid fa-circle-plus")
                    .SetAttribute("onclick", "window.add_new_element(this);");
            }

            var (_, _, head, body) = content.CreateTableOnCard(x);

            var tmpDictionary = new Dictionary<int, ElementNode>();
            foreach (var entityToken in entities)
            {
                var entityObj = (JObject)entityToken;
                var id = (int)entityObj[EntityModelAttributes.Id].NotNull();

                if (!entityObj.TryGetTypedValue(EntityModelAttributes.Author, out string? authorName) || authorName.NotNull().Length == 0)
                    authorName = null;
                if (!entityObj.TryGetTypedValue($"{EntityModelAttributes.Author}_{EntityModelAttributes.Id}", out int authorId) || authorId < 1)
                    authorId = -1;

                if (!entityObj.TryGetTypedValue(EntityModelAttributes.Categories, out List<string> categories))
                {
                    categories = [];
                }
                if (!entityObj.TryGetTypedValue(EntityModelAttributes.Tags, out List<string> tags))
                {
                    tags = [];
                }

                if (showOnlyByAuthorId >= 1 && authorId != showOnlyByAuthorId)
                    continue;

                if (showOnlyByCategoryNamesNullable != null && categories.Intersect(showOnlyByCategoryNamesNullable).Count() != showOnlyByCategoryNamesNullable.Count)
                    continue;

                if (showOnlyByTagNamesNullable != null && tags.Intersect(showOnlyByTagNamesNullable).Count() != showOnlyByTagNamesNullable.Count)
                    continue;

                if (tmpDictionary.TryGetValue(id, out var thisNode))
                {
                    thisNode.Obj = entityObj;
                }
                else
                {
                    thisNode = new ElementNode(id, null, entityObj);
                    tmpDictionary.Add(id, thisNode);
                }

                if (entityObj.TryGetTypedValue($"{EntityModelAttributes.Parent}_{EntityModelAttributes.Id}", out int parentId) && parentId >= 1)
                {
                    if (tmpDictionary.TryGetValue(parentId, out var value))
                    {
                        value.Children.Add(thisNode);
                        thisNode.Parent = value;
                    }
                    else
                    {
                        thisNode.Parent = new ElementNode(parentId, null, null);
                        thisNode.Parent.Children.Add(thisNode);
                        tmpDictionary.Add(parentId, thisNode.Parent);
                    }
                }

                if (tags.Count > 0)
                {
                    thisNode.Tags = tags;
                    _tagSeen = true;
                }

                if (categories.Count > 0)
                {
                    thisNode.Categories = categories;
                    _categorySeen = true;
                }

                thisNode.AuthorNullable = null;
                if (authorName != null && authorId >= 1)
                {
                    thisNode.AuthorNullable = new IdName(authorId, authorName);
                    _authorSeen = true;
                }

                if (_lockStatusForEntityType == null || !_lockStatusForEntityType.TryGetValue(id, out thisNode.LockState))
                {
                    thisNode.LockState = null;
                }
                else
                {
                    _lockedSeen = true;
                }
            }
            SetupRootsAndSort(tmpDictionary);

            var headerRowElement = _fullDocument.CreateElement<IHtmlTableRowElement>();
            head.AppendChild(headerRowElement);
            PopulateRowWithHeaderCells(headerRowElement);

            foreach (var currentNode in _roots)
            {
                PopulateTableWithData(body, currentNode, 0);
            }

            var jsFunction = (IHtmlScriptElement)_fullDocument.CreateElement("script");
            _fullDocument.Body?.AppendChild(jsFunction);
            jsFunction.Type = "text/javascript";
            jsFunction.InnerHtml = $$"""

                                     window.process_show_only_or_sort_by = function(key, value) {
                                         let built_url = '{{RfConfiguration.EndpointConfiguration.FinalEntitiesAdminBaseRoute}}?type={{entityName}}';
                                         const params = new URLSearchParams(window.location.search);
                                         let found_exact = false;
                                         for (const [k, v] of params.entries()) {
                                             if (k === 'type') continue;
                                             const k_encoded = encodeURIComponent(k);
                                             const v_encoded = encodeURIComponent(v);
                                             if (k_encoded !== key) {
                                                 built_url += `&${k_encoded}=${v_encoded}`;
                                             }
                                             else if (v_encoded == value) { //This should be == not ===, because of int str comparison
                                                 found_exact = true;
                                             }
                                         }
                                         if (!found_exact && value !== null) {
                                             built_url += `&${key}=${value}`;
                                         }
                                         window.location.href = built_url;
                                     };
                                     window.show_only_by_author = function(self, author_id) {
                                         window.process_show_only_or_sort_by('show_only_by_author', author_id);
                                     };
                                     window.show_only_by_category = function(self, category_name) {
                                         window.process_show_only_or_sort_by(`show_only_by_category_${encodeURIComponent(category_name)}`, 'true');
                                     };
                                     window.show_only_by_tag = function(self, tag_name) {
                                         window.process_show_only_or_sort_by(`show_only_by_tag_${encodeURIComponent(tag_name)}`, 'true');
                                     };
                                     window.sort_by_title = function(self) {
                                         window.process_show_only_or_sort_by('sort_by', 'title');
                                     };
                                     window.sort_by_modified_date = function(self) {
                                         window.process_show_only_or_sort_by('sort_by', null);
                                     };
                                     window.add_new_element = function(self) {
                                         window.location.href = '{{RfConfiguration.EndpointConfiguration.FinalEntitiesAdminBaseRoute}}?type={{entityName}}&{{EntityModelAttributes.Id}}=new';
                                     };
                                     window.clone_element = function(self, element_id) {
                                         window.location.href = '{{RfConfiguration.EndpointConfiguration.FinalEntitiesAdminBaseRoute}}?type={{entityName}}&{{EntityModelAttributes.Id}}=clone_from_' + element_id;
                                     };
                                     window.delete_element = function(self, element_id, element_title) {
                                         Swal.fire({
                                             title: 'Are you sure?',
                                             html: 'You will not be able to revert this!<br><br>{{RfConfiguration.EntityNameToConfiguration[entityName].EntityConfiguration.EntityReadableNameSingular}} with title <b>"' + element_title + '"</b> will be deleted forever!',
                                             icon: 'warning',
                                             showCancelButton: true,
                                             confirmButtonColor: '#d33',
                                             cancelButtonColor: '#3085d6',
                                             confirmButtonText: 'Yes, delete it!'
                                             }).then((result) => {
                                             if (result.isConfirmed) {
                                                 let delete_request = new XMLHttpRequest();
                                                 delete_request.withCredentials = true;
                                                 delete_request.open('POST', '{{RfEndpointMapper.PublicCrudEndpoint}}?operation=DELETE&type={{entityName}}');
                                                 delete_request.setRequestHeader('Content-Type', 'application/json');
                                                 delete_request.onreadystatechange = function() {
                                                     if (this.readyState === XMLHttpRequest.DONE) {
                                                         if (this.status === 200) {
                                                             window.location.reload();
                                                             return;
                                                         }
                                                         else {
                                                             let parsed;
                                                             try { parsed = JSON.parse(this.responseText); } catch (e) { parsed = { message: 'Delete request has failed.' } }
                                                             if (!('message' in parsed)) parsed.message = 'Delete request has failed.';
                                                             iziToast.error({
                                                                 message: parsed.message,
                                                                 timeout: 10000
                                                             });
                                                         }
                                                     }
                                                 };
                                                 delete_request.send(JSON.stringify({ "{{EntityModelAttributes.Id}}": element_id }));
                                             }
                                         });
                                     };
                                     """;
            return Task.FromResult(OperationResult<bool>.Success(true));
        }

        private void PopulateRowWithHeaderCells(IHtmlTableRowElement headerRowElement)
        {
            if (_fullDocument == null) return;
            {
                var headerTitleCellElement = _fullDocument.CreateElement<IHtmlTableHeaderCellElement>();
                headerRowElement.AppendChild(headerTitleCellElement);
                var headerTitleInnerElement = _fullDocument.CreateElement("span");
                headerTitleCellElement.AppendChild(headerTitleInnerElement);

                var anchorElement = _fullDocument.CreateElement<IHtmlAnchorElement>();
                headerTitleInnerElement.AppendChild(anchorElement);
                anchorElement.InnerHtml = $"<u>Title/Name</u> <i class='fa-solid fa-arrow-up-a-z'></i>";
                anchorElement.Href = "#";
                anchorElement.SetAttribute("onclick", $"window.sort_by_title(this);");
                anchorElement.AddClasses("text-nowrap");
            }
            if (_lockedSeen)
            {
                var headerLockedCellElement = _fullDocument.CreateElement<IHtmlTableHeaderCellElement>();
                headerRowElement.AppendChild(headerLockedCellElement);
                var headerLockedInnerElement = _fullDocument.CreateElement("span");
                headerLockedCellElement.AppendChild(headerLockedInnerElement);
                headerLockedInnerElement.InnerHtml = "Being Edited";
                headerLockedInnerElement.AddClasses("text-nowrap", "text-primary");
            }
            if (_authorSeen)
            {
                var headerAuthorCellElement = _fullDocument.CreateElement<IHtmlTableHeaderCellElement>();
                headerRowElement.AppendChild(headerAuthorCellElement);
                var headerAuthorInnerElement = _fullDocument.CreateElement("span");
                headerAuthorCellElement.AppendChild(headerAuthorInnerElement);
                headerAuthorInnerElement.InnerHtml = "Author";
                headerAuthorInnerElement.AddClasses("text-primary");
            }
            if (_categorySeen)
            {
                var headerCategoriesCellElement = _fullDocument.CreateElement<IHtmlTableHeaderCellElement>();
                headerRowElement.AppendChild(headerCategoriesCellElement);
                var headerCategoriesInnerElement = _fullDocument.CreateElement("span");
                headerCategoriesCellElement.AppendChild(headerCategoriesInnerElement);
                headerCategoriesInnerElement.InnerHtml = "Categories";
                headerCategoriesInnerElement.AddClasses("text-primary");
            }
            if (_tagSeen)
            {
                var headerTagsCellElement = _fullDocument.CreateElement<IHtmlTableHeaderCellElement>();
                headerRowElement.AppendChild(headerTagsCellElement);
                var headerTagsInnerElement = _fullDocument.CreateElement("span");
                headerTagsCellElement.AppendChild(headerTagsInnerElement);
                headerTagsInnerElement.InnerHtml = "Tags";
                headerTagsInnerElement.AddClasses("text-primary");
            }
            {
                var headerModifiedCellElement = _fullDocument.CreateElement<IHtmlTableHeaderCellElement>();
                headerRowElement.AppendChild(headerModifiedCellElement);
                var headerModifiedInnerElement = _fullDocument.CreateElement("span");
                headerModifiedCellElement.AppendChild(headerModifiedInnerElement);

                var anchorElement = _fullDocument.CreateElement<IHtmlAnchorElement>();
                headerModifiedInnerElement.AppendChild(anchorElement);
                anchorElement.InnerHtml = $"<u>Date (Last Modified)</u> <i class='fa-solid fa-arrow-up-1-9'></i>";
                anchorElement.Href = "#";
                anchorElement.SetAttribute("onclick", $"window.sort_by_modified_date(this);");
                anchorElement.ClassList.Add("text-nowrap");
            }
            if (!canUserDeleteAnEntity) return;
            var headerDeleteCellElement = _fullDocument.CreateElement<IHtmlTableHeaderCellElement>();
            headerRowElement.AppendChild(headerDeleteCellElement);
            var headerDeleteInnerElement = _fullDocument.CreateElement("span");
            headerDeleteCellElement.AppendChild(headerDeleteInnerElement);
            headerDeleteInnerElement.InnerHtml = "Deletion";
            headerDeleteInnerElement.AddClasses("text-primary");
        }

        private void SetupRootsAndSort(Dictionary<int, ElementNode> tmpDictionary)
        {
            foreach (var currentNode in tmpDictionary.Values.Where(currentNode => currentNode.Parent == null))
            {
                _roots.Add(currentNode);
                if (currentNode.Children.Count > 0)
                {
                    currentNode.Children = SortChildren(currentNode.Children);
                }
            }

            _roots = sortByNullable is EntityModelAttributes.Title ? _roots.OrderBy(c => c.Title).ToList() : _roots.OrderByDescending(c => c.ModifiedDate).ToList();
        }

        private List<ElementNode> SortChildren(List<ElementNode> children)
        {
            var childrenSorted = sortByNullable is EntityModelAttributes.Title ? children.OrderBy(c => c.Title).ToList() : children.OrderByDescending(c => c.ModifiedDate).ToList();

            if (childrenSorted.Count <= 0) return childrenSorted;
            foreach (var child in childrenSorted.Where(child => child.Children.Count > 0))
            {
                child.Children = SortChildren(child.Children);
            }
            return childrenSorted;
        }

        private void PopulateTableWithData(
            INode tBody,
            ElementNode node,
            int depth)
        {
            if (_fullDocument == null) return;

            var rowElement = _fullDocument.CreateElement<IHtmlTableRowElement>();
            tBody.AppendChild(rowElement);

            var titleCell = _fullDocument.CreateElement<IHtmlTableDataCellElement>();
            rowElement.AppendChild(titleCell);

            var titleDiv = _fullDocument.CreateElement<IHtmlDivElement>();
            titleCell.AppendChild(titleDiv);
            titleDiv.StyleElement($"margin-left: {depth * 3}%;");

            if (canUserEditAnEntity)
            {
                var titleElement = _fullDocument.CreateElement<IHtmlAnchorElement>();
                titleDiv.AppendChild(titleElement);
                titleElement.Href = $"{RfConfiguration.EndpointConfiguration.FinalEntitiesAdminBaseRoute}?type={entityName}&id={node.Id}";
                titleElement.InnerHtml = $"<i class='fa-solid fa-pen'></i><u>{node.Title.NotNull().LimitMaxCharacters(96)}</u>";
                titleElement.AddClasses("text-nowrap");
            }
            else if (canUserViewAnEntity)
            {
                var titleElement = _fullDocument.CreateElement<IHtmlAnchorElement>();
                titleDiv.AppendChild(titleElement);
                titleElement.Href = $"{RfConfiguration.EndpointConfiguration.FinalEntitiesBaseRoute}?type={entityName}&id={node.Id}";
                titleElement.InnerHtml = $"<i class='fa-solid fa-eye'></i><u>{node.Title.NotNull().LimitMaxCharacters(96)}</u>";
                titleElement.AddClasses("text-nowrap");
            }
            else
            {
                var titleElement = _fullDocument.CreateElement<IHtmlSpanElement>();
                titleDiv.AppendChild(titleElement);
                titleDiv.InnerHtml = node.Title.NotNull();
                titleDiv.AddClasses("text-nowrap");
            }

            if (_lockedSeen)
            {
                var lockedCell = _fullDocument.CreateElement<IHtmlTableDataCellElement>();
                rowElement.AppendChild(lockedCell);

                if (node.LockState != null)
                {
                    var lockedElement = _fullDocument.CreateElement<IHtmlSpanElement>();
                    lockedCell.AppendChild(lockedElement);
                    lockedElement.InnerHtml = $"Being edited by {node.LockState.LockedByUserName}";
                    lockedElement.AddClasses("text-nowrap");
                }
                else
                {
                    var noLockElement = _fullDocument.CreateElement<IHtmlDivElement>();
                    lockedCell.AppendChild(noLockElement);
                    noLockElement.InnerHtml = "<i class='fa-solid fa-lock-open'></i>";
                }
            }

            if (_authorSeen)
            {
                var authorCell = _fullDocument.CreateElement<IHtmlTableDataCellElement>();
                rowElement.AppendChild(authorCell);

                if (node.AuthorNullable != null)
                {
                    var authorElement = _fullDocument.CreateElement<IHtmlAnchorElement>();
                    authorCell.AppendChild(authorElement);
                    if (showOnlyByAuthorId >= 1 && showOnlyByAuthorId == node.AuthorNullable.Id)
                    {
                        authorElement.InnerHtml = $"<i class='fa-solid fa-filter-circle-xmark'></i><u>{node.AuthorNullable.Name}</u>";
                    }
                    else
                    {
                        authorElement.InnerHtml = $"<i class='fa-solid fa-filter'></i><u>{node.AuthorNullable.Name}</u>";
                    }
                    authorElement.Href = "#";
                    authorElement.SetAttribute("onclick", $"window.show_only_by_author(this, {node.AuthorNullable.Id});");
                    authorElement.AddClasses("text-nowrap");
                }
                else
                {
                    var noAuthorElement = _fullDocument.CreateElement<IHtmlDivElement>();
                    authorCell.AppendChild(noAuthorElement);
                    noAuthorElement.InnerHtml = "<i class='fa-solid fa-user-large-slash'></i>";
                }
            }

            if (_categorySeen)
            {
                var categoriesCell = _fullDocument.CreateElement<IHtmlTableDataCellElement>();
                rowElement.AppendChild(categoriesCell);

                if (node.Categories.Count > 0)
                {
                    var i = 0;
                    foreach (var category in node.Categories)
                    {
                        var categoryElement = _fullDocument.CreateElement<IHtmlAnchorElement>();
                        categoriesCell.AppendChild(categoryElement);
                        if (showOnlyByCategoryNamesNullable != null && showOnlyByCategoryNamesNullable.Contains(category))
                        {
                            categoryElement.InnerHtml = $"<i class='fa-solid fa-filter-circle-xmark'></i><u>{category.LimitMaxCharacters(32)}</u>";
                        }
                        else
                        {
                            categoryElement.InnerHtml = $"<i class='fa-solid fa-filter'></i><u>{category.LimitMaxCharacters(32)}</u>";
                        }
                        categoryElement.Href = "#";
                        categoryElement.SetAttribute("onclick", $"window.show_only_by_category(this, '{category}');");
                        categoryElement.AddClasses("text-nowrap");
                        if (i++ == node.Categories.Count - 1) continue;
                        var breakRowElement = _fullDocument.CreateElement<IHtmlBreakRowElement>();
                        categoriesCell.AppendChild(breakRowElement);
                    }
                }
                else
                {
                    var noCategoriesElement = _fullDocument.CreateElement<IHtmlDivElement>();
                    categoriesCell.AppendChild(noCategoriesElement);
                    noCategoriesElement.InnerHtml = "<i class='fa-solid fa-x'></i>";
                }
            }

            if (_tagSeen)
            {
                var tagsCell = _fullDocument.CreateElement<IHtmlTableDataCellElement>();
                rowElement.AppendChild(tagsCell);

                if (node.Tags.Count > 0)
                {
                    int i = 0;
                    foreach (var tag in node.Tags)
                    {
                        var tagElement = _fullDocument.CreateElement<IHtmlAnchorElement>();
                        tagsCell.AppendChild(tagElement);
                        if (showOnlyByTagNamesNullable != null && showOnlyByTagNamesNullable.Contains(tag))
                        {
                            tagElement.InnerHtml = $"<i class='fa-solid fa-filter-circle-xmark'></i><u>{tag.LimitMaxCharacters(32)}</u>";
                        }
                        else
                        {
                            tagElement.InnerHtml = $"<i class='fa-solid fa-filter'></i><u>{tag.LimitMaxCharacters(32)}</u>";
                        }
                        tagElement.Href = "#";
                        tagElement.SetAttribute("onclick", $"window.show_only_by_tag(this, '{tag}');");
                        tagElement.AddClasses("text-nowrap");
                        if (i++ == node.Tags.Count - 1) continue;
                        var breakRowElement = _fullDocument.CreateElement<IHtmlBreakRowElement>();
                        tagElement.AppendChild(breakRowElement);
                    }
                }
                else
                {
                    var noTagsElement = _fullDocument.CreateElement<IHtmlDivElement>();
                    tagsCell.AppendChild(noTagsElement);
                    noTagsElement.InnerHtml = "<i class='fa-solid fa-x'></i>";
                }
            }

            var dateModifiedCell = _fullDocument.CreateElement<IHtmlTableDataCellElement>();
            rowElement.AppendChild(dateModifiedCell);
            var dateModifiedElement = _fullDocument.CreateElement<IHtmlSpanElement>();
            dateModifiedCell.AppendChild(dateModifiedElement);
            dateModifiedElement.InnerHtml = node.ModifiedDate.ToString("dd MMMM yyyy - HH:mm");
            dateModifiedElement.AddClasses("text-nowrap");

            if (canUserDeleteAnEntity)
            {
                var deleteCell = _fullDocument.CreateElement<IHtmlTableDataCellElement>();
                rowElement.AppendChild(deleteCell);
                var deleteButtonElement = deleteCell.CreateButtonOnElement(_fullDocument.AsCreateElement(), "Delete", "fa-solid fa-trash");
                deleteButtonElement.SetAttribute("onclick", $"window.delete_element(this, {node.Id}, '{node.Title}');");
            }

            if (canUserCreateAnEntity)
            {
                var duplicateCell = _fullDocument.CreateElement<IHtmlTableDataCellElement>();
                rowElement.AppendChild(duplicateCell);
                var duplicateButtonElement = duplicateCell.CreateButtonOnElement(_fullDocument.AsCreateElement(), "Clone", "fa-solid fa-clone");
                duplicateButtonElement.SetAttribute("onclick", $"window.clone_element(this, {node.Id});");
            }

            foreach (var child in node.Children)
            {
                PopulateTableWithData(tBody, child, depth + 1);
            }
        }

        private List<ElementNode> _roots = [];
        private bool _authorSeen;
        private bool _categorySeen;
        private bool _tagSeen;
        private bool _lockedSeen;

        private class IdName(int id, string name)
        {
            public readonly int Id = id;
            public readonly string Name = name;
        }
        private class ElementNode
        {
            private JObject? _obj;
            public JObject? Obj
            {
                set
                {
                    _obj = value;

                    if (_obj == null) return;

                    if (_obj.TryGetValue(EntityModelAttributes.Date, out var dateToken))
                    {
                        CreationDate = dateToken.Type switch
                        {
                            JTokenType.Date => (DateTime)dateToken,
                            JTokenType.String when DateUtility.FromDesiredStringToDateTime(
                                dateToken.Value<string>(), out var tmp) => tmp,
                            _ => CreationDate
                        };
                    }

                    if (_obj.TryGetValue(EntityModelAttributes.Modified, out var modifiedToken))
                    {
                        ModifiedDate = modifiedToken.Type switch
                        {
                            JTokenType.Date => (DateTime)modifiedToken,
                            JTokenType.String when DateUtility.FromDesiredStringToDateTime(modifiedToken.Value<string>(),
                                out var tmp) => tmp,
                            _ => ModifiedDate
                        };
                    }

                    Title = _obj.TryGetTypedValue(EntityModelAttributes.Title, out string? tnTmp) ? tnTmp : null;
                }
            }

            public int Id { get; }

            public string? Title { get; private set; }

            public ElementNode? Parent;

            public List<ElementNode> Children = [];

            private DateTime CreationDate { get; set; }

            public DateTime ModifiedDate { get; private set; }

            public List<string> Tags = [];

            public List<string> Categories = [];

            public IdName? AuthorNullable;

            public EntityLockState? LockState;

            public ElementNode(int id, ElementNode? parent, JObject? obj)
            {
                Id = id;
                Parent = parent;
                Obj = obj;
            }
        }
    }
}
