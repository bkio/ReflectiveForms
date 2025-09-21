// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using Newtonsoft.Json;

namespace ReflectiveForms.Core.Models;

public class TitleRenderedModel : BaseModel
{
    [JsonProperty(EntityModelAttributes.TitleRendered)]
    public string Text = "";

    internal TitleRenderedModel() {}
}

public class EntityModel<T> : BaseModel where T : EntityFieldsModel, new()
{
    [JsonProperty(EntityModelAttributes.Id)]
    public int Id = -1;

    [JsonProperty(EntityModelAttributes.Slug)]
    public string Slug = "";

    [JsonProperty(EntityModelAttributes.Title)]
    public TitleRenderedModel Title = new();

    [JsonProperty(EntityModelAttributes.Date)]
    public string Date;

    [JsonProperty(EntityModelAttributes.DateGmt)]
    public string DateGmt;

    [JsonProperty(EntityModelAttributes.Modified)]
    public string LastUpdated;

    [JsonProperty(EntityModelAttributes.ModifiedGmt)]
    public string LastUpdatedGmt;

    /// <summary>
    /// Add all fields like Select, Text, etc. under a class inheriting from BaseModel.
    /// </summary>
    [JsonProperty(EntityModelAttributes.Fields)]
    public required T Fields { get; init; } = new();

    internal EntityModel()
    {
        var utcNow = DateTime.UtcNow;

        // UTC/GMT
        DateGmt = utcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        LastUpdatedGmt = DateGmt;

        // Local time
        var localNow = utcNow.ToLocalTime();
        Date = localNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        LastUpdated = Date;
    }
}
