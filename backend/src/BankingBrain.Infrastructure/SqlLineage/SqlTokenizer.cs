using System.Text;

namespace BankingBrain.Infrastructure.SqlLineage;

/// <summary>
/// A deterministic, dependency-free lexer for the subset of T-SQL (SQL Server dialect)
/// that <see cref="SqlLineageScriptAnalyzer"/> understands. It never executes or evaluates
/// SQL - it only recognizes identifiers (plain, bracketed "[x y]", quoted "x"), string
/// literals ('...', with '' escaping), numbers, line comments (--), block comments
/// (/* */, non-nested), and punctuation, while tracking 1-based line numbers for every
/// token so downstream evidence can cite an exact source location.
/// </summary>
public static class SqlTokenizer
{
    public static IReadOnlyList<SqlToken> Tokenize(string sql)
    {
        var tokens = new List<SqlToken>();
        var line = 1;
        var i = 0;
        var length = sql.Length;

        while (i < length)
        {
            var c = sql[i];

            if (c == '\n')
            {
                line++;
                i++;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '-' && i + 1 < length && sql[i + 1] == '-')
            {
                while (i < length && sql[i] != '\n') i++;
                continue;
            }

            if (c == '/' && i + 1 < length && sql[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < length && !(sql[i] == '*' && sql[i + 1] == '/'))
                {
                    if (sql[i] == '\n') line++;
                    i++;
                }
                i = Math.Min(i + 2, length);
                continue;
            }

            if (c == '[')
            {
                var startLine = line;
                var builder = new StringBuilder();
                i++;
                while (i < length && sql[i] != ']')
                {
                    if (sql[i] == '\n') line++;
                    builder.Append(sql[i]);
                    i++;
                }
                i = Math.Min(i + 1, length);
                tokens.Add(new SqlToken(SqlTokenType.Identifier, builder.ToString(), startLine));
                continue;
            }

            if (c == '"')
            {
                var startLine = line;
                var builder = new StringBuilder();
                i++;
                while (i < length)
                {
                    if (sql[i] == '"' && i + 1 < length && sql[i + 1] == '"')
                    {
                        builder.Append('"');
                        i += 2;
                        continue;
                    }
                    if (sql[i] == '"') break;
                    if (sql[i] == '\n') line++;
                    builder.Append(sql[i]);
                    i++;
                }
                i = Math.Min(i + 1, length);
                tokens.Add(new SqlToken(SqlTokenType.Identifier, builder.ToString(), startLine));
                continue;
            }

            if (c == '\'')
            {
                var startLine = line;
                var builder = new StringBuilder();
                i++;
                while (i < length)
                {
                    if (sql[i] == '\'' && i + 1 < length && sql[i + 1] == '\'')
                    {
                        builder.Append('\'');
                        i += 2;
                        continue;
                    }
                    if (sql[i] == '\'') break;
                    if (sql[i] == '\n') line++;
                    builder.Append(sql[i]);
                    i++;
                }
                i = Math.Min(i + 1, length);
                tokens.Add(new SqlToken(SqlTokenType.String, builder.ToString(), startLine));
                continue;
            }

            if (char.IsLetter(c) || c == '_' || c == '@' || c == '#')
            {
                var startLine = line;
                var start = i;
                i++;
                while (i < length && (char.IsLetterOrDigit(sql[i]) || sql[i] is '_' or '@' or '#' or '$'))
                    i++;
                tokens.Add(new SqlToken(SqlTokenType.Identifier, sql[start..i], startLine));
                continue;
            }

            if (char.IsDigit(c))
            {
                var startLine = line;
                var start = i;
                i++;
                while (i < length && (char.IsDigit(sql[i]) || sql[i] == '.'))
                    i++;
                tokens.Add(new SqlToken(SqlTokenType.Number, sql[start..i], startLine));
                continue;
            }

            tokens.Add(new SqlToken(SqlTokenType.Punctuation, c.ToString(), line));
            i++;
        }

        tokens.Add(new SqlToken(SqlTokenType.Eof, string.Empty, line));
        return tokens;
    }
}
