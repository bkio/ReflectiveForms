// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

namespace ReflectiveForms.Core.Models;

public static class EntityModelAttributes
{
    public const string Id = "id";
    public const string Slug = "slug";
    public const string Link = "link";
    public const string Fields = "fields";
    public const string Parent = "parent";
    public const string Author = "author";
    public const string Title = "title";
    public const string TitleRendered = "rendered";
    public const string Date = "date";
    public const string DateGmt = "date_gmt";
    public const string Modified = "modified";
    public const string ModifiedGmt = "modified_gmt";
    public const string Tags = RfReservedEntities.TagsEntityName;
    public const string Categories = RfReservedEntities.CategoriesEntityName;
}
