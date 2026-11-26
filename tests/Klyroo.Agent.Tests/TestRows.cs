using Klyroo.Spreadsheet;

namespace Klyroo.Agent.Tests;

/// <summary>The row class of the spreadsheet tests; the column keys are the lowercase names the earlier dictionary schema used.</summary>
public sealed class PersonRow
{
    public string? Name { get; set; }

    public double? Age { get; set; }

    public string? Team { get; set; }

    public string? Id { get; set; }
}

internal static class TestSheets
{
    /// <summary>The people schema: a required name, an age number, a team select with two options and a read-only id.</summary>
    public static SpreadsheetSchema<PersonRow> People(
        bool requireYears = false,
        Func<string, SpreadsheetOption<string?>>? create = null,
        Func<IReadOnlyList<SpreadsheetRow<PersonRow>>, Task<IReadOnlyList<SpreadsheetRejectedRow>?>>? onSubmit = null,
        string key = "people") =>
        SpreadsheetSchema.For<PersonRow>(s =>
        {
            s.Key = key;
            s.OnSubmit = onSubmit;
            s.Column(r => r.Name, c => c.Key("name").Label("Name").Required().Headers("Full name"));
            s.Column(r => r.Age, c => c.Key("age").Label("Age").Required(requireYears).Headers("Years"));
            s.Column(r => r.Team, c =>
            {
                c.Key("team").Label("Team").Options([new SpreadsheetOption<string?>("engine", "Engine"), new SpreadsheetOption<string?>("ops", "Operations")]);
                if (create is not null) c.OnCreateOption(create);
            });
            s.Column(r => r.Id, c => c.Key("id").Label("Id").Editable(false).Headers("Never"));
        });

    /// <summary>A single text column named <c>a</c> (header <c>A</c>) under the given schema key.</summary>
    public static SpreadsheetSchema<PersonRow> OneColumn(string key) =>
        SpreadsheetSchema.For<PersonRow>(s =>
        {
            s.Key = key;
            s.Column(r => r.Name, c => c.Key("a").Label("A"));
        });

    /// <summary>One <c>name</c> column under the given schema key.</summary>
    public static SpreadsheetSchema<PersonRow> Names(
        string key,
        bool required = false,
        Func<IReadOnlyList<SpreadsheetRow<PersonRow>>, Task<IReadOnlyList<SpreadsheetRejectedRow>?>>? onSubmit = null) =>
        SpreadsheetSchema.For<PersonRow>(s =>
        {
            s.Key = key;
            s.OnSubmit = onSubmit;
            s.Column(r => r.Name, c => c.Key("name").Label("Name").Required(required));
        });
}
