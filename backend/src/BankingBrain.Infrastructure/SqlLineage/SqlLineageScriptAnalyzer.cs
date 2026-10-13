using System.Security.Cryptography;
using System.Text;
using BankingBrain.Application.Common.CodeIntelligence.Semantics;

namespace BankingBrain.Infrastructure.SqlLineage;

/// <summary>
/// Result of analyzing a single SQL script file. Entities/Relationships reuse the exact
/// same shapes Code Intelligence persists through (<see cref="CodeEntityObservation"/>/
/// <see cref="CodeRelationshipObservation"/>) so SQL Lineage can go through the same
/// <c>ISemanticExtractionRepository.PersistCodeObservationsAsync</c> path without any new
/// persistence code. <see cref="UnresolvedCount"/> is a diagnostic-only count of statements
/// this analyzer deliberately declined to turn into a dependency claim (dynamic SQL,
/// ambiguous column references, or syntax outside the supported subset) - every one of
/// those is still recorded as a real, evidence-backed UNRESOLVED_REFERENCE relationship
/// (confidence 0) rather than silently dropped, so it stays visible to callers instead of
/// being indistinguishable from "fully resolved, no dependency".
/// </summary>
public sealed record SqlFileAnalysisResult(
    IReadOnlyList<CodeEntityObservation> Entities,
    IReadOnlyList<CodeRelationshipObservation> Relationships,
    int UnresolvedCount);

/// <summary>
/// A deterministic, statically-resolvable-only analyzer for a useful subset of T-SQL
/// (SQL Server dialect): CREATE/ALTER TABLE (columns, inline and table-level PRIMARY
/// KEY/FOREIGN KEY), CREATE/ALTER [OR ALTER] VIEW (source tables and best-effort
/// column-level lineage for simple SELECT lists), CREATE/ALTER [OR ALTER]
/// PROCEDURE/FUNCTION bodies (SELECT/INSERT/UPDATE/DELETE/EXEC statements), and
/// freestanding INSERT/UPDATE/DELETE/EXEC batches.
///
/// It never executes SQL and never guesses: anything it cannot statically resolve to a
/// concrete schema-qualified table/column/procedure (dynamic SQL via EXEC(string)/
/// sp_executesql, ambiguous unqualified columns with more than one candidate source
/// table, and syntax outside the subset above - MERGE, PIVOT/UNPIVOT, CTEs, cursors) is
/// recorded as an UNRESOLVED_REFERENCE relationship rather than fabricated as a real
/// dependency. Routine control-flow statements with no lineage content of their own
/// (DECLARE, variable SET, IF/WHILE/BEGIN/END, RETURN/PRINT/THROW/RAISERROR/BREAK/
/// CONTINUE) are recognized and skipped without being flagged unresolved, since flagging
/// ordinary control flow as "unresolved" would bury the genuinely uncertain cases in noise.
/// </summary>
public sealed class SqlLineageScriptAnalyzer
{
    private static readonly HashSet<string> ControlFlowKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "DECLARE", "IF", "ELSE", "WHILE", "RETURN", "PRINT", "BREAK", "CONTINUE",
        "THROW", "RAISERROR", "COMMIT", "ROLLBACK", "BEGIN", "END", "TRY", "CATCH",
        "GRANT", "DENY", "REVOKE", "SET"
    };

    private readonly List<CodeEntityObservation> _entities = new();
    private readonly List<CodeRelationshipObservation> _relationships = new();
    private readonly HashSet<string> _seenRelationshipKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _seenEntityIds = new(StringComparer.Ordinal);
    private int _unresolvedCount;

    private string _relativePath = string.Empty;
    private string _currentDatabase = "default";

    public SqlFileAnalysisResult Analyze(string sql, string relativePath, string defaultDatabase)
    {
        _entities.Clear();
        _relationships.Clear();
        _seenRelationshipKeys.Clear();
        _seenEntityIds.Clear();
        _unresolvedCount = 0;
        _relativePath = relativePath;
        _currentDatabase = string.IsNullOrWhiteSpace(defaultDatabase) ? "default" : defaultDatabase;

        var cursor = new TokenCursor(SqlTokenizer.Tokenize(sql));

        while (!cursor.AtEnd)
        {
            if (cursor.Current.IsPunctuation(";")) { cursor.Advance(); continue; }
            if (cursor.Current.IsKeyword("GO")) { cursor.Advance(); continue; }
            DispatchTopLevelStatement(cursor, anchorExternalId: null);
        }

        return new SqlFileAnalysisResult(_entities, _relationships, _unresolvedCount);
    }

    // ---------------------------------------------------------------- top-level dispatch

    private void DispatchTopLevelStatement(TokenCursor cursor, string? anchorExternalId)
    {
        var line = cursor.Current.Line;

        if (cursor.Current.IsKeyword("USE"))
        {
            cursor.Advance();
            var db = ReadSingleIdentifier(cursor);
            if (db is not null) _currentDatabase = db;
            SkipToStatementBoundary(cursor);
            return;
        }

        if (cursor.Current.IsKeyword("CREATE") || cursor.Current.IsKeyword("ALTER"))
        {
            cursor.Advance();
            if (cursor.Current.IsKeyword("OR")) { cursor.Advance(); cursor.MatchKeyword("ALTER"); }

            if (cursor.Current.IsKeyword("TABLE")) { cursor.Advance(); ParseCreateOrAlterTable(cursor, line); return; }
            if (cursor.Current.IsKeyword("VIEW")) { cursor.Advance(); ParseCreateOrAlterView(cursor, line); return; }
            if (cursor.Current.IsKeyword("PROCEDURE") || cursor.Current.IsKeyword("PROC"))
            { cursor.Advance(); ParseCreateOrAlterRoutine(cursor, line, isFunction: false); return; }
            if (cursor.Current.IsKeyword("FUNCTION")) { cursor.Advance(); ParseCreateOrAlterRoutine(cursor, line, isFunction: true); return; }
            if (cursor.Current.IsKeyword("SCHEMA"))
            {
                cursor.Advance();
                var name = ReadSingleIdentifier(cursor);
                if (name is not null) EnsureSchema(_currentDatabase, name, line);
                SkipToStatementBoundary(cursor);
                return;
            }

            EmitUnresolved("unsupported-syntax", DescribeUpcoming(cursor), anchorExternalId ?? DatabaseId(_currentDatabase), line);
            SkipToStatementBoundary(cursor);
            return;
        }

        if (cursor.Current.IsKeyword("INSERT")) { ParseInsert(cursor, line, enclosingRoutineId: anchorExternalId); return; }
        if (cursor.Current.IsKeyword("UPDATE")) { ParseUpdate(cursor, line, enclosingRoutineId: anchorExternalId); return; }
        if (cursor.Current.IsKeyword("DELETE")) { ParseDelete(cursor, line, enclosingRoutineId: anchorExternalId); return; }
        if (cursor.Current.IsKeyword("SELECT")) { ParseSelect(cursor, line, enclosingRoutineId: anchorExternalId, viewTargetId: null, insertTarget: null); return; }
        if (cursor.Current.IsKeyword("EXEC") || cursor.Current.IsKeyword("EXECUTE"))
        { ParseExec(cursor, line, enclosingRoutineId: anchorExternalId); return; }

        if (ControlFlowKeywords.Contains(cursor.Current.Type == SqlTokenType.Identifier ? cursor.Current.Text : string.Empty))
        {
            cursor.Advance();
            return;
        }

        if (cursor.Current.Type == SqlTokenType.Eof)
            return;

        // Unknown/unsupported statement shape (MERGE, PIVOT/UNPIVOT, CTE "WITH", cursors, ...).
        EmitUnresolved("unsupported-syntax", DescribeUpcoming(cursor), anchorExternalId ?? DatabaseId(_currentDatabase), line);
        SkipToStatementBoundary(cursor);
    }

    // ---------------------------------------------------------------- CREATE/ALTER TABLE

    private void ParseCreateOrAlterTable(TokenCursor cursor, int statementLine)
    {
        var (schema, name) = ReadSchemaQualifiedName(cursor);
        if (name is null) { SkipToStatementBoundary(cursor); return; }

        var tableId = TableId(_currentDatabase, schema, name);
        EnsureSchema(_currentDatabase, schema, statementLine);
        EmitEntity("SqlTable", $"{_currentDatabase}.{schema}.{name}", tableId, Location(statementLine));
        EmitRelationship(SchemaId(_currentDatabase, schema), tableId, "SCHEMA_CONTAINS_TABLE", statementLine);

        if (cursor.Current.IsKeyword("ADD"))
        {
            cursor.Advance();
            ParseColumnAndConstraintItems(cursor, tableId, statementLine, stopAtTopLevelSemicolon: true);
            SkipToStatementBoundary(cursor);
            return;
        }

        if (!cursor.Current.IsPunctuation("(")) { SkipToStatementBoundary(cursor); return; }
        cursor.Advance();
        ParseColumnAndConstraintItems(cursor, tableId, statementLine, stopAtTopLevelSemicolon: false);
        cursor.MatchPunctuation(")");
        SkipToStatementBoundary(cursor);
    }

    /// <summary>
    /// Parses a comma-separated list of column definitions and table-level PRIMARY
    /// KEY/FOREIGN KEY/UNIQUE constraints, up to the closing ')' (or, for an ALTER TABLE
    /// ADD without parentheses, up to the statement-ending ';'/EOF/GO).
    /// </summary>
    private void ParseColumnAndConstraintItems(TokenCursor cursor, string tableId, int statementLine, bool stopAtTopLevelSemicolon)
    {
        while (!cursor.AtEnd)
        {
            if (!stopAtTopLevelSemicolon && cursor.Current.IsPunctuation(")")) break;
            if (stopAtTopLevelSemicolon && (cursor.Current.IsPunctuation(";") || cursor.Current.IsKeyword("GO") || cursor.Current.Type == SqlTokenType.Eof)) break;

            var items = ReadTopLevelCommaItem(cursor);
            if (items.Count == 0) break;
            ParseSingleColumnOrConstraintItem(items, tableId, statementLine);

            if (cursor.Current.IsPunctuation(",")) { cursor.Advance(); continue; }
            break;
        }
    }

    private void ParseSingleColumnOrConstraintItem(List<SqlToken> item, string tableId, int statementLine)
    {
        var cursor = new TokenCursor(item.Append(new SqlToken(SqlTokenType.Eof, string.Empty, statementLine)).ToList());

        if (cursor.Current.IsKeyword("CONSTRAINT"))
        {
            cursor.Advance();
            ReadSingleIdentifier(cursor); // constraint name, not modeled
        }

        if (cursor.Current.IsKeyword("PRIMARY") || cursor.Current.IsKeyword("UNIQUE"))
        {
            // Table-level PK/UNIQUE: no new dependency to record (columns are already
            // declared via their own column items).
            return;
        }

        if (cursor.Current.IsKeyword("FOREIGN"))
        {
            cursor.Advance();
            cursor.MatchKeyword("KEY");
            var localColumns = ReadParenColumnList(cursor);
            if (!cursor.Current.IsKeyword("REFERENCES")) return;
            cursor.Advance();
            var (refSchema, refTable) = ReadSchemaQualifiedName(cursor);
            if (refTable is null) return;
            var refTableId = TableId(_currentDatabase, refSchema, refTable);
            var refColumns = cursor.Current.IsPunctuation("(") ? ReadParenColumnList(cursor) : new List<string>();

            for (var i = 0; i < localColumns.Count; i++)
            {
                var localColumnId = ColumnId(tableId, localColumns[i]);
                EmitEntity("SqlColumn", $"{tableId}.{localColumns[i]}", localColumnId, Location(statementLine));
                var refColumnName = i < refColumns.Count ? refColumns[i] : (refColumns.Count == 1 ? refColumns[0] : null);
                if (refColumnName is null)
                {
                    EmitUnresolved("ambiguous-foreign-key", $"FOREIGN KEY on {localColumns[i]} references {refTable} without a matching column list", tableId, statementLine);
                    continue;
                }
                var refColumnId = ColumnId(refTableId, refColumnName);
                EmitEntity("SqlColumn", $"{refTableId}.{refColumnName}", refColumnId, null);
                EmitRelationship(localColumnId, refColumnId, "COLUMN_DEPENDS_ON_COLUMN", statementLine);
            }
            return;
        }

        // Ordinary column definition: NAME <data type tokens...> [inline REFERENCES ...]
        if (cursor.Current.Type != SqlTokenType.Identifier) return;
        var columnName = cursor.Current.Text;
        cursor.Advance();

        var columnId = ColumnId(tableId, columnName);
        EmitEntity("SqlColumn", $"{tableId}.{columnName}", columnId, Location(statementLine));
        EmitRelationship(tableId, columnId, "TABLE_CONTAINS_COLUMN", statementLine);

        while (!cursor.AtEnd)
        {
            if (cursor.Current.IsKeyword("REFERENCES"))
            {
                cursor.Advance();
                var (refSchema, refTable) = ReadSchemaQualifiedName(cursor);
                if (refTable is not null)
                {
                    var refTableId = TableId(_currentDatabase, refSchema, refTable);
                    var refColumns = cursor.Current.IsPunctuation("(") ? ReadParenColumnList(cursor) : new List<string>();
                    var refColumnName = refColumns.Count == 1 ? refColumns[0] : null;
                    if (refColumnName is not null)
                    {
                        var refColumnId = ColumnId(refTableId, refColumnName);
                        EmitEntity("SqlColumn", $"{refTableId}.{refColumnName}", refColumnId, null);
                        EmitRelationship(columnId, refColumnId, "COLUMN_DEPENDS_ON_COLUMN", statementLine);
                    }
                    else
                    {
                        EmitUnresolved("ambiguous-foreign-key", $"inline REFERENCES on {columnName} to {refTable} without exactly one referenced column", tableId, statementLine);
                    }
                }
                continue;
            }
            cursor.Advance();
        }
    }

    // ---------------------------------------------------------------- CREATE/ALTER VIEW

    private void ParseCreateOrAlterView(TokenCursor cursor, int statementLine)
    {
        var (schema, name) = ReadSchemaQualifiedName(cursor);
        if (name is null) { SkipToStatementBoundary(cursor); return; }

        var viewId = ViewId(_currentDatabase, schema, name);
        EnsureSchema(_currentDatabase, schema, statementLine);
        EmitEntity("SqlView", $"{_currentDatabase}.{schema}.{name}", viewId, Location(statementLine));
        EmitRelationship(SchemaId(_currentDatabase, schema), viewId, "SCHEMA_CONTAINS_VIEW", statementLine);

        // Skip any column-name list and options up to AS.
        while (!cursor.AtEnd && !cursor.Current.IsKeyword("AS")) cursor.Advance();
        if (cursor.Current.IsKeyword("AS")) cursor.Advance();

        if (!cursor.Current.IsKeyword("SELECT"))
        {
            EmitUnresolved("unsupported-syntax", "view body is not a plain SELECT", viewId, statementLine);
            SkipToStatementBoundary(cursor);
            return;
        }

        ParseSelect(cursor, statementLine, enclosingRoutineId: null, viewTargetId: viewId, insertTarget: null);
        SkipToStatementBoundary(cursor);
    }

    // ---------------------------------------------------------------- CREATE/ALTER PROCEDURE/FUNCTION

    private void ParseCreateOrAlterRoutine(TokenCursor cursor, int statementLine, bool isFunction)
    {
        var (schema, name) = ReadSchemaQualifiedName(cursor);
        if (name is null) { SkipToStatementBoundary(cursor); return; }

        var entityType = isFunction ? "SqlFunction" : "SqlProcedure";
        var predicate = isFunction ? "SCHEMA_CONTAINS_FUNCTION" : "SCHEMA_CONTAINS_PROCEDURE";
        var routineId = isFunction ? FunctionId(_currentDatabase, schema, name) : ProcedureId(_currentDatabase, schema, name);

        EnsureSchema(_currentDatabase, schema, statementLine);
        EmitEntity(entityType, $"{_currentDatabase}.{schema}.{name}", routineId, Location(statementLine));
        EmitRelationship(SchemaId(_currentDatabase, schema), routineId, predicate, statementLine);

        // Skip parameter list (balanced parens), then (for functions) a RETURNS clause,
        // up to the routine's AS keyword.
        if (cursor.Current.IsPunctuation("(")) SkipBalancedParens(cursor);
        while (!cursor.AtEnd && !cursor.Current.IsKeyword("AS")) cursor.Advance();
        if (cursor.Current.IsKeyword("AS")) cursor.Advance();

        var hasBegin = cursor.Current.IsKeyword("BEGIN");
        if (hasBegin) cursor.Advance();

        var depth = hasBegin ? 1 : 0;

        while (!cursor.AtEnd)
        {
            if (cursor.Current.IsPunctuation(";")) { cursor.Advance(); continue; }
            if (cursor.Current.IsKeyword("GO")) break;

            if (cursor.Current.IsKeyword("BEGIN")) { depth++; cursor.Advance(); continue; }
            if (cursor.Current.IsKeyword("END"))
            {
                depth--;
                cursor.Advance();
                if (depth <= 0) break;
                continue;
            }

            if (cursor.Current.Type == SqlTokenType.Eof) break;

            DispatchTopLevelStatement(cursor, anchorExternalId: routineId);
        }
    }

    // ---------------------------------------------------------------- INSERT / UPDATE / DELETE / EXEC

    private void ParseInsert(TokenCursor cursor, int statementLine, string? enclosingRoutineId)
    {
        cursor.Advance(); // INSERT
        cursor.MatchKeyword("INTO");
        var (schema, name) = ReadSchemaQualifiedName(cursor);
        if (name is null) { SkipToStatementBoundary(cursor); return; }

        var targetId = TableId(_currentDatabase, schema, name);
        var targetColumns = cursor.Current.IsPunctuation("(") ? ReadParenColumnList(cursor) : new List<string>();

        if (enclosingRoutineId is not null)
        {
            EmitRelationship(enclosingRoutineId, targetId, "PROCEDURE_WRITES_TABLE", statementLine);
            foreach (var column in targetColumns)
            {
                var columnId = ColumnId(targetId, column);
                EmitEntity("SqlColumn", $"{targetId}.{column}", columnId, null);
                EmitRelationship(enclosingRoutineId, columnId, "PROCEDURE_WRITES_COLUMN", statementLine);
            }
        }

        if (cursor.Current.IsKeyword("SELECT"))
        {
            ParseSelect(cursor, statementLine, enclosingRoutineId, viewTargetId: null,
                insertTarget: targetColumns.Count > 0 ? (targetId, targetColumns) : null);
        }
        else
        {
            SkipToStatementBoundary(cursor);
        }
    }

    private void ParseUpdate(TokenCursor cursor, int statementLine, string? enclosingRoutineId)
    {
        cursor.Advance(); // UPDATE
        var (schema, name) = ReadSchemaQualifiedName(cursor);
        if (name is null) { SkipToStatementBoundary(cursor); return; }

        var targetId = TableId(_currentDatabase, schema, name);
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [name] = targetId };

        if (cursor.Current.Type == SqlTokenType.Identifier && !cursor.Current.IsKeyword("SET"))
        {
            aliases[cursor.Current.Text] = targetId;
            cursor.Advance();
        }

        if (enclosingRoutineId is not null)
            EmitRelationship(enclosingRoutineId, targetId, "PROCEDURE_WRITES_TABLE", statementLine);

        cursor.MatchKeyword("SET");

        while (!cursor.AtEnd)
        {
            if (cursor.Current.Type != SqlTokenType.Identifier) break;
            var setColumn = cursor.Current.Text;
            cursor.Advance();
            if (!cursor.Current.IsPunctuation("=")) break;
            cursor.Advance();

            var targetColumnId = ColumnId(targetId, setColumn);
            EmitEntity("SqlColumn", $"{targetId}.{setColumn}", targetColumnId, null);
            if (enclosingRoutineId is not null)
                EmitRelationship(enclosingRoutineId, targetColumnId, "PROCEDURE_WRITES_COLUMN", statementLine);

            // Expression up to the next top-level comma/FROM/WHERE: best-effort qualified
            // column references inside it feed COLUMN_DEPENDS_ON_COLUMN for this target.
            while (!cursor.AtEnd && !cursor.Current.IsPunctuation(",") &&
                   !cursor.Current.IsKeyword("FROM") && !cursor.Current.IsKeyword("WHERE") &&
                   !cursor.Current.IsPunctuation(";") && !cursor.Current.IsKeyword("GO"))
            {
                if (cursor.Current.Type == SqlTokenType.Identifier && cursor.Peek(1).IsPunctuation(".") &&
                    cursor.Peek(2).Type == SqlTokenType.Identifier)
                {
                    var qualifier = cursor.Current.Text;
                    var column = cursor.Peek(2).Text;
                    cursor.Advance(); cursor.Advance(); cursor.Advance();
                    if (aliases.TryGetValue(qualifier, out var sourceTableId))
                    {
                        var sourceColumnId = ColumnId(sourceTableId, column);
                        EmitEntity("SqlColumn", $"{sourceTableId}.{column}", sourceColumnId, null);
                        EmitRelationship(targetColumnId, sourceColumnId, "COLUMN_DEPENDS_ON_COLUMN", statementLine);
                    }
                    continue;
                }
                cursor.Advance();
            }

            if (cursor.Current.IsPunctuation(",")) { cursor.Advance(); continue; }
            break;
        }

        if (cursor.Current.IsKeyword("FROM"))
        {
            cursor.Advance();
            var sources = ReadFromAndJoins(cursor, aliases);
            foreach (var source in sources)
            {
                if (enclosingRoutineId is not null)
                    EmitRelationship(enclosingRoutineId, source, "PROCEDURE_READS_TABLE", statementLine);
            }
        }

        SkipToStatementBoundary(cursor);
    }

    private void ParseDelete(TokenCursor cursor, int statementLine, string? enclosingRoutineId)
    {
        cursor.Advance(); // DELETE
        cursor.MatchKeyword("FROM");
        var (schema, name) = ReadSchemaQualifiedName(cursor);
        if (name is null) { SkipToStatementBoundary(cursor); return; }

        var targetId = TableId(_currentDatabase, schema, name);
        if (enclosingRoutineId is not null)
            EmitRelationship(enclosingRoutineId, targetId, "PROCEDURE_WRITES_TABLE", statementLine);

        SkipToStatementBoundary(cursor);
    }

    private void ParseExec(TokenCursor cursor, int statementLine, string? enclosingRoutineId)
    {
        cursor.Advance(); // EXEC/EXECUTE

        if (cursor.Current.IsPunctuation("(") ||
            cursor.Current.IsKeyword("sp_executesql") ||
            (cursor.Current.Type == SqlTokenType.Identifier && cursor.Current.Text.StartsWith('@') && cursor.Peek(1).IsPunctuation("(")))
        {
            EmitUnresolved("dynamic-sql", "EXEC of a dynamically constructed statement", enclosingRoutineId ?? DatabaseId(_currentDatabase), statementLine);
            SkipToStatementBoundary(cursor);
            return;
        }

        var (schema, name) = ReadSchemaQualifiedName(cursor);
        if (name is null)
        {
            EmitUnresolved("dynamic-sql", "EXEC target could not be statically resolved", enclosingRoutineId ?? DatabaseId(_currentDatabase), statementLine);
            SkipToStatementBoundary(cursor);
            return;
        }

        var calledId = ProcedureId(_currentDatabase, schema, name);
        EmitEntity("SqlProcedure", $"{_currentDatabase}.{schema}.{name}", calledId, null);

        if (enclosingRoutineId is not null)
            EmitRelationship(enclosingRoutineId, calledId, "PROCEDURE_CALLS_PROCEDURE", statementLine);
        else
            EmitUnresolved("top-level-call", $"top-level EXEC {name} has no enclosing procedure/function to attribute the call to", calledId, statementLine);

        SkipToStatementBoundary(cursor);
    }

    /// <summary>
    /// Parses a SELECT statement's column list and FROM/JOIN table sources. When
    /// <paramref name="viewTargetId"/> is set (view body), emits VIEW_CONTAINS_COLUMN/
    /// VIEW_DERIVES_FROM_TABLE/VIEW_DERIVES_FROM_COLUMN. When <paramref name="enclosingRoutineId"/>
    /// is set (procedure/function body), emits PROCEDURE_READS_TABLE/PROCEDURE_READS_COLUMN.
    /// When <paramref name="insertTarget"/> is set (INSERT ... SELECT), emits
    /// COLUMN_DEPENDS_ON_COLUMN from each named target column to its positional source column(s).
    /// </summary>
    private void ParseSelect(
        TokenCursor cursor,
        int statementLine,
        string? enclosingRoutineId,
        string? viewTargetId,
        (string TableId, List<string> Columns)? insertTarget)
    {
        cursor.Advance(); // SELECT
        if (cursor.Current.IsKeyword("DISTINCT") || cursor.Current.IsKeyword("ALL")) cursor.Advance();
        if (cursor.Current.IsKeyword("TOP"))
        {
            cursor.Advance();
            if (cursor.Current.IsPunctuation("(")) SkipBalancedParens(cursor); else cursor.Advance();
        }

        var selectList = ReadUntilTopLevelKeyword(cursor, "FROM");
        if (!cursor.Current.IsKeyword("FROM"))
        {
            // SELECT with no FROM (e.g. a scalar/computed result) carries no table lineage.
            SkipToStatementBoundary(cursor);
            return;
        }
        cursor.Advance();

        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sourceTableIds = ReadFromAndJoins(cursor, aliases);

        if (cursor.Current.IsKeyword("WHERE") || cursor.Current.IsKeyword("GROUP") ||
            cursor.Current.IsKeyword("ORDER") || cursor.Current.IsKeyword("HAVING"))
        {
            SkipToStatementBoundary(cursor);
        }

        foreach (var sourceTableId in sourceTableIds)
        {
            if (viewTargetId is not null)
                EmitRelationship(viewTargetId, sourceTableId, "VIEW_DERIVES_FROM_TABLE", statementLine);
            if (enclosingRoutineId is not null)
                EmitRelationship(enclosingRoutineId, sourceTableId, "PROCEDURE_READS_TABLE", statementLine);
        }

        var unresolvedAnchor = viewTargetId ?? insertTarget?.TableId ?? enclosingRoutineId ?? DatabaseId(_currentDatabase);
        var outputs = ParseSelectList(selectList, aliases, sourceTableIds, statementLine, unresolvedAnchor);

        for (var i = 0; i < outputs.Count; i++)
        {
            var (outputName, sourceColumnIds) = outputs[i];

            if (viewTargetId is not null)
            {
                var viewColumnId = ColumnId(viewTargetId, outputName);
                EmitEntity("SqlColumn", $"{viewTargetId}.{outputName}", viewColumnId, null);
                EmitRelationship(viewTargetId, viewColumnId, "VIEW_CONTAINS_COLUMN", statementLine);
                foreach (var sourceColumnId in sourceColumnIds)
                    EmitRelationship(viewColumnId, sourceColumnId, "VIEW_DERIVES_FROM_COLUMN", statementLine);
            }

            if (enclosingRoutineId is not null)
            {
                foreach (var sourceColumnId in sourceColumnIds)
                    EmitRelationship(enclosingRoutineId, sourceColumnId, "PROCEDURE_READS_COLUMN", statementLine);
            }

            if (insertTarget is { } target && i < target.Columns.Count)
            {
                var targetColumnId = ColumnId(target.TableId, target.Columns[i]);
                EmitEntity("SqlColumn", $"{target.TableId}.{target.Columns[i]}", targetColumnId, null);
                foreach (var sourceColumnId in sourceColumnIds)
                    EmitRelationship(targetColumnId, sourceColumnId, "COLUMN_DEPENDS_ON_COLUMN", statementLine);
            }
        }
    }

    /// <summary>
    /// Splits a SELECT list into top-level comma-separated items and, for each, resolves
    /// the output column name (explicit "AS alias", or the bare column name) together with
    /// every "qualifier.column" reference found anywhere in that item's expression
    /// (so e.g. "a.amount + b.fee AS total" yields two source columns for "total"). A bare,
    /// unqualified column is resolved only when exactly one source table is in scope;
    /// "*"/"table.*" and a genuinely ambiguous unqualified column are recorded as
    /// UNRESOLVED_REFERENCE rather than guessed.
    /// </summary>
    private List<(string OutputName, List<string> SourceColumnIds)> ParseSelectList(
        List<SqlToken> selectListTokens,
        Dictionary<string, string> aliases,
        List<string> sourceTableIds,
        int statementLine,
        string unresolvedAnchorId)
    {
        var results = new List<(string, List<string>)>();
        if (selectListTokens.Count == 0) return results;

        var items = SplitTopLevelComma(selectListTokens);
        var autoIndex = 0;

        foreach (var item in items)
        {
            if (item.Count == 1 && item[0].IsPunctuation("*"))
            {
                EmitUnresolved("select-star", "SELECT * does not statically resolve to named output columns", unresolvedAnchorId, statementLine);
                continue;
            }

            string? explicitAlias = null;
            var lastKeywordAsIndex = -1;
            for (var i = 0; i < item.Count; i++)
            {
                if (item[i].IsKeyword("AS")) lastKeywordAsIndex = i;
            }
            if (lastKeywordAsIndex >= 0 && lastKeywordAsIndex + 1 < item.Count)
            {
                explicitAlias = item[lastKeywordAsIndex + 1].Text;
            }
            else if (item.Count >= 2 && item[^1].Type == SqlTokenType.Identifier &&
                     !item[^2].IsPunctuation(".") && !IsOperator(item[^2]))
            {
                // "expr alias" without AS - only when the trailing identifier isn't itself
                // part of a qualifier.column pair and isn't immediately after an operator.
                explicitAlias = item[^1].Text;
            }

            var sourceColumnIds = new List<string>();
            var qualifiedRefs = 0;
            for (var i = 0; i < item.Count; i++)
            {
                if (item[i].Type == SqlTokenType.Identifier && i + 2 < item.Count &&
                    item[i + 1].IsPunctuation(".") && item[i + 2].Type == SqlTokenType.Identifier)
                {
                    qualifiedRefs++;
                    if (item[i + 2].IsPunctuation("*"))
                    {
                        if (aliases.TryGetValue(item[i].Text, out var starTableId))
                            EmitUnresolved("select-star", $"{item[i].Text}.* does not statically resolve to named output columns", starTableId, statementLine);
                        i += 2;
                        continue;
                    }
                    if (aliases.TryGetValue(item[i].Text, out var sourceTableId))
                    {
                        var columnId = ColumnId(sourceTableId, item[i + 2].Text);
                        EmitEntity("SqlColumn", $"{sourceTableId}.{item[i + 2].Text}", columnId, null);
                        sourceColumnIds.Add(columnId);
                    }
                    i += 2;
                }
            }

            string outputName;
            if (explicitAlias is not null)
            {
                outputName = explicitAlias;
            }
            else if (qualifiedRefs == 0 && item.Count >= 1 && item[0].Type == SqlTokenType.Identifier &&
                     (item.Count == 1 || item[1].IsPunctuation(".") == false) && !LooksLikeFunctionCall(item))
            {
                // Bare, unqualified column reference used directly as the output.
                var bareName = item[0].Text;
                if (sourceTableIds.Count == 1)
                {
                    var onlyTableId = sourceTableIds[0];
                    var columnId = ColumnId(onlyTableId, bareName);
                    EmitEntity("SqlColumn", $"{onlyTableId}.{bareName}", columnId, null);
                    sourceColumnIds.Add(columnId);
                }
                else if (sourceTableIds.Count > 1)
                {
                    EmitUnresolved("ambiguous-column", $"unqualified column '{bareName}' with {sourceTableIds.Count} tables in scope", unresolvedAnchorId, statementLine);
                }
                outputName = bareName;
            }
            else
            {
                outputName = explicitAlias ?? $"expr{autoIndex++}";
            }

            results.Add((outputName, sourceColumnIds));
        }

        return results;
    }

    private static bool LooksLikeFunctionCall(List<SqlToken> item) =>
        item.Count >= 2 && item[0].Type == SqlTokenType.Identifier && item[1].IsPunctuation("(");

    private static bool IsOperator(SqlToken token) =>
        token.Type == SqlTokenType.Punctuation && token.Text is "+" or "-" or "*" or "/" or "=" or "(" or ",";

    /// <summary>Reads FROM's first table source and any following JOIN clauses, registering aliases and returning the distinct table external ids referenced.</summary>
    private List<string> ReadFromAndJoins(TokenCursor cursor, Dictionary<string, string> aliases)
    {
        var tableIds = new List<string>();

        var first = ReadTableSourceWithAlias(cursor, aliases);
        if (first is not null) tableIds.Add(first);

        while (!cursor.AtEnd)
        {
            var isJoin = cursor.Current.IsKeyword("JOIN");
            var skippedJoinModifier = false;
            if (!isJoin && (cursor.Current.IsKeyword("INNER") || cursor.Current.IsKeyword("LEFT") ||
                             cursor.Current.IsKeyword("RIGHT") || cursor.Current.IsKeyword("FULL") ||
                             cursor.Current.IsKeyword("OUTER") || cursor.Current.IsKeyword("CROSS")))
            {
                var save = cursor.Position;
                while (cursor.Current.IsKeyword("INNER") || cursor.Current.IsKeyword("LEFT") ||
                       cursor.Current.IsKeyword("RIGHT") || cursor.Current.IsKeyword("FULL") ||
                       cursor.Current.IsKeyword("OUTER") || cursor.Current.IsKeyword("CROSS"))
                    cursor.Advance();
                if (cursor.Current.IsKeyword("JOIN")) { isJoin = true; skippedJoinModifier = true; }
                else cursor.Position = save;
            }

            if (!isJoin) break;
            cursor.Advance(); // JOIN

            var joined = ReadTableSourceWithAlias(cursor, aliases);
            if (joined is not null) tableIds.Add(joined);

            if (cursor.Current.IsKeyword("ON"))
            {
                cursor.Advance();
                SkipJoinCondition(cursor);
            }

            _ = skippedJoinModifier;
        }

        return tableIds.Distinct(StringComparer.Ordinal).ToList();
    }

    private string? ReadTableSourceWithAlias(TokenCursor cursor, Dictionary<string, string> aliases)
    {
        if (cursor.Current.IsPunctuation("("))
        {
            // Derived table / subquery source: not statically attributable to one object.
            SkipBalancedParens(cursor);
            if (cursor.Current.Type == SqlTokenType.Identifier && !IsClauseKeyword(cursor.Current.Text))
                cursor.Advance();
            return null;
        }

        var (schema, name) = ReadSchemaQualifiedName(cursor);
        if (name is null) return null;

        var tableId = TableId(_currentDatabase, schema, name);
        aliases[name] = tableId;

        if (cursor.Current.IsKeyword("AS"))
        {
            cursor.Advance();
            if (cursor.Current.Type == SqlTokenType.Identifier) { aliases[cursor.Current.Text] = tableId; cursor.Advance(); }
        }
        else if (cursor.Current.Type == SqlTokenType.Identifier && !IsClauseKeyword(cursor.Current.Text))
        {
            aliases[cursor.Current.Text] = tableId;
            cursor.Advance();
        }

        return tableId;
    }

    private static bool IsClauseKeyword(string text) =>
        string.Equals(text, "WHERE", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(text, "JOIN", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(text, "INNER", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(text, "LEFT", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(text, "RIGHT", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(text, "FULL", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(text, "OUTER", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(text, "CROSS", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(text, "ON", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(text, "GROUP", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(text, "ORDER", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(text, "HAVING", StringComparison.OrdinalIgnoreCase);

    private static void SkipJoinCondition(TokenCursor cursor)
    {
        while (!cursor.AtEnd)
        {
            if (cursor.Current.IsKeyword("JOIN") || cursor.Current.IsKeyword("WHERE") ||
                cursor.Current.IsKeyword("GROUP") || cursor.Current.IsKeyword("ORDER") ||
                cursor.Current.IsKeyword("HAVING") || cursor.Current.IsPunctuation(";") ||
                cursor.Current.IsKeyword("GO") || cursor.Current.Type == SqlTokenType.Eof)
                return;
            if (cursor.Current.IsKeyword("INNER") || cursor.Current.IsKeyword("LEFT") ||
                cursor.Current.IsKeyword("RIGHT") || cursor.Current.IsKeyword("FULL") ||
                cursor.Current.IsKeyword("OUTER") || cursor.Current.IsKeyword("CROSS"))
                return;
            cursor.Advance();
        }
    }

    // ---------------------------------------------------------------- shared token helpers

    private sealed class TokenCursor
    {
        private readonly IReadOnlyList<SqlToken> _tokens;
        public int Position;

        public TokenCursor(IReadOnlyList<SqlToken> tokens) { _tokens = tokens; }

        public SqlToken Current => _tokens[Math.Min(Position, _tokens.Count - 1)];
        public bool AtEnd => Current.Type == SqlTokenType.Eof;

        public SqlToken Peek(int offset)
        {
            var index = Position + offset;
            return index >= _tokens.Count ? _tokens[^1] : _tokens[index];
        }

        public void Advance() { if (Position < _tokens.Count - 1) Position++; }

        public void MatchKeyword(string keyword) { if (Current.IsKeyword(keyword)) Advance(); }
        public void MatchPunctuation(string text) { if (Current.IsPunctuation(text)) Advance(); }
    }

    private static (string Schema, string? Name) ReadSchemaQualifiedName(TokenCursor cursor)
    {
        var parts = new List<string>();
        if (cursor.Current.Type != SqlTokenType.Identifier) return ("dbo", null);

        parts.Add(cursor.Current.Text);
        cursor.Advance();

        while (cursor.Current.IsPunctuation(".") && cursor.Peek(1).Type == SqlTokenType.Identifier)
        {
            cursor.Advance();
            parts.Add(cursor.Current.Text);
            cursor.Advance();
        }

        // Up to 3 parts supported here: [db.]schema.name (a 4th, server-qualified, part is
        // outside this analyzer's supported scope and collapses into "name" as the last
        // segment, with the extra leading segments ignored - same as an unresolved linked-
        // server reference would be, just without a dedicated diagnostic per segment).
        return parts.Count switch
        {
            1 => ("dbo", parts[0]),
            2 => (parts[0], parts[1]),
            >= 3 => (parts[^2], parts[^1]),
            _ => ("dbo", null)
        };
    }

    private static string? ReadSingleIdentifier(TokenCursor cursor)
    {
        if (cursor.Current.Type != SqlTokenType.Identifier) return null;
        var text = cursor.Current.Text;
        cursor.Advance();
        return text;
    }

    private static List<string> ReadParenColumnList(TokenCursor cursor)
    {
        var columns = new List<string>();
        if (!cursor.Current.IsPunctuation("(")) return columns;
        cursor.Advance();
        while (!cursor.AtEnd && !cursor.Current.IsPunctuation(")"))
        {
            if (cursor.Current.Type == SqlTokenType.Identifier) columns.Add(cursor.Current.Text);
            cursor.Advance();
            if (cursor.Current.IsPunctuation(",")) cursor.Advance();
        }
        cursor.MatchPunctuation(")");
        return columns;
    }

    private static void SkipBalancedParens(TokenCursor cursor)
    {
        if (!cursor.Current.IsPunctuation("(")) return;
        var depth = 0;
        do
        {
            if (cursor.Current.IsPunctuation("(")) depth++;
            else if (cursor.Current.IsPunctuation(")")) depth--;
            cursor.Advance();
        } while (depth > 0 && !cursor.AtEnd);
    }

    /// <summary>Reads one comma-separated item at the current paren depth (used inside an outer parenthesized list), leaving the cursor on the terminating ',' or ')'.</summary>
    private static List<SqlToken> ReadTopLevelCommaItem(TokenCursor cursor)
    {
        var item = new List<SqlToken>();
        var depth = 0;
        while (!cursor.AtEnd)
        {
            if (depth == 0 && (cursor.Current.IsPunctuation(",") || cursor.Current.IsPunctuation(")"))) break;
            if (cursor.Current.IsPunctuation("(")) depth++;
            if (cursor.Current.IsPunctuation(")")) depth--;
            item.Add(cursor.Current);
            cursor.Advance();
        }
        return item;
    }

    private static List<SqlToken> ReadUntilTopLevelKeyword(TokenCursor cursor, string keyword)
    {
        var tokens = new List<SqlToken>();
        var depth = 0;
        while (!cursor.AtEnd)
        {
            if (depth == 0 && cursor.Current.IsKeyword(keyword)) break;
            if (cursor.Current.IsPunctuation("(")) depth++;
            if (cursor.Current.IsPunctuation(")")) depth--;
            tokens.Add(cursor.Current);
            cursor.Advance();
        }
        return tokens;
    }

    private static List<List<SqlToken>> SplitTopLevelComma(List<SqlToken> tokens)
    {
        var items = new List<List<SqlToken>>();
        var current = new List<SqlToken>();
        var depth = 0;
        foreach (var token in tokens)
        {
            if (token.IsPunctuation("(")) depth++;
            if (token.IsPunctuation(")")) depth--;
            if (depth == 0 && token.IsPunctuation(","))
            {
                items.Add(current);
                current = new List<SqlToken>();
                continue;
            }
            current.Add(token);
        }
        if (current.Count > 0) items.Add(current);
        return items;
    }

    private static void SkipToStatementBoundary(TokenCursor cursor)
    {
        var depth = 0;
        while (!cursor.AtEnd)
        {
            if (cursor.Current.IsPunctuation("(")) depth++;
            if (cursor.Current.IsPunctuation(")")) depth--;
            if (depth <= 0 && cursor.Current.IsPunctuation(";")) { cursor.Advance(); return; }
            if (depth <= 0 && (cursor.Current.IsKeyword("GO") || cursor.Current.IsKeyword("END"))) return;
            cursor.Advance();
        }
    }

    private static string DescribeUpcoming(TokenCursor cursor)
    {
        var parts = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            var token = cursor.Peek(i);
            if (token.Type == SqlTokenType.Eof) break;
            parts.Add(token.Text);
        }
        return string.Join(' ', parts);
    }

    // ---------------------------------------------------------------- identity + emission

    private string Location(int line) => $"{_relativePath}:{line}-{line}";

    private static string DatabaseId(string db) => $"sql-database:{db}";
    private static string SchemaId(string db, string schema) => $"sql-schema:{db}.{schema}";
    private static string TableId(string db, string schema, string name) => $"sql-table:{db}.{schema}.{name}";
    private static string ViewId(string db, string schema, string name) => $"sql-view:{db}.{schema}.{name}";
    private static string ProcedureId(string db, string schema, string name) => $"sql-procedure:{db}.{schema}.{name}";
    private static string FunctionId(string db, string schema, string name) => $"sql-function:{db}.{schema}.{name}";
    private static string ColumnId(string ownerExternalId, string column) => $"{ownerExternalId}/column:{column}";

    private void EnsureSchema(string db, string schema, int line)
    {
        var dbId = DatabaseId(db);
        EmitEntity("SqlDatabase", db, dbId, null);
        var schemaId = SchemaId(db, schema);
        EmitEntity("SqlSchema", $"{db}.{schema}", schemaId, null);
        EmitRelationship(dbId, schemaId, "DATABASE_CONTAINS_SCHEMA", line);
    }

    private void EmitEntity(string entityType, string canonicalName, string externalId, string? sourceLocation)
    {
        if (!_seenEntityIds.Add(externalId)) return;
        _entities.Add(new CodeEntityObservation(null, entityType, canonicalName, externalId, sourceLocation));
    }

    private void EmitRelationship(string subjectExternalId, string objectExternalId, string predicate, int line, decimal confidence = 1.0m)
    {
        var key = $"{subjectExternalId}|{predicate}|{objectExternalId}";
        if (!_seenRelationshipKeys.Add(key)) return;
        _relationships.Add(new CodeRelationshipObservation(Guid.Empty, Location(line), subjectExternalId, objectExternalId, predicate, confidence));
    }

    private void EmitUnresolved(string category, string detail, string anchorExternalId, int line)
    {
        _unresolvedCount++;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{_relativePath}|{line}|{category}|{detail}")))[..32];
        var unresolvedId = $"sql-unresolved:{hash}";
        EmitEntity("SqlUnresolvedReference", $"{category}: {detail}", unresolvedId, Location(line));
        var key = $"{anchorExternalId}|UNRESOLVED_REFERENCE|{unresolvedId}";
        if (!_seenRelationshipKeys.Add(key)) return;
        _relationships.Add(new CodeRelationshipObservation(Guid.Empty, Location(line), anchorExternalId, unresolvedId, "UNRESOLVED_REFERENCE", 0m));
    }
}
