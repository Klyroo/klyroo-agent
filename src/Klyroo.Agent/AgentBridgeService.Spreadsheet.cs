using System.Globalization;

namespace Klyroo.Agent;

public sealed partial class AgentBridgeService
{
    /// <summary>
    /// Registers an importer as one <c>spreadsheet</c> component that accepts <c>editCell</c>, <c>nextStep</c>,
    /// <c>previousStep</c>, <c>submit</c> and <c>clearFile</c>. Loading the file itself is not an agent action.
    /// </summary>
    /// <param name="importer">The importer to expose.</param>
    /// <returns>A handle that removes the importer's component when disposed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="importer"/> is <see langword="null"/>.</exception>
    public IDisposable RegisterSpreadsheet(SpreadsheetImporter importer)
    {
        ArgumentNullException.ThrowIfNull(importer);
        return AddTargets([new BoundTarget
        {
            Ref = NextRef(),
            Component = "spreadsheet",
            Label = importer.Schema.Key,
            GetValue = () => importer.CurrentStepKey,
            Actions = ["editCell", "nextStep", "previousStep", "submit", "clearFile"],
            GetChoices = () => [],
            RowOf = (action, args) => action == "editCell" && args.Count > 0 ? args[0]?.ToString() : null,
            ExecuteAction = async (action, args) =>
            {
                switch (action)
                {
                    case "editCell":
                        if (args.Count < 3) return false;
                        if (!int.TryParse(args[0]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rIdx)
                            || rIdx < 0 || rIdx >= importer.AllRows.Count) return false;
                        var fKey = args[1]?.ToString() ?? "";
                        if (!importer.Schema.Columns.Any(c => string.Equals(c.Key, fKey, StringComparison.Ordinal))) return false;
                        var text = args[2]?.ToString() ?? "";
                        importer.EditCell(rIdx, fKey, text);
                        return true;
                    case "nextStep":
                        return importer.NextStep();
                    case "previousStep":
                        return importer.PreviousStep();
                    case "submit":
                        await importer.SubmitAsync();
                        return importer.SubmitStatus == SpreadsheetSubmitStatus.Done;
                    case "clearFile":
                        importer.ClearFile();
                        return true;
                    default:
                        return false;
                }
            },
        }]);
    }
}
