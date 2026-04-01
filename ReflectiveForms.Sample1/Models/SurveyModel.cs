// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using Newtonsoft.Json;
using ReflectiveForms.Core.Attributes;
using ReflectiveForms.Core.Attributes.Fields;
using ReflectiveForms.Core.Enums;
using ReflectiveForms.Core.Models;

namespace ReflectiveForms.Sample1.Models;

// ──────────────────────────────────────────────────────────────────
// Survey Entity — Deeply nested repeaters with display conditions
//
// Structure (3 levels deep):
//   Survey
//     └─ sections[]                  (Repeater, min 1 / max 10)
//          ├─ section_title          (Text, mandatory)
//          ├─ section_description    (TextArea)
//          ├─ has_scoring            (Checkbox) ← controls display
//          ├─ passing_score          (Number, display: has_scoring == true)
//          ├─ scoring_mode           (Select, display: has_scoring == true)
//          ├─ score_explanation      (TextArea, display: scoring_mode == weighted)
//          └─ questions[]            (Repeater, min 1 / max 20)
//               ├─ question_text     (TextArea, mandatory)
//               ├─ question_type     (Select: text/choice/rating)
//               ├─ is_required       (Checkbox)
//               ├─ help_text         (Text, display: is_required == true)
//               ├─ min_rating        (Number, display: question_type == rating)
//               ├─ max_rating        (Number, display: question_type == rating)
//               └─ choices[]         (Repeater, display: question_type == choice,
//                                     min 2 / max 8)
//                    ├─ choice_label (Text, mandatory)
//                    ├─ is_correct   (Checkbox)
//                    └─ choice_score (Number, display: ../../has_scoring == true)
//                                    ^ uses ancestor field reference
// ──────────────────────────────────────────────────────────────────

/// <summary>
/// Level 3: Individual choice within a question
/// </summary>
[StickyTitle("choice_label")]
internal class SurveyChoiceModel : BaseModel
{
    [JsonProperty("choice_label"),
     Text(
         label: "Choice Label",
         instructions: "Text displayed to the respondent for this choice.",
         mandatory: true,
         placeholderText: "e.g. Strongly Agree")]
    public string ChoiceLabel = "";

    [JsonProperty("is_correct"),
     Checkbox(
         label: "Correct Answer",
         instructions: "Mark this choice as a correct answer (for scored surveys).",
         defaultValue: false)]
    public bool IsCorrect;

    [JsonProperty("choice_score"),
     Number(
         label: "Choice Score",
         instructions: "Points awarded when this choice is selected.",
         mandatory: false,
         placeholderText: "0",
         minimumMaximumValues: [0, 100],
         stepSize: 1)]
    public double ChoiceScore;
}

/// <summary>
/// Level 2: Individual question within a section
/// </summary>
[StickyTitle("question_text")]
internal class SurveyQuestionModel : BaseModel
{
    [JsonProperty("question_text"),
     TextArea(
         label: "Question Text",
         instructions: "The full question as displayed to the respondent.",
         mandatory: true,
         placeholderText: "Enter the question here...")]
    public string QuestionText = "";

    [JsonProperty("question_type"),
     Select(
         label: "Question Type",
         instructions: "Determines what kind of input the respondent sees.",
         defaultValue: "text",
         choices:
         [
             "text : Free Text",
             "choice : Multiple Choice",
             "rating : Rating Scale"
         ])]
    public string QuestionType = "text";

    [JsonProperty("is_required"),
     Checkbox(
         label: "Required",
         instructions: "If checked, the respondent must answer this question.",
         defaultValue: false)]
    public bool IsRequired;

    [JsonProperty("help_text"),
     DisplayCondition("is_required == true"),
     Text(
         label: "Help Text",
         instructions: "Guidance shown next to the question when it is required.",
         mandatory: false,
         placeholderText: "e.g. This question is mandatory.")]
    public string HelpText = "";

    [JsonProperty("min_rating"),
     DisplayCondition("question_type == rating"),
     Number(
         label: "Min Rating",
         instructions: "Lowest value on the rating scale.",
         mandatory: false,
         placeholderText: "1",
         minimumMaximumValues: [0, 10],
         stepSize: 1)]
    public double MinRating;

    [JsonProperty("max_rating"),
     DisplayCondition("question_type == rating"),
     Number(
         label: "Max Rating",
         instructions: "Highest value on the rating scale.",
         mandatory: false,
         placeholderText: "5",
         minimumMaximumValues: [1, 100],
         stepSize: 1)]
    public double MaxRating;

    [JsonProperty("choices", NullValueHandling = NullValueHandling.Ignore),
     DisplayCondition("question_type == choice"),
     Repeater(
         label: "Choices",
         instructions: "Define the answer choices for this question. Minimum 2, maximum 8.",
         repeaterFor: typeof(SurveyChoiceModel),
         addButtonLabel: "Add Choice",
         minimumRows: 2,
         maximumRows: 8,
         groupRenderStyle: GroupRenderStyle.Grid3ElementsInRow,
         useAccordion: RepeatUseAccordion.No)]
    public List<SurveyChoiceModel>? Choices = null;
}

/// <summary>
/// Level 1: Section containing questions
/// </summary>
[StickyTitle("section_title")]
internal class SurveySectionModel : BaseModel
{
    [JsonProperty("section_title"),
     Text(
         label: "Section Title",
         instructions: "Name of this section as shown to the respondent.",
         mandatory: true,
         placeholderText: "e.g. Demographics")]
    public string SectionTitle = "";

    [JsonProperty("section_description"),
     TextArea(
         label: "Section Description",
         instructions: "Optional introductory text shown at the top of this section.",
         mandatory: false,
         placeholderText: "")]
    public string SectionDescription = "";

    [JsonProperty("has_scoring"),
     Checkbox(
         label: "Enable Scoring",
         instructions: "If checked, questions in this section can have point values.",
         defaultValue: false)]
    public bool HasScoring;

    [JsonProperty("passing_score"),
     DisplayCondition("has_scoring == true"),
     Number(
         label: "Passing Score",
         instructions: "Minimum total points needed to pass this section.",
         mandatory: false,
         placeholderText: "70",
         minimumMaximumValues: [0, 10000],
         stepSize: 1)]
    public double PassingScore;

    [JsonProperty("scoring_mode"),
     DisplayCondition("has_scoring == true"),
     Select(
         label: "Scoring Mode",
         instructions: "How question scores are combined.",
         defaultValue: "simple",
         choices:
         [
             "simple : Simple Sum",
             "weighted : Weighted Average"
         ])]
    public string ScoringMode = "simple";

    [JsonProperty("score_explanation"),
     DisplayCondition("scoring_mode == weighted"),
     TextArea(
         label: "Weighting Explanation",
         instructions: "Describe how weights are applied to question scores.",
         mandatory: false,
         placeholderText: "Explain the weighting formula...")]
    public string ScoreExplanation = "";

    [JsonProperty("questions", NullValueHandling = NullValueHandling.Ignore),
     Repeater(
         label: "Questions",
         instructions: "Add the questions for this section. At least one is required.",
         repeaterFor: typeof(SurveyQuestionModel),
         addButtonLabel: "Add Question",
         minimumRows: 1,
         maximumRows: 20,
         useAccordion: RepeatUseAccordion.Yes)]
    public List<SurveyQuestionModel> Questions = [];
}

/// <summary>
/// Root entity: Survey
/// </summary>
internal class SurveyModel : EntityFieldsModel
{
    [JsonProperty("survey_description"),
     TextArea(
         label: "Survey Description",
         instructions: "A summary of what this survey is about.",
         mandatory: true,
         placeholderText: "Describe the purpose of this survey...")]
    public string SurveyDescription = "";

    [JsonProperty("is_anonymous"),
     Checkbox(
         label: "Anonymous Responses",
         instructions: "If checked, respondent identity is not collected.",
         defaultValue: false)]
    public bool IsAnonymous;

    [JsonProperty("response_limit"),
     DisplayCondition("is_anonymous == false"),
     Number(
         label: "Response Limit per Person",
         instructions: "Maximum number of times a single person may respond.",
         mandatory: false,
         placeholderText: "1",
         minimumMaximumValues: [1, 100],
         stepSize: 1)]
    public double ResponseLimit;

    [JsonProperty("due_date"),
     DatePicker(
         label: "Due Date",
         instructions: "Deadline for survey responses.",
         mandatory: false,
         dateFormat: "yyyyMMdd")]
    public string DueDate = "";

    [JsonProperty("survey_status"),
     Select(
         label: "Survey Status",
         instructions: "Current lifecycle status of this survey.",
         defaultValue: "draft",
         choices:
         [
             "draft : Draft",
             "active : Active",
             "closed : Closed"
         ])]
    public string SurveyStatus = "draft";

    [JsonProperty("sections", NullValueHandling = NullValueHandling.Ignore),
     Repeater(
         label: "Sections",
         instructions: "Organize your survey into sections. At least one section is required.",
         repeaterFor: typeof(SurveySectionModel),
         addButtonLabel: "Add Section",
         minimumRows: 1,
         maximumRows: 10,
         useAccordion: RepeatUseAccordion.Yes)]
    public List<SurveySectionModel> Sections = [];
}
