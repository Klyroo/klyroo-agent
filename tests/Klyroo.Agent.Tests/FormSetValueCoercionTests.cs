using System.Text.Json;
using Xunit;

namespace Klyroo.Agent.Tests;

public sealed class FormSetValueCoercionTests
{
    private static readonly FormOptionItem[] Fruits =
    [
        new("apple", "Apple"),
        new("pear", "Poire"),
        new("fig", "Fig", Disabled: true),
    ];

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static (AgentBridgeService Agent, DynamicFormController Form) Setup(DynamicFieldSchema field, IReadOnlyCollection<string>? allowSensitiveWrites = null)
    {
        var agent = new AgentBridgeService();
        var form = new DynamicFormController(new DynamicFormSchema { FormKey = "f", Title = "Form", Fields = [field] });
        agent.RegisterForm(form, allowSensitiveWrites: allowSensitiveWrites);
        return (agent, form);
    }

    private static async Task<AgentActResult> Set(AgentBridgeService agent, AgentArgument value) =>
        await agent.ActAsync([new AgentActStep("F", "setValue", [value])]);

    [Fact]
    public async Task Text_kinds_unwrap_a_JsonElement_string_and_reject_lists_and_overlong_text()
    {
        var (agent, form) = Setup(new DynamicFieldSchema { Key = "f", Label = "F", MaxLength = 5 });

        Assert.True((await Set(agent, Json("\"Ada\""))).Ok);
        Assert.Equal("Ada", form.GetValue("f"));
        Assert.IsType<string>(form.GetValue("f"));

        Assert.True((await Set(agent, Json("42"))).Ok);
        Assert.Equal("42", form.GetValue("f"));

        var tooLong = await Set(agent, Json("\"Lovelace\""));
        Assert.False(tooLong.Ok);
        Assert.Contains("at most 5", tooLong.Error);
        Assert.Equal("42", form.GetValue("f"));

        var list = await Set(agent, Json("[\"a\"]"));
        Assert.False(list.Ok);
        Assert.Contains("expects text", list.Error);

        var obj = await Set(agent, Json("{\"a\":1}"));
        Assert.False(obj.Ok);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("\"yes\"", true)]
    [InlineData("\"Off\"", false)]
    [InlineData("1", true)]
    public async Task Checkbox_and_switch_store_a_bool(string json, bool expected)
    {
        foreach (var kind in new[] { FormFieldKind.Checkbox, FormFieldKind.Switch })
        {
            var (agent, form) = Setup(new DynamicFieldSchema { Key = "f", Label = "F", Kind = kind, DefaultValue = !expected });
            var result = await Set(agent, Json(json));
            Assert.True(result.Ok, result.Error);
            Assert.Equal(expected, form.GetValue("f"));
            Assert.IsType<bool>(form.GetValue("f"));
        }
    }

    [Theory]
    [InlineData("\"maybe\"")]
    [InlineData("2")]
    [InlineData("null")]
    [InlineData("[true]")]
    public async Task Checkbox_rejects_values_that_are_not_boolean(string json)
    {
        var (agent, form) = Setup(new DynamicFieldSchema { Key = "f", Label = "F", Kind = FormFieldKind.Checkbox, DefaultValue = false });
        var result = await Set(agent, Json(json));
        Assert.False(result.Ok);
        Assert.Contains("boolean", result.Error);
        Assert.Equal(false, form.GetValue("f"));
    }

    [Theory]
    [InlineData("12.5", 12.5)]
    [InlineData("\"12.5\"", 12.5)]
    [InlineData("\"-3\"", -3.0)]
    [InlineData("\"1e2\"", 100.0)]
    public async Task Number_parses_numbers_and_numeric_text_with_the_invariant_culture(string json, double expected)
    {
        var (agent, form) = Setup(new DynamicFieldSchema { Key = "f", Label = "F", Kind = FormFieldKind.Number });
        var result = await Set(agent, Json(json));
        Assert.True(result.Ok, result.Error);
        Assert.Equal(expected, form.GetValue("f"));
        Assert.IsType<double>(form.GetValue("f"));
    }

    [Theory]
    [InlineData("\"1,5\"")]
    [InlineData("\"12 apples\"")]
    [InlineData("\"NaN\"")]
    [InlineData("\"Infinity\"")]
    [InlineData("true")]
    [InlineData("[1]")]
    public async Task Number_rejects_text_that_is_not_a_finite_number(string json)
    {
        var (agent, form) = Setup(new DynamicFieldSchema { Key = "f", Label = "F", Kind = FormFieldKind.Number, DefaultValue = 7.0 });
        var result = await Set(agent, Json(json));
        Assert.False(result.Ok);
        Assert.Contains("finite number", result.Error);
        Assert.Equal(7.0, form.GetValue("f"));
    }

    [Fact]
    public async Task Number_and_slider_enforce_min_and_max()
    {
        foreach (var kind in new[] { FormFieldKind.Number, FormFieldKind.Slider })
        {
            var (agent, form) = Setup(new DynamicFieldSchema { Key = "f", Label = "F", Kind = kind, Min = 1, Max = 10 });
            Assert.False((await Set(agent, Json("0"))).Ok);
            Assert.False((await Set(agent, Json("11"))).Ok);
            Assert.True((await Set(agent, Json("10"))).Ok);
            Assert.Equal(10.0, form.GetValue("f"));
        }
    }

    [Fact]
    public async Task Rating_requires_a_whole_number_within_the_range()
    {
        var (agent, form) = Setup(new DynamicFieldSchema { Key = "f", Label = "F", Kind = FormFieldKind.Rating, Max = 5 });
        Assert.True((await Set(agent, Json("4"))).Ok);
        Assert.Equal(4.0, form.GetValue("f"));
        Assert.False((await Set(agent, Json("4.5"))).Ok);
        Assert.False((await Set(agent, Json("6"))).Ok);
        Assert.Equal(4.0, form.GetValue("f"));
    }

    [Fact]
    public async Task Date_is_stored_as_yyyy_MM_dd_and_rejects_other_notations()
    {
        var (agent, form) = Setup(new DynamicFieldSchema { Key = "f", Label = "F", Kind = FormFieldKind.Date });
        Assert.True((await Set(agent, Json("\"2026-03-27\""))).Ok);
        Assert.Equal("2026-03-27", form.GetValue("f"));
        Assert.True((await Set(agent, Json("\"2026-04-01T09:30:00\""))).Ok);
        Assert.Equal("2026-04-01", form.GetValue("f"));

        foreach (var bad in new[] { "\"3/4/2026\"", "\"27 March 2026\"", "\"2026-02-30\"", "20260327", "true" })
        {
            var result = await Set(agent, Json(bad));
            Assert.False(result.Ok, bad);
            Assert.Contains("yyyy-MM-dd", result.Error);
        }
        Assert.Equal("2026-04-01", form.GetValue("f"));
    }

    [Fact]
    public async Task DateTime_and_time_use_the_formats_of_the_html_inputs()
    {
        var (agent, form) = Setup(new DynamicFieldSchema { Key = "f", Label = "F", Kind = FormFieldKind.DateTime });
        Assert.True((await Set(agent, Json("\"2026-03-27 14:30\""))).Ok);
        Assert.Equal("2026-03-27T14:30", form.GetValue("f"));
        Assert.True((await Set(agent, Json("\"2026-03-27T14:30:15\""))).Ok);
        Assert.Equal("2026-03-27T14:30:15", form.GetValue("f"));
        Assert.False((await Set(agent, Json("\"2026-03-27T14:30:00Z\""))).Ok);
        Assert.False((await Set(agent, Json("\"tomorrow\""))).Ok);

        var (timeAgent, timeForm) = Setup(new DynamicFieldSchema { Key = "f", Label = "F", Kind = FormFieldKind.Time });
        Assert.True((await Set(timeAgent, Json("\"09:05\""))).Ok);
        Assert.Equal("09:05", timeForm.GetValue("f"));
        Assert.True((await Set(timeAgent, Json("\"09:05:30\""))).Ok);
        Assert.Equal("09:05:30", timeForm.GetValue("f"));
        Assert.False((await Set(timeAgent, Json("\"25:00\""))).Ok);
        Assert.False((await Set(timeAgent, Json("\"9am\""))).Ok);
    }

    [Fact]
    public async Task DateRange_needs_two_ordered_dates()
    {
        var (agent, form) = Setup(new DynamicFieldSchema { Key = "f", Label = "F", Kind = FormFieldKind.DateRange });
        Assert.True((await Set(agent, Json("[\"2026-01-01\",\"2026-01-31\"]"))).Ok);
        Assert.Equal(["2026-01-01", "2026-01-31"], Assert.IsAssignableFrom<IReadOnlyList<string>>(form.GetValue("f")));

        Assert.False((await Set(agent, Json("[\"2026-02-01\",\"2026-01-31\"]"))).Ok);
        Assert.False((await Set(agent, Json("[\"2026-01-01\"]"))).Ok);
        Assert.False((await Set(agent, Json("\"2026-01-01\""))).Ok);
        Assert.False((await Set(agent, Json("[\"2026-01-01\",\"soon\"]"))).Ok);
    }

    [Fact]
    public async Task ColorPicker_requires_a_hex_color_and_lowercases_it()
    {
        var (agent, form) = Setup(new DynamicFieldSchema { Key = "f", Label = "F", Kind = FormFieldKind.ColorPicker });
        Assert.True((await Set(agent, Json("\"#1A2B3C\""))).Ok);
        Assert.Equal("#1a2b3c", form.GetValue("f"));
        Assert.False((await Set(agent, Json("\"red\""))).Ok);
        Assert.False((await Set(agent, Json("\"#12\""))).Ok);
    }

    [Theory]
    [InlineData(FormFieldKind.Select)]
    [InlineData(FormFieldKind.Radio)]
    public async Task Single_choice_kinds_match_an_option_by_value_or_normalized_label(FormFieldKind kind)
    {
        var (agent, form) = Setup(new DynamicFieldSchema { Key = "f", Label = "F", Kind = kind, Options = Fruits });

        Assert.True((await Set(agent, Json("\"pear\""))).Ok);
        Assert.Equal("pear", form.GetValue("f"));

        // A label stores the option value, ignoring case and accents.
        Assert.True((await Set(agent, Json("\"  APPLE \""))).Ok);
        Assert.Equal("apple", form.GetValue("f"));
        Assert.True((await Set(agent, Json("\"poire\""))).Ok);
        Assert.Equal("pear", form.GetValue("f"));

        var unknown = await Set(agent, Json("\"banana\""));
        Assert.False(unknown.Ok);
        Assert.Contains("not one of the options", unknown.Error);

        var disabled = await Set(agent, Json("\"fig\""));
        Assert.False(disabled.Ok);
        Assert.Contains("disabled", disabled.Error);

        var many = await Set(agent, Json("[\"apple\",\"pear\"]"));
        Assert.False(many.Ok);

        Assert.Equal("pear", form.GetValue("f"));
    }

    [Fact]
    public async Task AutoComplete_matches_an_option_and_keeps_other_text_as_typed_like_its_input()
    {
        var (agent, form) = Setup(new DynamicFieldSchema { Key = "f", Label = "F", Kind = FormFieldKind.AutoComplete, Options = Fruits, MaxLength = 10 });

        Assert.True((await Set(agent, Json("\"poire\""))).Ok);
        Assert.Equal("pear", form.GetValue("f"));
        Assert.True((await Set(agent, Json("\"banana\""))).Ok);
        Assert.Equal("banana", form.GetValue("f"));
        Assert.False((await Set(agent, Json("\"a very long fruit name\""))).Ok);
        Assert.False((await Set(agent, Json("[\"apple\",\"pear\"]"))).Ok);
        Assert.Equal("banana", form.GetValue("f"));
    }

    [Fact]
    public async Task Single_choice_with_an_empty_value_clears_the_selection()
    {
        var (agent, form) = Setup(new DynamicFieldSchema { Key = "f", Label = "F", Kind = FormFieldKind.Select, Options = Fruits, DefaultValue = "apple" });
        Assert.True((await Set(agent, Json("null"))).Ok);
        Assert.Null(form.GetValue("f"));
    }

    [Fact]
    public async Task A_label_shared_by_two_options_is_ambiguous_and_the_value_still_works()
    {
        var (agent, form) = Setup(new DynamicFieldSchema
        {
            Key = "f", Label = "F", Kind = FormFieldKind.Select,
            Options = [new("a1", "Paris"), new("a2", "Paris")],
        });
        var ambiguous = await Set(agent, Json("\"paris\""));
        Assert.False(ambiguous.Ok);
        Assert.Contains("a1, a2", ambiguous.Error);
        Assert.True((await Set(agent, Json("\"a2\""))).Ok);
        Assert.Equal("a2", form.GetValue("f"));
    }

    [Fact]
    public async Task Options_that_come_from_a_factory_are_checked_against_the_current_list()
    {
        var allowed = new List<FormOptionItem> { new("a", "A") };
        var (agent, form) = Setup(new DynamicFieldSchema
        {
            Key = "f", Label = "F", Kind = FormFieldKind.Select,
            OptionsFactory = _ => allowed,
        });
        Assert.False((await Set(agent, Json("\"b\""))).Ok);
        allowed.Add(new("b", "B"));
        Assert.True((await Set(agent, Json("\"b\""))).Ok);
        Assert.Equal("b", form.GetValue("f"));
    }

    [Theory]
    [InlineData(FormFieldKind.CheckboxGroup, false)]
    [InlineData(FormFieldKind.Select, true)]
    [InlineData(FormFieldKind.AutoComplete, true)]
    public async Task Multiple_choice_kinds_take_a_list_of_known_options(FormFieldKind kind, bool multipleFlag)
    {
        var (agent, form) = Setup(new DynamicFieldSchema { Key = "f", Label = "F", Kind = kind, Multiple = multipleFlag, Options = Fruits });

        var ok = await Set(agent, Json("[\"apple\",\"Poire\",\"apple\"]"));
        Assert.True(ok.Ok, ok.Error);
        Assert.Equal(["apple", "pear"], Assert.IsAssignableFrom<IReadOnlyList<string>>(form.GetValue("f")));

        // A single value becomes a one-element list.
        Assert.True((await Set(agent, Json("\"pear\""))).Ok);
        Assert.Equal(["pear"], Assert.IsAssignableFrom<IReadOnlyList<string>>(form.GetValue("f")));

        var bad = await Set(agent, Json("[\"apple\",\"banana\"]"));
        Assert.False(bad.Ok);
        Assert.Equal(["pear"], Assert.IsAssignableFrom<IReadOnlyList<string>>(form.GetValue("f")));

        Assert.False((await Set(agent, Json("[\"fig\"]"))).Ok);
        Assert.False((await Set(agent, Json("[1,true]"))).Ok);

        Assert.True((await Set(agent, Json("[]"))).Ok);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<string>>(form.GetValue("f")));
    }

    [Fact]
    public async Task Tag_takes_trimmed_distinct_text_items()
    {
        var (agent, form) = Setup(new DynamicFieldSchema { Key = "f", Label = "F", Kind = FormFieldKind.Tag });
        Assert.True((await Set(agent, Json("[\" x \",\"y\",\"x\",\"\"]"))).Ok);
        Assert.Equal(["x", "y"], Assert.IsAssignableFrom<IReadOnlyList<string>>(form.GetValue("f")));
        Assert.False((await Set(agent, Json("[1]"))).Ok);
    }

    [Fact]
    public async Task Repeatable_lists_cannot_be_set_with_setValue()
    {
        var (agent, _) = Setup(new DynamicFieldSchema
        {
            Key = "f", Label = "F", Kind = FormFieldKind.ArrayList,
            Fields = [new DynamicFieldSchema { Key = "c", Label = "C" }],
        });
        var result = await Set(agent, Json("[]"));
        Assert.False(result.Ok);
        Assert.Contains("repeatable list", result.Error);
    }

    [Fact]
    public async Task Plain_CLR_values_keep_working_for_callers_that_do_not_use_JSON()
    {
        var (agent, form) = Setup(new DynamicFieldSchema { Key = "f", Label = "F", Kind = FormFieldKind.Number });
        Assert.True((await Set(agent, 3)).Ok);
        Assert.Equal(3.0, form.GetValue("f"));
        Assert.True((await Set(agent, 2.5m)).Ok);
        Assert.Equal(2.5, form.GetValue("f"));
    }

    [Fact]
    public async Task A_rejected_value_does_not_touch_the_form_or_notify_observers()
    {
        var (agent, form) = Setup(new DynamicFieldSchema { Key = "f", Label = "F", Kind = FormFieldKind.Select, Options = Fruits, DefaultValue = "apple" });
        var observed = new List<ObservedChange>();
        using var sub = agent.Observe(observed.Add);
        var changes = 0;
        form.Changed += () => changes++;

        var result = await Set(agent, Json("\"banana\""));

        Assert.False(result.Ok);
        Assert.Empty(observed);
        Assert.Equal(0, changes);
        Assert.Equal("apple", form.GetValue("f"));
    }

    [Fact]
    public async Task Sensitive_text_fields_still_redact_after_a_coerced_set()
    {
        var (agent, form) = Setup(new DynamicFieldSchema { Key = "f", Label = "F", Kind = FormFieldKind.Password }, allowSensitiveWrites: ["f"]);
        var result = await Set(agent, Json("\"s3cret!\""));
        Assert.True(result.Ok);
        Assert.Equal("s3cret!", form.GetValue("f"));
        Assert.Equal(AgentValue.From(AgentBridgeService.RedactedValue), result.Values.Values.Single());
    }

    [Theory]
    [InlineData(FormFieldKind.Checkbox, false, "bool")]
    [InlineData(FormFieldKind.Switch, false, "bool")]
    [InlineData(FormFieldKind.CheckboxGroup, false, "list")]
    [InlineData(FormFieldKind.Tag, false, "list")]
    [InlineData(FormFieldKind.Select, true, "list")]
    [InlineData(FormFieldKind.AutoComplete, true, "list")]
    [InlineData(FormFieldKind.Select, false, "null")]
    [InlineData(FormFieldKind.Radio, false, "null")]
    [InlineData(FormFieldKind.Number, false, "null")]
    [InlineData(FormFieldKind.Date, false, "null")]
    [InlineData(FormFieldKind.Text, false, "null")]
    public async Task Clear_stores_the_empty_value_of_the_field_kind(FormFieldKind kind, bool multiple, string expected)
    {
        object? start = kind switch
        {
            FormFieldKind.Checkbox or FormFieldKind.Switch => true,
            _ when expected == "list" => new List<string> { "apple" },
            FormFieldKind.Number => 5.0,
            _ => "apple",
        };
        var (agent, form) = Setup(new DynamicFieldSchema { Key = "f", Label = "F", Kind = kind, Multiple = multiple, Options = Fruits, DefaultValue = start });

        var result = await agent.ActAsync([new AgentActStep("F", "clear")]);

        Assert.True(result.Ok, result.Error);
        var value = form.GetValue("f");
        switch (expected)
        {
            case "bool": Assert.Equal(false, value); break;
            case "list": Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<string>>(value)); break;
            default: Assert.Null(value); break;
        }
    }

    [Fact]
    public async Task Clear_is_refused_on_a_repeatable_list()
    {
        var (agent, _) = Setup(new DynamicFieldSchema { Key = "f", Label = "F", Kind = FormFieldKind.ArrayList, Fields = [new DynamicFieldSchema { Key = "c", Label = "C" }] });
        var result = await agent.ActAsync([new AgentActStep("F", "clear")]);
        Assert.False(result.Ok);
        Assert.Contains("no value to clear", result.Error);
    }

    [Theory]
    [InlineData("12345678901234567890", "12345678901234567890")]
    [InlineData("9223372036854775807", "9223372036854775807")]
    [InlineData("-42", "-42")]
    [InlineData("1.50", "1.50")]
    [InlineData("0.1000000000000000055511151231257827", "0.1000000000000000055511151231257827")]
    [InlineData("123456789012345678901234567890123456789", "123456789012345678901234567890123456789")]
    [InlineData("1e3", "1e3")]
    [InlineData("-0.0", "-0.0")]
    public async Task A_text_field_gets_the_exact_digits_of_a_JSON_number(string json, string expected)
    {
        var (agent, form) = Setup(new DynamicFieldSchema { Key = "f", Label = "F" });

        var result = await Set(agent, Json(json));

        Assert.True(result.Ok, result.Error);
        Assert.Equal(expected, form.GetValue("f"));
    }

    [Fact]
    public async Task A_numeric_field_still_stores_a_double_for_large_and_fractional_numbers()
    {
        var (agent, form) = Setup(new DynamicFieldSchema { Key = "f", Label = "F", Kind = FormFieldKind.Number });
        Assert.True((await Set(agent, Json("9223372036854775807"))).Ok);
        Assert.Equal(9223372036854775807d, form.GetValue("f"));
        Assert.True((await Set(agent, Json("0.1"))).Ok);
        Assert.Equal(0.1, form.GetValue("f"));
        Assert.True((await Set(agent, Json("1e3"))).Ok);
        Assert.Equal(1000.0, form.GetValue("f"));
        Assert.True((await Set(agent, Json("123456789012345678901234567890123456789"))).Ok);
        Assert.Equal(1.2345678901234568e38, form.GetValue("f"));
        var overflow = await Set(agent, Json("1e400"));
        Assert.False(overflow.Ok);
        Assert.Equal(1.2345678901234568e38, form.GetValue("f"));
    }

    [Fact]
    public async Task A_number_matches_an_option_by_its_exact_text()
    {
        var (agent, form) = Setup(new DynamicFieldSchema { Key = "f", Label = "F", Kind = FormFieldKind.Select, Options = [new("1", "One"), new("20", "Twenty")] });
        Assert.True((await Set(agent, Json("20"))).Ok);
        Assert.Equal("20", form.GetValue("f"));
        Assert.False((await Set(agent, Json("2"))).Ok);
    }
}
