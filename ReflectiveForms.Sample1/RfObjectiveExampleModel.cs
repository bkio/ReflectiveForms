// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using CrossCloudKit.Utilities.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core;
using ReflectiveForms.Core.Attributes;
using ReflectiveForms.Core.Attributes.Fields;
using ReflectiveForms.Core.Enums;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Operation;
using ReflectiveForms.Core.Utilities;

namespace ReflectiveForms.Sample1;

[StickyTitle("comment")]
internal class SampleCommentModel : BaseModel
{
    /// <summary>
    /// Relation example
    /// </summary>
    [JsonProperty("author"),
        Relation(
            label: "Author",
            instructions: "",
            mandatory: true,
            relationEntityName: RfReservedEntities.UsersEntityName,
            isRelationEntityNotExistsOk: true)]
    public int AuthorId = -1;

    [JsonProperty("comment"),
        TextArea(
            label: "Comment",
            instructions: "",
            mandatory: true,
            placeholderText: "")]
    public string Text = "";
}

[StickyTitle("key_result")]
internal class KeyResultsModel : BaseModel
{
    /// <summary>
    /// Text/TextArea example
    /// </summary>
    [JsonProperty("key_result"),
        TextArea(
            label: "Key Results",
            instructions: "Use <b>measurable</b> statements; like Make .... or Complete ...; avoid unmeasurable statements like Improve ...",
            mandatory: true,
            placeholderText: "")]
    public string KeyResult = "";

    [JsonProperty("key_result_comments", NullValueHandling = NullValueHandling.Ignore),
        Repeater(
            label: "Key Result Comments",
            instructions: "",
            repeaterFor: typeof(SampleCommentModel),
            addButtonLabel: "Add Comment to the Key Result",
            groupRenderStyle: GroupRenderStyle.Grid2ElementsInRow)]
    public List<SampleCommentModel> Comments = [];

    /// <summary>
    /// Checkbox example
    /// </summary>
    [JsonProperty("achieved"),
     Checkbox(
         label: "Is it achieved?",
         instructions: "If the key result is achieved, check this.",
         defaultValue: false)]
    public bool IsAchieved;
}

internal class RfObjectiveExampleModel : EntityFieldsModel
{
    /// <summary>
    /// DatePicker example
    /// </summary>
    [JsonProperty("objective_work_start_date"),
     DatePicker(
         label: "Objective Work Planned Start Date",
         instructions:
         "This date can be overridden automatically by roadmap calculation based on team availabilities.<br>Therefore choose the value <b>Order of Importance Among Objectives</b> underneath this field carefully.",
         mandatory: true,
         dateFormat: "yyyyMMdd")]
    public string DesiredObjectiveWorkStartDate = "";
    public Task<object?> DesiredObjectiveWorkStartDate___DynamicDefaultValueAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult<object?>(DateTime.Now.ToString("yyyyMMdd"));
    }

    /// <summary>
    /// Static Select example
    /// </summary>
    [JsonProperty("objective_type"),
     Select(
         label: "Short-term or Long-term?",
         instructions: "Shall give <b>short-term</b> or <b>long-term</b> values?",
         defaultValue: "short_term",
         choices:
         [
             "short_term : Gives short-term values",
             "long_term : Gives long-term values"
         ])]
    public string ObjectiveType = "short_term";

    /// <summary>
    /// Url example
    /// </summary>
    [JsonProperty("documentation_url"),
     Url(
         label: "Objective Documentation URL",
         instructions: "Detailed documentation of the objective is found here.",
         mandatory: false,
         placeholderText: "https://example.com/documentation"
     )]
    public string DocumentationUrl = "";

    /// <summary>
    /// LogicSanityCheckAsync example
    /// </summary>
    [JsonProperty("root_cause"),
     TextArea(
         label: "Root Cause",
         instructions: "",
         mandatory: true,
         placeholderText: "")]
    public string RootCause = "";
    public async Task<string?> RootCause___LogicSanityCheckAsync(int entityId, EntityOperationState operationState, JObject parentJObject, CancellationToken cancellationToken)
    {
        var allEntities = await operationState.GetAllEntitiesInOperationAsync("objective", cancellationToken);
        if (!allEntities.IsSuccessful)
            return allEntities.ErrorMessage;

        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var entity in allEntities.Data)
        {
            var casted = entity.ToObjectWithPolymorphism<EntityModel<RfObjectiveExampleModel>>().NotNull();
            var fields = casted.Fields;
            if (fields.RootCause == RootCause && casted.Id != entityId)
                return "Root cause is already used by another objective.";
        }
        return null; //Passed sanity check.
    }

    /// <summary>
    /// Group example
    /// </summary>
    [JsonProperty("creator_comment"),
     Group(
         label: "What drove the creation of this objective?",
         instructions: "",
         groupFor: typeof(SampleCommentModel))]
    public SampleCommentModel CreatorComment = new();

    /// <summary>
    /// Repeater example
    /// </summary>
    [JsonProperty("key_results", NullValueHandling = NullValueHandling.Ignore),
     Repeater(
         label: "Key Results",
         instructions:
         "<b>Add statements that are measurably. Avoid: 'happy customer'-like unmeasurable statements.</b>",
         repeaterFor: typeof(KeyResultsModel),
         addButtonLabel: "Add Key Result",
         useAccordion: RepeatUseAccordion.Yes)]
    public List<KeyResultsModel> KeyResults = [];

    /// <summary>
    /// Repeater example
    /// </summary>
    [JsonProperty("objective_comments", NullValueHandling = NullValueHandling.Ignore),
     Repeater(
         label: "Objective Comments",
         instructions: "",
         repeaterFor: typeof(SampleCommentModel),
         addButtonLabel: "Add Comment",
         groupRenderStyle: GroupRenderStyle.Grid2ElementsInRow)]
    public List<SampleCommentModel> Comments = [];

    /// <summary>
    /// DynamicChoicesCompileTimeAsync example
    /// </summary>
    [JsonProperty("objective_initiation_year"),
     Select(
         label: "Objective Initiation Year",
         instructions: "",
         defaultValue: "",
         choices: null)]
    public string ObjectiveInitiationYear { get; init; } = "-1";
    public static Task<string[]> ObjectiveInitiationYear___DynamicChoicesCompileTimeAsync(CancellationToken cancellationToken)
    {
        var result = new List<string>
        {
            "-1 : Please Select",
            $"{DateTime.Now.Year - 1} : {DateTime.Now.Year - 1}",
            $"{DateTime.Now.Year} : {DateTime.Now.Year}",
            $"{DateTime.Now.Year + 1} : {DateTime.Now.Year + 1}"
        };
        return Task.FromResult(result.ToArray());
    }

    /// <summary>
    /// DynamicChoicesRuntimeAsync example
    /// </summary>
    [JsonProperty("year_based_okr_type"),
     Select(
         label: "Select an OKR type",
         instructions: "",
         defaultValue: "",
         choices: null)]
    public string YearBasedOkrType = "unspecified";
    public Task<string> YearBasedOkrType___DynamicChoicesRuntimeAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult("""
                               const input = window.latest_dynamic_options_input;

                               if (input.objective_initiation_year <= 0) return ['unspecified : Select an objective initiation year first.'];

                               return [
                                  'unspecified : Select an OKR type',
                                  `${input.objective_initiation_year - 1}_low_priority : ${input.objective_initiation_year - 1} (Low Priority)`,
                                  `${input.objective_initiation_year - 1}_high_priority : ${input.objective_initiation_year - 1} (High Priority)`,
                                  `${input.objective_initiation_year}_low_priority : ${input.objective_initiation_year} (Low Priority)`,
                                  `${input.objective_initiation_year}_high_priority : ${input.objective_initiation_year} (High Priority)`,
                                  `${input.objective_initiation_year + 1}_low_priority : ${input.objective_initiation_year + 1} (Low Priority)`,
                                  `${input.objective_initiation_year + 1}_high_priority : ${input.objective_initiation_year + 1} (High Priority)`
                               ];
                               """);
    }
}
