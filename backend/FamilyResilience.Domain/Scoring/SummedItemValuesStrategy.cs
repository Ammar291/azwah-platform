using System.Text.Json;
using FamilyResilience.Domain.Common;
using FamilyResilience.Domain.Questionnaires;

namespace FamilyResilience.Domain.Scoring;

/// <summary>
/// Sums item values. Forward items count their raw value; items the approved version marks Reverse count
/// (lowest + highest substantive value) − raw, which is 5 − raw on 1–4 and 6 − raw on 1–5. Non-substantive answers are
/// excluded under the approved policy. Category scores aggregate the same way; a tier is given only when the approved
/// parameters define bands.
/// </summary>
public sealed class SummedItemValuesStrategy : IAssessmentScoringStrategy
{
    public const string Code = "summed_item_values";

    public string PolicyCode => Code;

    public int AlgorithmVersion => 1;

    public Result<ScoringParameters> ValidateParameters(JsonElement parameters, ScoringContent content)
    {
        var read = SummedItemValuesParameters.Read(parameters, content);
        return read.IsFailure
            ? Result.Failure<ScoringParameters>(read.Error!)
            : Result.Success<ScoringParameters>(read.Value);
    }

    public Result<ScoringComputation> Score(ScoringInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Parameters is not SummedItemValuesParameters parameters)
        {
            throw new ArgumentException("The parameters were not produced by this strategy.", nameof(input));
        }

        var items = input.Content.Items.ToDictionary(item => item.ItemId);
        var responses = new Dictionary<InstrumentItemId, ScoringResponse>();
        foreach (var response in input.Responses)
        {
            if (!items.ContainsKey(response.ItemId) || !responses.TryAdd(response.ItemId, response))
            {
                return Result.Failure<ScoringComputation>(ScoringErrors.AnswerInvalid);
            }
        }

        var contributions = new List<ItemContribution>();
        var unanswered = new List<string>();
        foreach (var item in input.Content.Items)
        {
            if (item.Direction == ScoringDirection.Unspecified)
            {
                return Result.Failure<ScoringComputation>(About(ScoringErrors.ItemDirectionUnspecified, item.Code));
            }

            if (!item.IsScored)
            {
                continue;
            }

            if (!responses.TryGetValue(item.ItemId, out var response))
            {
                unanswered.Add(item.Code);
                continue;
            }

            var option = item.Options.FirstOrDefault(candidate => candidate.OptionId == response.OptionId);
            if (option is null || option.Value != response.RawValue)
            {
                return Result.Failure<ScoringComputation>(About(ScoringErrors.AnswerInvalid, item.Code));
            }

            contributions.Add(new ItemContribution(
                item.ItemId, response.RawValue, option.IsNonSubstantive ? null : item.PointsFor(response.RawValue), item.Category));
        }

        if (unanswered.Count > 0)
        {
            return Result.Failure<ScoringComputation>(About(ScoringErrors.UnansweredItems, string.Join(", ", unanswered)));
        }

        var excluded = contributions.Count(contribution => contribution.IsExcluded);
        if (excluded > 0)
        {
            if (parameters.NonSubstantive is not { } policy)
            {
                return Result.Failure<ScoringComputation>(ScoringErrors.DefinitionInvalid);
            }

            if ((decimal)excluded / contributions.Count > policy.MaxShare)
            {
                return Result.Success(Incomplete(contributions, ScoreIncompleteReasons.NonSubstantiveShareExceeded));
            }
        }

        decimal? total = null;
        if (parameters.Total)
        {
            total = Aggregate(contributions, parameters.NonSubstantive);
            if (total is null)
            {
                return Result.Success(Incomplete(contributions, ScoreIncompleteReasons.NoSubstantiveAnswers));
            }
        }

        var categories = new List<CategoryScore>();
        foreach (var group in contributions
                     .Where(contribution => contribution.Category is not null)
                     .GroupBy(contribution => contribution.Category ?? string.Empty, StringComparer.Ordinal)
                     .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var members = group.ToList();
            if (Aggregate(members, parameters.NonSubstantive) is not { } score)
            {
                return Result.Success(Incomplete(contributions, ScoreIncompleteReasons.NoSubstantiveAnswers));
            }

            categories.Add(new CategoryScore(group.Key, score, members.Count));
        }

        var tier = parameters.TierBands.Count > 0 && total is { } value
            ? parameters.TierBands.Single(band => band.Min <= value && value <= band.Max).Code
            : null;

        return Result.Success(new ScoringComputation(
            PolicyCode, AlgorithmVersion, ScoreStatus.Complete, null, total, tier, contributions, categories));
    }

    /// <summary>
    /// The plain sum when every item has a substantive answer; otherwise the mean of the substantive answers times the
    /// number of items, rounded as approved. Null when no item has a substantive answer.
    /// </summary>
    private static decimal? Aggregate(IReadOnlyCollection<ItemContribution> contributions, NonSubstantivePolicy? policy)
    {
        var answered = contributions.Select(contribution => contribution.CalculatedValue).OfType<decimal>().ToList();
        if (answered.Count == 0)
        {
            return null;
        }

        var sum = answered.Sum();
        if (answered.Count == contributions.Count)
        {
            return sum;
        }

        if (policy is null)
        {
            throw new InvalidOperationException("Excluded answers need an approved non-substantive policy.");
        }

        return decimal.Round(sum * contributions.Count / answered.Count, policy.RoundingDecimals, policy.RoundingMode);
    }

    private ScoringComputation Incomplete(IReadOnlyList<ItemContribution> contributions, string reason) =>
        new(PolicyCode, AlgorithmVersion, ScoreStatus.Incomplete, reason, null, null, contributions, []);

    private static Error About(Error error, string detail) => error with { Message = $"{error.Message} ({detail})" };
}
