using System.Text.RegularExpressions;
using BankingBrain.Application.Common.Interfaces;
using BankingBrain.Application.Common.Models;

namespace BankingBrain.Infrastructure.Processing;

public sealed class DeterministicSemanticExtractor : ISemanticExtractor
{
    private static readonly Regex TextRelation = new(
        @"^(?<subject>[A-Z][A-Za-z0-9&.' -]{1,80}?)\s+(?<predicate>owns|has|is located in|reports to|owes|manages|has rating|has balance|has revenue|has exposure)\s+(?<object>[^.]+)\.?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string Identity => "deterministic-rules";
    public string Version => "1.0";

    public Task<SemanticExtractionResult> ExtractAsync(
        SemanticExtractionInput input,
        CancellationToken cancellationToken)
    {
        var entities = new List<SemanticEntityCandidate>();
        var claims = new List<SemanticClaimCandidate>();
        var relationships = new List<SemanticRelationshipCandidate>();

        var cells = input.Sections.Where(section => section.Kind == "cell").ToList();
        ExtractStructured(cells, entities, claims, relationships, cancellationToken);
        ExtractText(input.Sections.Where(section => section.Kind is "text" or "paragraph" or "heading"), entities, claims, relationships, cancellationToken);

        return Task.FromResult(new SemanticExtractionResult(
            DeduplicateEntities(entities),
            DeduplicateClaims(claims),
            DeduplicateRelationships(relationships)));
    }

    private static void ExtractStructured(
        IReadOnlyList<NormalizedSection> cells,
        List<SemanticEntityCandidate> entities,
        List<SemanticClaimCandidate> claims,
        List<SemanticRelationshipCandidate> relationships,
        CancellationToken cancellationToken)
    {
        foreach (var sheetGroup in cells.GroupBy(cell => cell.SheetName ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            var rows = sheetGroup.GroupBy(cell => cell.RowNumber ?? 0).OrderBy(group => group.Key).ToList();
            if (rows.Count < 2) continue;
            var headerRow = rows[0].ToDictionary(
                cell => cell.ColumnNumber ?? 0,
                cell => (cell.Value ?? cell.Text ?? cell.Title ?? string.Empty).Trim(),
                EqualityComparer<int>.Default);
            if (headerRow.Count == 0) continue;

            var primaryHeader = headerRow.OrderBy(pair => pair.Key).First();
            foreach (var row in rows.Skip(1))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var rowCells = row.ToDictionary(cell => cell.ColumnNumber ?? 0);
                if (!rowCells.TryGetValue(primaryHeader.Key, out var primaryCell)) continue;
                var subject = (primaryCell.Value ?? primaryCell.Text ?? primaryCell.Title ?? string.Empty).Trim();
                if (subject.Length == 0) continue;
                var subjectType = InferEntityType(primaryHeader.Value);
                entities.Add(new SemanticEntityCandidate(subjectType, subject, Array.Empty<string>(), .85m, primaryCell.Sequence));

                foreach (var pair in rowCells)
                {
                    if (pair.Key == primaryHeader.Key || !headerRow.TryGetValue(pair.Key, out var predicate)) continue;
                    var value = (pair.Value.Value ?? pair.Value.Text ?? string.Empty).Trim();
                    if (value.Length == 0 || predicate.Length == 0) continue;
                                var normalizedPredicate = NormalizePredicate(predicate);
                                var financial = ParseFinancialValue(value, normalizedPredicate);
                                claims.Add(new SemanticClaimCandidate(subject, normalizedPredicate, value, .8m, pair.Value.Sequence, subjectType, financial.ObjectEntityType, AsOfUtc: financial.AsOfUtc, ValueKind: financial.ValueKind, NumericValue: financial.NumericValue, CurrencyCode: financial.CurrencyCode, Unit: financial.Unit));
                    if (normalizedPredicate is "OWNS" or "LENDS_TO" or "BORROWS_FROM" or "HAS_RELATIONSHIP")
                    {
                        var objectType = InferEntityType(predicate);
                        entities.Add(new SemanticEntityCandidate(objectType, value, Array.Empty<string>(), .7m, pair.Value.Sequence));
                        relationships.Add(new SemanticRelationshipCandidate(subject, normalizedPredicate, value, .7m, pair.Value.Sequence, subjectType, objectType));
                    }
                }
            }
        }
    }

    private static void ExtractText(
        IEnumerable<NormalizedSection> sections,
        List<SemanticEntityCandidate> entities,
        List<SemanticClaimCandidate> claims,
        List<SemanticRelationshipCandidate> relationships,
        CancellationToken cancellationToken)
    {
        foreach (var section in sections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = (section.Text ?? section.Value ?? section.Title ?? string.Empty).Trim();
            if (text.Length == 0) continue;
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var match = TextRelation.Match(line.Trim());
                if (!match.Success) continue;
                var subject = match.Groups["subject"].Value.Trim();
                var predicate = NormalizePredicate(match.Groups["predicate"].Value);
                var objectValue = match.Groups["object"].Value.Trim();
                var financial = ParseFinancialValue(objectValue, predicate);
                entities.Add(new SemanticEntityCandidate("ORGANIZATION", subject, Array.Empty<string>(), .7m, section.Sequence));
                if (financial.ObjectEntityType is not null) entities.Add(new SemanticEntityCandidate(financial.ObjectEntityType, objectValue, Array.Empty<string>(), .65m, section.Sequence));
                claims.Add(new SemanticClaimCandidate(subject, predicate, objectValue, .65m, section.Sequence, "ORGANIZATION", financial.ObjectEntityType, AsOfUtc: financial.AsOfUtc, ValueKind: financial.ValueKind, NumericValue: financial.NumericValue, CurrencyCode: financial.CurrencyCode, Unit: financial.Unit));
                if (predicate is "OWNS" or "LENDS_TO" or "BORROWS_FROM" or "HAS_RELATIONSHIP")
                    relationships.Add(new SemanticRelationshipCandidate(subject, predicate, objectValue, .65m, section.Sequence, "ORGANIZATION", financial.ObjectEntityType ?? "ORGANIZATION"));
            }
        }
    }

    private static string NormalizePredicate(string value) => value.Trim().ToUpperInvariant().Replace(' ', '_') switch
    {
        "RATING" or "RATINGS" => "HAS_RATING",
        "BALANCE" => "HAS_BALANCE",
        "EXPOSURE" => "HAS_EXPOSURE",
        "REVENUE" => "HAS_REVENUE",
        "OWES" => "BORROWS_FROM",
        "REPORTS_TO" => "HAS_RELATIONSHIP",
        "LOCATED_IN" => "HAS_RELATIONSHIP",
        var other => other
    };

    private static string InferEntityType(string header) =>
        header.ToLowerInvariant() switch
        {
            var value when value.Contains("account") => "ACCOUNT",
            var value when value.Contains("customer") => "PERSON",
            var value when value.Contains("company") => "ORGANIZATION",
            var value when value.Contains("institution") || value.Contains("bank") => "INSTITUTION",
            var value when value.Contains("loan") || value.Contains("facility") => "LOAN_FACILITY",
            var value when value.Contains("name") => "ORGANIZATION",
            _ => "MENTION"
        };

    private static (string ValueKind, decimal? NumericValue, string? CurrencyCode, string? Unit, DateTime? AsOfUtc, string? ObjectEntityType) ParseFinancialValue(string value, string predicate)
    {
        var match = Regex.Match(value, @"(?:(?<currency>USD|EUR|GBP|JPY)\s*)?(?<amount>-?[0-9][0-9,]*(?:\.[0-9]+)?)\s*(?<unit>%|percent|million|billion)?", RegexOptions.IgnoreCase);
        if (!match.Success || !decimal.TryParse(match.Groups["amount"].Value.Replace(",", string.Empty), out var amount)) return ("text", null, null, null, null, predicate is "OWNS" or "LENDS_TO" or "BORROWS_FROM" ? "ORGANIZATION" : null);
        var unit = match.Groups["unit"].Value.Trim().ToLowerInvariant();
        var currency = match.Groups["currency"].Value.ToUpperInvariant();
        return (unit is "%" or "percent" ? "percentage" : "money", amount, string.IsNullOrWhiteSpace(currency) ? null : currency, string.IsNullOrWhiteSpace(unit) ? null : unit, null, null);
    }

    private static IReadOnlyList<SemanticEntityCandidate> DeduplicateEntities(IEnumerable<SemanticEntityCandidate> candidates) =>
        candidates.GroupBy(candidate => $"{candidate.EntityType}|{Normalize(candidate.CanonicalName)}", StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(candidate => candidate.Confidence).First())
            .ToList();

    private static IReadOnlyList<SemanticClaimCandidate> DeduplicateClaims(IEnumerable<SemanticClaimCandidate> candidates) =>
        candidates.GroupBy(candidate => $"{Normalize(candidate.Subject)}|{Normalize(candidate.Predicate)}|{Normalize(candidate.ObjectValue)}|{candidate.EvidenceSequence}", StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(candidate => candidate.Confidence).First())
            .ToList();

    private static IReadOnlyList<SemanticRelationshipCandidate> DeduplicateRelationships(IEnumerable<SemanticRelationshipCandidate> candidates) =>
        candidates.GroupBy(candidate => $"{Normalize(candidate.Subject)}|{Normalize(candidate.Predicate)}|{Normalize(candidate.Object)}|{candidate.EvidenceSequence}", StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(candidate => candidate.Confidence).First())
            .ToList();

    private static string Normalize(string value) =>
        string.Join(' ', value.Trim().ToUpperInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
