using System.Text.Json.Serialization;

namespace Klyroo.Agent;

/// <summary>
/// One record of a collection a component pages through with <see cref="AgentQueryRequest.Collection"/>. The subtype
/// depends on the collection: <see cref="AgentRowRecord"/> for <c>rows</c>, <see cref="AgentIssueRecord"/> for
/// <c>issues</c>, <see cref="AgentColumnRecord"/> for <c>columns</c>, <see cref="AgentHeaderRecord"/> for <c>headers</c> and
/// <see cref="AgentQuestionRecord"/> for <c>questions</c>. The JSON an agent reads has the properties of the subtype, in camel case.
/// </summary>
[JsonDerivedType(typeof(AgentRowRecord))]
[JsonDerivedType(typeof(AgentIssueRecord))]
[JsonDerivedType(typeof(AgentColumnRecord))]
[JsonDerivedType(typeof(AgentHeaderRecord))]
[JsonDerivedType(typeof(AgentQuestionRecord))]
public abstract record AgentRecord;

/// <summary>A data row of a spreadsheet import, from the <c>rows</c> collection.</summary>
/// <param name="RowNumber">The number the user sees for the row in the file.</param>
/// <param name="Status">The row status: <c>valid</c>, <c>warning</c> or <c>blocking</c>.</param>
/// <param name="Importable">Whether the row would be imported.</param>
/// <param name="DiscardReason">Why the row is left out of the import, or <see langword="null"/>.</param>
/// <param name="Edited">Whether a cell of the row was edited.</param>
/// <param name="Cells">The text of each cell, keyed by column key.</param>
/// <param name="Issues">What is wrong with the row, empty when nothing is.</param>
public sealed record AgentRowRecord(
    int RowNumber,
    string Status,
    bool Importable,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? DiscardReason,
    bool Edited,
    IReadOnlyDictionary<string, string> Cells,
    IReadOnlyList<AgentIssueRecord> Issues) : AgentRecord;

/// <summary>An issue of a spreadsheet import, from the <c>issues</c> collection or the <see cref="AgentRowRecord.Issues"/> of a row.</summary>
/// <param name="Field">The key of the column the issue is about, or <see langword="null"/> for a row issue.</param>
/// <param name="Code">The stable code of the issue, such as <c>number</c> or <c>value.unknown</c>.</param>
/// <param name="Level">The level: <c>error</c> or <c>warning</c>.</param>
/// <param name="Message">What is wrong, in words.</param>
/// <param name="RowNumber">The row number the issue belongs to. Set in the <c>issues</c> collection, <see langword="null"/> inside a row.</param>
public sealed record AgentIssueRecord(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Field,
    string Code,
    string Level,
    string Message,
    int? RowNumber = null) : AgentRecord;

/// <summary>A column of a spreadsheet import, from the <c>columns</c> collection.</summary>
/// <param name="Key">The column key.</param>
/// <param name="Label">The label shown to the user.</param>
/// <param name="Required">Whether the column must have a value.</param>
/// <param name="Editable">Whether <c>editCell</c> may change it.</param>
/// <param name="Status">The mapping status: <c>matched</c>, <c>suggested</c>, <c>unmatched</c>, <c>ignored</c> or another status of the importer.</param>
/// <param name="HeaderIndex">The 0-based index of the file column assigned to it, or <see langword="null"/>.</param>
/// <param name="Header">The text of that file column, or <see langword="null"/>.</param>
/// <param name="SuggestedHeader">The file column the importer suggests, or <see langword="null"/>.</param>
/// <param name="SuggestedHeaderIndex">The index of the suggested file column, or <see langword="null"/>.</param>
/// <param name="Samples">A few values found in the column.</param>
public sealed record AgentColumnRecord(
    string Key,
    string Label,
    bool Required,
    bool Editable,
    string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? HeaderIndex,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Header,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? SuggestedHeader,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] int? SuggestedHeaderIndex,
    IReadOnlyList<string> Samples) : AgentRecord;

/// <summary>A column of the loaded file, from the <c>headers</c> collection.</summary>
/// <param name="Index">The 0-based index of the file column, to pass to <c>assignColumn</c>.</param>
/// <param name="Header">The header text.</param>
/// <param name="UsedBy">The key of the column that uses it, or <see langword="null"/>.</param>
public sealed record AgentHeaderRecord(
    int Index,
    string Header,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? UsedBy) : AgentRecord;

/// <summary>A question the importer asks about an unknown value, from the <c>questions</c> collection.</summary>
/// <param name="Field">The key of the column the value is in.</param>
/// <param name="FieldLabel">The label of that column.</param>
/// <param name="RawValue">The value as it appears in the file.</param>
/// <param name="Open">Whether the question still needs an answer.</param>
/// <param name="Answer">The answer given (<c>Map</c>, <c>LeaveEmpty</c>, <c>SkipRows</c> or <c>Create</c>), or <see langword="null"/>.</param>
/// <param name="MappedValue">The value the answer maps to, or <see langword="null"/>.</param>
/// <param name="CanCreate">Whether a new value can be created for the column.</param>
/// <param name="RowCount">How many rows have the value.</param>
/// <param name="RowNumbers">The row numbers that have the value, at most 20.</param>
/// <param name="Choices">The values the answer can map to.</param>
public sealed record AgentQuestionRecord(
    string Field,
    string FieldLabel,
    string RawValue,
    bool Open,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? Answer,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? MappedValue,
    bool CanCreate,
    int RowCount,
    IReadOnlyList<int> RowNumbers,
    IReadOnlyList<AgentQuestionChoice> Choices) : AgentRecord;

/// <summary>One value an answer of an <see cref="AgentQuestionRecord"/> can map to.</summary>
/// <param name="Value">The value to pass to <c>answerValue</c>.</param>
/// <param name="Label">The text shown to the user.</param>
public sealed record AgentQuestionChoice(string Value, string Label);
