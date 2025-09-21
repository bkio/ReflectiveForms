// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using Newtonsoft.Json;

namespace ReflectiveForms.Core.Models;

// 1) WithoutParentWithoutAuthorWithoutTagsWithoutCategories
public sealed class WithoutParentWithoutAuthorWithoutTagsWithoutCategories<T> : EntityModel<T> where T : EntityFieldsModel, new()
{
    internal WithoutParentWithoutAuthorWithoutTagsWithoutCategories() { }
}

// 2) WithParentWithoutAuthorWithoutTagsWithoutCategories
public sealed class WithParentWithoutAuthorWithoutTagsWithoutCategories<T> : EntityModel<T> where T : EntityFieldsModel, new()
{
    [JsonProperty(EntityModelAttributes.Parent)]
    public int Parent = -1;

    internal WithParentWithoutAuthorWithoutTagsWithoutCategories() { }
}

// 3) WithoutParentWithAuthorWithoutTagsWithoutCategories
public sealed class WithoutParentWithAuthorWithoutTagsWithoutCategories<T> : EntityModel<T> where T : EntityFieldsModel, new()
{
    [JsonProperty(EntityModelAttributes.Author)]
    public int Author = -1;

    internal WithoutParentWithAuthorWithoutTagsWithoutCategories() { }
}

// 4) WithoutParentWithoutAuthorWithTagsWithoutCategories
public sealed class WithoutParentWithoutAuthorWithTagsWithoutCategories<T> : EntityModel<T> where T : EntityFieldsModel, new()
{
    [JsonProperty(EntityModelAttributes.Tags)]
    public List<int> Tags = [];

    internal WithoutParentWithoutAuthorWithTagsWithoutCategories() { }
}

// 5) WithoutParentWithoutAuthorWithoutTagsWithCategories
public sealed class WithoutParentWithoutAuthorWithoutTagsWithCategories<T> : EntityModel<T> where T : EntityFieldsModel, new()
{
    [JsonProperty(EntityModelAttributes.Categories)]
    public List<int> Categories = [];

    internal WithoutParentWithoutAuthorWithoutTagsWithCategories() { }
}

// 6) WithParentWithAuthorWithoutTagsWithoutCategories
public sealed class WithParentWithAuthorWithoutTagsWithoutCategories<T> : EntityModel<T> where T : EntityFieldsModel, new()
{
    [JsonProperty(EntityModelAttributes.Parent)]
    public int Parent = -1;

    [JsonProperty(EntityModelAttributes.Author)]
    public int Author = -1;

    internal WithParentWithAuthorWithoutTagsWithoutCategories() { }
}

// 7) WithParentWithoutAuthorWithTagsWithoutCategories
public sealed class WithParentWithoutAuthorWithTagsWithoutCategories<T> : EntityModel<T> where T : EntityFieldsModel, new()
{
    [JsonProperty(EntityModelAttributes.Parent)]
    public int Parent = -1;

    [JsonProperty(EntityModelAttributes.Tags)]
    public List<int> Tags = [];

    internal WithParentWithoutAuthorWithTagsWithoutCategories() { }
}

// 8) WithParentWithoutAuthorWithoutTagsWithCategories
public sealed class WithParentWithoutAuthorWithoutTagsWithCategories<T> : EntityModel<T> where T : EntityFieldsModel, new()
{
    [JsonProperty(EntityModelAttributes.Parent)]
    public int Parent = -1;

    [JsonProperty(EntityModelAttributes.Categories)]
    public List<int> Categories = [];

    internal WithParentWithoutAuthorWithoutTagsWithCategories() { }
}

// 9) WithoutParentWithAuthorWithTagsWithoutCategories
public sealed class WithoutParentWithAuthorWithTagsWithoutCategories<T> : EntityModel<T> where T : EntityFieldsModel, new()
{
    [JsonProperty(EntityModelAttributes.Author)]
    public int Author = -1;

    [JsonProperty(EntityModelAttributes.Tags)]
    public List<int> Tags = [];

    internal WithoutParentWithAuthorWithTagsWithoutCategories() { }
}

// 10) WithoutParentWithAuthorWithoutTagsWithCategories
public sealed class WithoutParentWithAuthorWithoutTagsWithCategories<T> : EntityModel<T> where T : EntityFieldsModel, new()
{
    [JsonProperty(EntityModelAttributes.Author)]
    public int Author = -1;

    [JsonProperty(EntityModelAttributes.Categories)]
    public List<int> Categories = [];

    internal WithoutParentWithAuthorWithoutTagsWithCategories() { }
}

// 11) WithoutParentWithoutAuthorWithTagsWithCategories
public sealed class WithoutParentWithoutAuthorWithTagsWithCategories<T> : EntityModel<T> where T : EntityFieldsModel, new()
{
    [JsonProperty(EntityModelAttributes.Tags)]
    public List<int> Tags = [];

    [JsonProperty(EntityModelAttributes.Categories)]
    public List<int> Categories = [];

    internal WithoutParentWithoutAuthorWithTagsWithCategories() { }
}

// 12) WithParentWithAuthorWithTagsWithoutCategories
public sealed class WithParentWithAuthorWithTagsWithoutCategories<T> : EntityModel<T> where T : EntityFieldsModel, new()
{
    [JsonProperty(EntityModelAttributes.Parent)]
    public int Parent = -1;

    [JsonProperty(EntityModelAttributes.Author)]
    public int Author = -1;

    [JsonProperty(EntityModelAttributes.Tags)]
    public List<int> Tags = [];

    internal WithParentWithAuthorWithTagsWithoutCategories() { }
}

// 13) WithParentWithAuthorWithoutTagsWithCategories
public sealed class WithParentWithAuthorWithoutTagsWithCategories<T> : EntityModel<T> where T : EntityFieldsModel, new()
{
    [JsonProperty(EntityModelAttributes.Parent)]
    public int Parent = -1;

    [JsonProperty(EntityModelAttributes.Author)]
    public int Author = -1;

    [JsonProperty(EntityModelAttributes.Categories)]
    public List<int> Categories = [];

    internal WithParentWithAuthorWithoutTagsWithCategories() { }
}

// 14) WithParentWithoutAuthorWithTagsWithCategories
public sealed class WithParentWithoutAuthorWithTagsWithCategories<T> : EntityModel<T> where T : EntityFieldsModel, new()
{
    [JsonProperty(EntityModelAttributes.Parent)]
    public int Parent = -1;

    [JsonProperty(EntityModelAttributes.Tags)]
    public List<int> Tags = [];

    [JsonProperty(EntityModelAttributes.Categories)]
    public List<int> Categories = [];

    internal WithParentWithoutAuthorWithTagsWithCategories() { }
}

// 15) WithoutParentWithAuthorWithTagsWithCategories
public sealed class WithoutParentWithAuthorWithTagsWithCategories<T> : EntityModel<T> where T : EntityFieldsModel, new()
{
    [JsonProperty(EntityModelAttributes.Author)]
    public int Author = -1;

    [JsonProperty(EntityModelAttributes.Tags)]
    public List<int> Tags = [];

    [JsonProperty(EntityModelAttributes.Categories)]
    public List<int> Categories = [];

    internal WithoutParentWithAuthorWithTagsWithCategories() { }
}

// 16) WithParentWithAuthorWithTagsWithCategories
public sealed class WithParentWithAuthorWithTagsWithCategories<T> : EntityModel<T> where T : EntityFieldsModel, new()
{
    [JsonProperty(EntityModelAttributes.Parent)]
    public int Parent = -1;

    [JsonProperty(EntityModelAttributes.Author)]
    public int Author = -1;

    [JsonProperty(EntityModelAttributes.Tags)]
    public List<int> Tags = [];

    [JsonProperty(EntityModelAttributes.Categories)]
    public List<int> Categories = [];

    internal WithParentWithAuthorWithTagsWithCategories() { }
}

