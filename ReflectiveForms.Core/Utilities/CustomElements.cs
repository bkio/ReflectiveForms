// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using CrossCloudKit.Utilities.Common;
// ReSharper disable NotAccessedPositionalProperty.Global
// ReSharper disable ClassNeverInstantiated.Global

// ReSharper disable MemberCanBePrivate.Global

namespace ReflectiveForms.Core.Utilities;

public static class CustomElements
{
    public static T AddClasses<T>(this T el, params string[] classNames) where T : IElement?
    {
        foreach (var className in classNames)
        {
            el?.ClassList.Add(className);
        }
        return el;
    }
    public static T RemoveClasses<T>(this T el, params string[] classNames) where T : IElement?
    {
        foreach (var className in classNames)
        {
            el?.ClassList.Remove(className);
        }
        return el;
    }
    public static T StyleElement<T>(this T el, string newStyle) where T : IElement
    {
        el.SetAttribute("style", newStyle);
        return el;
    }
    public static IHtmlDivElement CreateRow(this IElement? parentEl, CreateElement? createElement)
    {
        ArgumentNullException.ThrowIfNull(createElement);

        var newRow = createElement.Invoke<IHtmlDivElement>();
        parentEl?.AppendChild(newRow);
        newRow.ClassList.Add("row", "mb-3");
        return newRow;
    }
    public static IHtmlDivElement CreateCol1OnRow(this IHtmlDivElement? parentRow, CreateElement? createElement)
    {
        ArgumentNullException.ThrowIfNull(createElement);

        var newCol = createElement.Invoke<IHtmlDivElement>();
        parentRow?.AppendChild(newCol);
        newCol.ClassList.Add("col-12");
        return newCol;
    }
    public static IHtmlDivElement CreateCol2OnRow(this IHtmlDivElement parentRow, CreateElement? createElement)
    {
        ArgumentNullException.ThrowIfNull(createElement);

        var newCol = createElement.Invoke<IHtmlDivElement>();
        parentRow.AppendChild(newCol);
        newCol.ClassList.Add("col-xl-6", "col-lg-6", "col-md-12", "col-sm-12", "col-12");
        return newCol;
    }
    public static IHtmlDivElement CreateCol3OnRow(this IHtmlDivElement parentRow, CreateElement? createElement)
    {
        ArgumentNullException.ThrowIfNull(createElement);

        var newCol = createElement.Invoke<IHtmlDivElement>();
        parentRow.AppendChild(newCol);
        newCol.ClassList.Add("col-xl-4", "col-lg-4", "col-md-12", "col-sm-12", "col-12");
        return newCol;
    }
    public static IHtmlDivElement CreateCol4OnRow(this IHtmlDivElement parentRow, CreateElement? createElement)
    {
        ArgumentNullException.ThrowIfNull(createElement);

        var newCol = createElement.Invoke<IHtmlDivElement>();
        parentRow.AppendChild(newCol);
        newCol.ClassList.Add("col-xl-3", "col-lg-6", "col-md-12", "col-sm-12", "col-12");
        return newCol;
    }
    public static IHtmlDivElement CreateCol6OnRow(this IHtmlDivElement parentRow, CreateElement? createElement)
    {
        ArgumentNullException.ThrowIfNull(createElement);

        var newCol = createElement.Invoke<IHtmlDivElement>();
        parentRow.AppendChild(newCol);
        newCol.ClassList.Add("col-xl-2", "col-lg-4", "col-md-12", "col-sm-12", "col-12");
        return newCol;
    }
    public static IHtmlDivElement CreateCustomColOnRow(this IHtmlDivElement parentRow, CreateElement? createElement, int colClassNo)
    {
        ArgumentNullException.ThrowIfNull(createElement);

        var newCol = createElement.Invoke<IHtmlDivElement>();
        parentRow.AppendChild(newCol);
        newCol.ClassList.Add($"col-xl-{colClassNo}", $"col-lg-{colClassNo}", "col-md-12", "col-sm-12", "col-12");
        return newCol;
    }
    public static IHtmlDivElement CreateColFitContentLeftAlignedOnRow(this IHtmlDivElement parentRow, CreateElement? createElement)
    {
        ArgumentNullException.ThrowIfNull(createElement);

        var newCol = createElement.Invoke<IHtmlDivElement>();
        parentRow.AppendChild(newCol);
        newCol.ClassList.Add("col", "d-xl-flex", "justify-content-xl-start");
        return newCol;
    }
    public static IHtmlDivElement CreateColFitContentRightAlignedOnRow(this IHtmlDivElement parentRow, CreateElement? createElement)
    {
        ArgumentNullException.ThrowIfNull(createElement);

        var newCol = createElement.Invoke<IHtmlDivElement>();
        parentRow.AppendChild(newCol);
        newCol.ClassList.Add("col", "d-xl-flex", "justify-content-xl-end");
        return newCol;
    }
    public static IHtmlDivElement CreateColFitContentCenteredOnRow(this IHtmlDivElement parentRow, CreateElement? createElement)
    {
        ArgumentNullException.ThrowIfNull(createElement);

        var newCol = createElement.Invoke<IHtmlDivElement>();
        parentRow.AppendChild(newCol);
        newCol.ClassList.Add("col", "d-flex", "justify-content-center");
        return newCol;
    }
    public static (IHtmlDivElement Wrapper, IHtmlDivElement HeaderRow, IHtmlDivElement Content) CreateCardOnCol(this IHtmlElement parentCol, CreateElement? createElement, string faIcon = "", string? headerText = "")
    {
        ArgumentNullException.ThrowIfNull(createElement);

        var newCard = createElement.Invoke<IHtmlDivElement>();
        parentCol.AppendChild(newCard);
        newCard.ClassList.Add("card", "shadow", "border-left-primary", "py-1");

        var cardBody = createElement.Invoke<IHtmlDivElement>();
        newCard.AppendChild(cardBody);
        cardBody.ClassList.Add("card-body");

        var rowInCard = createElement.Invoke<IHtmlDivElement>();
        cardBody.AppendChild(rowInCard);
        rowInCard.ClassList.Add("row", "no-gutters", "align-items-center");

        var contentAndHeaderWrapperCol = createElement.Invoke<IHtmlDivElement>();
        rowInCard.AppendChild(contentAndHeaderWrapperCol);
        contentAndHeaderWrapperCol.ClassList.Add("col", "mr-2", "content-and-header-col");

        var cardHeaderRow = createElement.Invoke<IHtmlDivElement>();
        contentAndHeaderWrapperCol.AppendChild(cardHeaderRow);
        cardHeaderRow.ClassList.Add("row", "card-header-row");
        var cardHeader = createElement.Invoke<IHtmlDivElement>();
        cardHeaderRow.AppendChild(cardHeader);
        cardHeader.ClassList.Add("col", "text", "font-weight-bold", "text-primary", "card-header-col");
        if (headerText != null)
        {
            cardHeader.InnerHtml = headerText;
            if (headerText.Length > 0)
            {
                cardHeader.ClassList.Add("mb-3");
            }
        }

        var cardContent = createElement.Invoke<IHtmlDivElement>();
        contentAndHeaderWrapperCol.AppendChild(cardContent);
        cardContent.ClassList.Add("mb-0", "text-gray-800", "card-content");

        if (faIcon.Length <= 0) return (newCard, cardHeaderRow, cardContent);
        var cardIcon = createElement.Invoke<IHtmlDivElement>();
        rowInCard.AppendChild(cardIcon);
        cardIcon.ClassList.Add("col-auto");
        cardIcon.InnerHtml = $"<i class='{faIcon} fa-2x text-gray-300'></i>";

        return (newCard, cardHeaderRow, cardContent);
    }
    public static IHtmlHrElement CreateDivider(this IElement parent, CreateElement? createElement)
    {
        ArgumentNullException.ThrowIfNull(createElement);

        var divider = createElement.Invoke<IHtmlHrElement>();
        parent.AppendChild(divider);
        divider.ClassList.Add("border-secondary", "my-5");
        return divider;
    }
    public static IHtmlAnchorElement CreateButtonOnElement(this IElement parent, CreateElement? createElement, string buttonInnerText, string faIcon = "fa-solid fa-arrow-pointer", string buttonColorClass = "btn-primary")
    {
        ArgumentNullException.ThrowIfNull(createElement);

        var newA = createElement.Invoke<IHtmlAnchorElement>();
        parent.AppendChild(newA);
        newA.AddClasses("btn", buttonColorClass);

        if (buttonInnerText.Length > 0 && faIcon.Length > 0)
        {
            newA.AddClasses("btn-icon-split");
        }
        if (faIcon.Length > 0)
        {
            var iconInA = createElement.Invoke<IHtmlSpanElement>();
            newA.AppendChild(iconInA);
            iconInA.ClassList.Add("icon");
            iconInA.InnerHtml = $"<i class='{faIcon}'></i>";
        }

        if (buttonInnerText.Length <= 0) return newA;
        var textInA = createElement.Invoke<IHtmlSpanElement>();
        newA.AppendChild(textInA);
        textInA.ClassList.Add("text");
        textInA.InnerHtml = buttonInnerText;
        textInA.SetAttribute("data-calculate-dynamic-font-size", "window.innerWidth - 200");

        return newA;
    }
    //Note: Do not use it in FIELDS elements! It uses ids and element clone creates problem for repeater elements.
    public static IHtmlDivElement CreateCollapseOnCard(this IHtmlDivElement parentCard, CreateElement? createElement, string buttonInnerText, string faIcon = "fa-solid fa-arrow-pointer", string buttonColorClass = "btn-primary")
    {
        ArgumentNullException.ThrowIfNull(createElement);

        var areaId = StringUtilities.GenerateRandomString(32, DigitOptions.OnlyCharacters, CaseOptions.FullUppercase);

        var showHideButton = parentCard.CreateButtonOnElement(createElement, buttonInnerText, faIcon, buttonColorClass);
        showHideButton.SetAttribute("data-toggle", "collapse");
        showHideButton.SetAttribute("data-target", $"#{areaId}");
        showHideButton.SetAttribute("aria-expanded", "false");
        showHideButton.SetAttribute("aria-controls", areaId);

        var collapseContent = createElement.Invoke<IHtmlDivElement>();
        parentCard.AppendChild(collapseContent);
        collapseContent.ClassList.Add("collapse");
        collapseContent.Id = areaId;

        return collapseContent;
    }
    // ReSharper disable once UnusedTupleComponentInReturnValue
    public static (IHtmlDivElement Wrapper, IHtmlTableElement Table, IHtmlTableSectionElement THead, IHtmlTableSectionElement TBody) CreateTableOnCard(this IElement parentCard, CreateElement? createElement, bool bSetDynamicMinWidth = true, int minWidthToBeSubtractedFromInnerWidth = 200)
    {
        ArgumentNullException.ThrowIfNull(createElement);

        var wrapper = createElement.Invoke<IHtmlDivElement>();
        parentCard.AppendChild(wrapper);
        wrapper.ClassList.Add("table-responsive");

        var table = createElement.Invoke<IHtmlTableElement>();
        wrapper.AppendChild(table);
        table.ClassList.Add("table");
        if (bSetDynamicMinWidth)
        {
            table.SetAttribute("data-dynamic-min-width", $"window.innerWidth - {minWidthToBeSubtractedFromInnerWidth}");
        }

        var tableHead = (IHtmlTableSectionElement)createElement.Invoke("thead");
        table.AppendChild(tableHead);

        var tableBody = (IHtmlTableSectionElement)createElement.Invoke("tbody");
        table.AppendChild(tableBody);

        return (wrapper, table, tableHead, tableBody);
    }
    public enum AccordionType
    {
        ButtonsAtTheTop,
        ButtonsOnEachCard
    }
    public enum ButtonsAlignOn
    {
        Left,
        Right,
        Center
    }
    public abstract record CustomElementsExpandButton(
        string ExpandButtonInnerText,
        string ExpandButtonFaIcon);

    public record CustomElementsAddNewButtonResult(
        string Id,
        IHtmlDivElement? Wrapper,
        IHtmlDivElement ContentBody);
    public record CustomElementsCreateAccordionOnColOutput(
        IHtmlDivElement Wrapper,
        Func<CustomElementsExpandButton, CustomElementsAddNewButtonResult?> ActionAddNew,
        Func<string, bool> ActionRemove);
    public record CustomElementsCreateAccordionOnColInput(
        CreateElement? CreateElement,
        AccordionType AccordionType,
        ButtonsAlignOn AlignButtonsOn);

    public static CustomElementsCreateAccordionOnColOutput CreateAccordionOnCol(this IElement parentCol, CustomElementsCreateAccordionOnColInput input)
    {
        var (createElement, accordionType, buttonsAlignOn) = input;

        ArgumentNullException.ThrowIfNull(createElement);

        var tabsArea = createElement.Invoke<IHtmlDivElement>();
        parentCol.AppendChild(tabsArea);
        tabsArea.Id = StringUtilities.GenerateRandomString(32, DigitOptions.OnlyCharacters, CaseOptions.FullUppercase);
        var tabsAreaWeakRef = new WeakReference<IHtmlDivElement>(tabsArea);

        Func<CustomElementsExpandButton, CustomElementsAddNewButtonResult?> actionAddNew;
        Func<string, bool> actionRemove;

        if (accordionType == AccordionType.ButtonsOnEachCard)
        {
            tabsArea.AddClasses("accordion");

            actionAddNew = v =>
            {
                var (expandButtonInnerText, expandButtonFaIcon) = v;

                if (!tabsAreaWeakRef.TryGetTarget(out var tabsAreaDerefed)) return null;

                var cardParent = createElement.Invoke<IHtmlDivElement>().AddClasses("card");
                tabsAreaDerefed.AppendChild(cardParent);
                cardParent.Id = StringUtilities.GenerateRandomString(32, DigitOptions.OnlyCharacters, CaseOptions.FullUppercase);
                {
                    var cardHeader = createElement.Invoke<IHtmlDivElement>().AddClasses("card-header");
                    cardParent.AppendChild(cardHeader);
                    cardHeader.Id = $"{cardParent.Id}-header";
                    {
                        var buttonCol = buttonsAlignOn switch
                        {
                            ButtonsAlignOn.Left => cardHeader.CreateRow(createElement)
                                .CreateColFitContentLeftAlignedOnRow(createElement),
                            ButtonsAlignOn.Right => cardHeader.CreateRow(createElement)
                                .CreateColFitContentRightAlignedOnRow(createElement),
                            _ => cardHeader.CreateRow(createElement).CreateColFitContentCenteredOnRow(createElement)
                        };

                        var h2 = createElement.Invoke("h2").AddClasses("mb-0");
                        buttonCol.AppendChild(h2);
                        {
                            var button = h2.CreateButtonOnElement(createElement, expandButtonInnerText, expandButtonFaIcon);
                            button.SetAttribute("data-toggle", "collapse");
                            button.SetAttribute("data-target", $"#{cardParent.Id}-content");
                        }
                    }

                    var cardContent = createElement.Invoke<IHtmlDivElement>().AddClasses("collapse");
                    cardParent.AppendChild(cardContent);
                    cardContent.Id = $"{cardParent.Id}-content";
                    cardContent.SetAttribute("aria-labelledby", cardHeader.Id);
                    cardContent.SetAttribute("data-parent", $"#{tabsAreaDerefed.Id}");
                    {
                        var cardContentBody = createElement.Invoke<IHtmlDivElement>().AddClasses("card-body");
                        cardContent.AppendChild(cardContentBody);

                        return new CustomElementsAddNewButtonResult(cardParent.Id, cardParent, cardContentBody);
                    }
                }
            };
            actionRemove = areaId =>
            {
                if (!tabsAreaWeakRef.TryGetTarget(out var tabsAreaDerefed)) return false;

                var cardParent = tabsAreaDerefed.QuerySelector($"A#{areaId}");
                if (cardParent == null) return false;

                cardParent.Remove();
                return true;
            };
        }
        else //ButtonsAtTheTop
        {
            var buttonsCol = buttonsAlignOn switch
            {
                ButtonsAlignOn.Left => tabsArea.CreateRow(createElement)
                    .CreateColFitContentLeftAlignedOnRow(createElement),
                ButtonsAlignOn.Right => tabsArea.CreateRow(createElement)
                    .CreateColFitContentRightAlignedOnRow(createElement),
                _ => tabsArea.CreateRow(createElement).CreateColFitContentCenteredOnRow(createElement)
            };

            var buttonsColWeakRef = new WeakReference<IHtmlDivElement>(buttonsCol);

            actionAddNew = v =>
            {
                var (expandButtonInnerText, expandButtonFaIcon) = v;
                if (!tabsAreaWeakRef.TryGetTarget(out var tabsAreaDerefed) || !buttonsColWeakRef.TryGetTarget(out var buttonsColDerefed)) return null;

                var newArea = createElement.Invoke<IHtmlDivElement>().AddClasses("collapse", "mt-3");
                tabsAreaDerefed.AppendChild(newArea);
                newArea.Id = StringUtilities.GenerateRandomString(32, DigitOptions.OnlyCharacters, CaseOptions.FullUppercase);
                newArea.SetAttribute("data-parent", $"#{tabsAreaDerefed.Id}");

                var newButton = buttonsColDerefed.CreateButtonOnElement(createElement, expandButtonInnerText, expandButtonFaIcon).AddClasses("mx-2");
                newButton.SetAttribute("data-toggle", "collapse");
                newButton.SetAttribute("role", "button");
                newButton.SetAttribute("aria-expanded", "false");
                newButton.SetAttribute("aria-controls", newArea.Id);
                newButton.Href = $"#{newArea.Id}";

                return new CustomElementsAddNewButtonResult(newArea.Id, null, newArea);
            };
            actionRemove = areaId =>
            {
                if (!tabsAreaWeakRef.TryGetTarget(out var tabsAreaDerefed)) return false;

                var relevantButton = tabsAreaDerefed.QuerySelector($"[aria-controls=\"{areaId}\"]");
                if (relevantButton == null) return false;

                var relevantArea = tabsAreaDerefed.QuerySelector($"#{areaId}");
                if (relevantArea == null) return false;

                relevantButton.Remove();
                relevantArea.Remove();
                return true;
            };
        }
        return new CustomElementsCreateAccordionOnColOutput(tabsArea, actionAddNew, actionRemove);
    }
    //Note: Do not use it in FIELDS elements! It uses ids and element clone creates problem for repeater elements.
    public static (IHtmlDivElement Wrapper, IHtmlInputElement Checkbox) CreateCheckboxOnCard(this IElement parentCard, CreateElement? createElement, string labelText)
    {
        ArgumentNullException.ThrowIfNull(createElement);

        var wrapper = createElement.Invoke<IHtmlDivElement>();
        parentCard.AppendChild(wrapper);
        wrapper.ClassList.Add("custom-control", "custom-switch", "d-flex", "align-items-center");

        var newCheckboxInput = createElement.Invoke<IHtmlInputElement>();
        wrapper.AppendChild(newCheckboxInput);
        newCheckboxInput.ClassList.Add("custom-control-input");
        newCheckboxInput.Type = "checkbox";
        newCheckboxInput.Id = StringUtilities.GenerateRandomString(32, DigitOptions.OnlyCharacters, CaseOptions.FullUppercase);

        var newLabel = createElement.Invoke<IHtmlLabelElement>();
        wrapper.AppendChild(newLabel);
        newLabel.ClassList.Add("custom-control-label", "text-nowrap");
        newLabel.SetAttribute("for", newCheckboxInput.Id);
        newLabel.InnerHtml = labelText;

        return (wrapper, newCheckboxInput);
    }
    //Note: Do not use it in FIELDS elements! It uses ids and element clone creates problem for repeater elements.
    public static CustomSelect CreateSelectBoxOnCard(this IHtmlDivElement parentCard, CreateElement? createElement)
    {
        ArgumentNullException.ThrowIfNull(createElement);

        var newBox = createElement.Invoke<IHtmlDivElement>();
        parentCard.AppendChild(newBox);
        newBox.ClassList.Add("dropdown", "custom-generated-select-box");

        var newButton = createElement.Invoke<IHtmlButtonElement>();
        newBox.AppendChild(newButton);
        newButton.ClassList.Add("btn", "btn-primary", "dropdown-toggle");
        newButton.Type = "button";
        newButton.Id = StringUtilities.GenerateRandomString(32, DigitOptions.OnlyCharacters, CaseOptions.FullUppercase);
        newButton.SetAttribute("data-toggle", "dropdown");
        newButton.SetAttribute("aria-haspopup", "true");
        newButton.SetAttribute("aria-expanded", "false");

        var optionsContainer = createElement.Invoke<IHtmlDivElement>();
        newBox.AppendChild(optionsContainer);
        optionsContainer.ClassList.Add("dropdown-menu", "animated--fade-in");
        optionsContainer.SetAttribute("aria-labelledby", newButton.Id);
        optionsContainer.SetAttribute("data-make-scrollable-auto-calculate", "xy");

        var customHandler = new CustomSelect(newBox, optionsContainer, newButton);
        MemoryGCConnector.Instance.Connect(customHandler, newBox);
        return customHandler;
    }
    public class CustomSelectOption(CustomSelect select, IHtmlAnchorElement aElement)
    {
        private readonly WeakReference<CustomSelect> _select = new(select);
        private readonly WeakReference<IHtmlAnchorElement> _aElement = new(aElement);

        public string? Value
        {
            get => !_aElement.TryGetTarget(out var deref) ? null : deref.GetAttribute("data-value");
            set
            {
                if (!_aElement.TryGetTarget(out var deref)) return;
                deref.SetAttribute("data-value", value);
            }
        }
        public string? InnerHtml
        {
            get => !_aElement.TryGetTarget(out var deref) ? null : deref.InnerHtml;
            set
            {
                if (!_aElement.TryGetTarget(out var deref)) return;
                deref.InnerHtml = value.NotNull();

                if (!_select.TryGetTarget(out var selectDeref)) return;
                if (selectDeref.SelectedOption == this
                    && selectDeref.Button != null)
                {
                    selectDeref.Button.InnerHtml = value.NotNull();
                }
            }
        }
        public string? TextContent
        {
            get => InnerHtml;
            set => InnerHtml = value;
        }
        public string? Text
        {
            get => InnerHtml;
            set => InnerHtml = value;
        }
        public bool Selected
        {
            get
            {
                if (!_select.TryGetTarget(out var selectDeref)) return false;
                return selectDeref.SelectedOption == this;
            }
            set
            {
                if (!_select.TryGetTarget(out var selectDeref)) return;
                if (value)
                {
                    if (selectDeref.SelectedOption == this) return;
                    selectDeref.SelectedOption = this;
                }
                else
                {
                    if (selectDeref.SelectedOption != this) return;
                    selectDeref.SelectedOption = null;
                }
            }
        }
    }
    public class CustomSelect(IHtmlDivElement wrapper, IHtmlDivElement optionsContainer, IHtmlButtonElement button)
    {
        private readonly WeakReference<IHtmlDivElement> _wrapper = new(wrapper);
        private readonly WeakReference<IHtmlDivElement> _optionsContainer = new(optionsContainer);
        private readonly WeakReference<IHtmlButtonElement> _button = new(button);
        private readonly List<CustomSelectOption> _options = [];

        public CustomSelectOption? AddOption(CreateElement createElement)
        {
            if (!_optionsContainer.TryGetTarget(out var optionsContainerDeref)) return null;

            var newAnchor = createElement.Invoke<IHtmlAnchorElement>();
            optionsContainerDeref.AppendChild(newAnchor);
            newAnchor.ClassList.Add("dropdown-item");
            newAnchor.SetAttribute("onclick", """

                                              const wrapper_el = this.parentElement.parentElement;
                                              for (let i = 0; i < wrapper_el.childNodes.length; i++) {
                                                  const child = wrapper_el.childNodes[i];
                                                  if (child instanceof HTMLButtonElement) {
                                                      child.innerHTML = this.innerHTML;
                                                      break;
                                                  }
                                              }
                                              wrapper_el.setAttribute('data-value', this.getAttribute('data-value'));
                                              new Function(parent_div.getAttribute('data-onchange')).call(wrapper_el);

                                              """);
            var newOption = new CustomSelectOption(this, newAnchor);
            _options.Add(newOption);
            return newOption;
        }

        private CustomSelectOption? _selectedOption;
        public CustomSelectOption? SelectedOption
        {
            get => _selectedOption;
            set
            {
                if (_selectedOption == value) return;

                _selectedOption = value;

                if (value == null)
                {
                    if (Button != null) Button.InnerHtml = "";
                    Wrapper?.SetAttribute("data-value", "");

                    foreach (var t in _options.Where(t => t.Value == "-1"))
                    {
                        t.Selected = true;
                        if (Button != null) Button.InnerHtml = t.InnerHtml.NotNull();
                        Wrapper?.SetAttribute("data-value", t.Value);
                    }
                }
                else
                {
                    if (Button != null) Button.InnerHtml = _selectedOption.NotNull().InnerHtml.NotNull();
                    Wrapper?.SetAttribute("data-value", _selectedOption.NotNull().Value);
                }
            }
        }

        public string? OnChange
        {
            get => Wrapper?.GetAttribute("data-onchange");
            set => Wrapper?.SetAttribute("data-onchange", value);
        }

        public IHtmlDivElement? Wrapper => !_wrapper.TryGetTarget(out var wrapperDeref) ? null : wrapperDeref;
        public IHtmlButtonElement? Button => !_button.TryGetTarget(out var buttonDeref) ? null : buttonDeref;
    }
}
