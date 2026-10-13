namespace BankingBrain.Infrastructure.SqlLineage;

public enum SqlTokenType
{
    Identifier,
    String,
    Number,
    Punctuation,
    Eof
}

/// <summary>
/// One lexical unit from a SQL Server script. <see cref="Text"/> is already unwrapped of
/// bracket/quote delimiters for Identifier/String tokens (case preserved as written -
/// SQL Server identifiers are compared case-insensitively almost everywhere in this
/// parser via <see cref="StringComparer.OrdinalIgnoreCase"/>, never re-cased here).
/// </summary>
public sealed record SqlToken(SqlTokenType Type, string Text, int Line)
{
    public bool IsPunctuation(string text) => Type == SqlTokenType.Punctuation && Text == text;

    public bool IsKeyword(string keyword) =>
        Type == SqlTokenType.Identifier && string.Equals(Text, keyword, StringComparison.OrdinalIgnoreCase);
}
