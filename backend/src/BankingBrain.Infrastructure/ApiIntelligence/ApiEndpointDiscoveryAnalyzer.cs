using System.Security.Cryptography;
using System.Text;
using BankingBrain.Application.Common.ApiIntelligence;
using BankingBrain.Application.Common.CodeIntelligence.Projects;
using BankingBrain.Application.Common.CodeIntelligence.Semantics;
using BankingBrain.Infrastructure.CodeIntelligence.Projects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace BankingBrain.Infrastructure.ApiIntelligence;

/// <summary>
/// Discovers ASP.NET Core endpoints from the exact same Roslyn compilations Code
/// Intelligence would build for this repository (via the shared
/// <see cref="RoslynProjectCompiler"/>), and resolves each one's handler wherever a real,
/// statically-checkable binding exists:
///   - Minimal API (<c>Map(Get|Post|Put|Delete|Patch|Methods)</c>): the route is read from
///     the call's literal route-template argument (a non-literal route is not discovered
///     at all - there is nothing stable to name it by). The handler argument is resolved
///     either directly (a method-group reference) or, for an inline lambda, by walking the
///     lambda body's own call expressions: a MediatR-shaped <c>sender.Send(new
///     XCommand(...))</c> resolves to whichever class in the repository implements
///     <c>MediatR.IRequestHandler&lt;XCommand,*&gt;</c>'s <c>Handle</c> method; any other
///     call to a method declared within this same repository resolves directly.
///   - Attribute-routed controllers (<c>ControllerBase</c>/<c>Controller</c> subclasses,
///     actions carrying an <c>[Http*]</c> attribute with an explicit route-template
///     argument, combined with an optional class-level <c>[Route]</c> prefix): the handler
///     is the action method itself, always resolvable.
/// Everything else - a lambda with no resolvable call, a handler argument that is neither a
/// literal lambda nor a direct method reference, an unresolvable MediatR command, action
/// routing driven by naming convention rather than an explicit route attribute - is recorded
/// as an UNRESOLVED_REFERENCE relationship rather than fabricated.
/// </summary>
public sealed class ApiEndpointDiscoveryAnalyzer : IApiEndpointDiscoveryAnalyzer
{
    private const decimal ResolvedConfidence = 1.0m;
    private static readonly string[] MinimalApiMapMethods = { "MapGet", "MapPost", "MapPut", "MapDelete", "MapPatch" };
    private static readonly string[] HttpAttributeNames = { "HttpGetAttribute", "HttpPostAttribute", "HttpPutAttribute", "HttpDeleteAttribute", "HttpPatchAttribute", "HttpHeadAttribute", "HttpOptionsAttribute" };

    private readonly ICodeCompilationBuilder _compilationBuilder;

    public ApiEndpointDiscoveryAnalyzer(ICodeCompilationBuilder compilationBuilder)
    {
        _compilationBuilder = compilationBuilder;
    }

    public ApiEndpointDiscoveryResult Analyze(
        CodeRepositoryStructure repository,
        CodeFileDocumentIndex documentIndex,
        string repositoryExternalIdentifier)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(documentIndex);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryExternalIdentifier);

        var compilationPlan = _compilationBuilder.Build(repository);
        var (orderedProjects, compilations) = RoslynProjectCompiler.Build(compilationPlan);

        var entities = new List<CodeEntityObservation>();
        var relationships = new List<CodeRelationshipObservation>();
        var seenEntityIds = new HashSet<string>(StringComparer.Ordinal);
        var seenRelationshipKeys = new HashSet<string>(StringComparer.Ordinal);
        var discovered = 0;
        var resolved = 0;
        var unresolved = 0;

        // Pass 1: index every MediatR request handler in the repository by the FQN of the
        // request (command/query) type it handles, across every project's own compilation -
        // a handler can live in a different project than the endpoint that dispatches to it
        // (e.g. Application vs. Api), and this repository-wide index is built once up front
        // rather than re-searched per endpoint.
        var handlerMethodFqnByRequestFqn = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var compilation in compilations.Values)
        {
            foreach (var tree in compilation.SyntaxTrees)
            {
                var semanticModel = compilation.GetSemanticModel(tree);

                foreach (var classDeclaration in tree.GetRoot().DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    if (semanticModel.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol classSymbol)
                        continue;

                    foreach (var handlerInterface in classSymbol.AllInterfaces)
                    {
                        if (handlerInterface.Name != "IRequestHandler" || handlerInterface.TypeArguments.Length != 2)
                            continue;

                        var requestFqn = GetSymbolName(handlerInterface.TypeArguments[0]);
                        var handleMethod = classSymbol.GetMembers("Handle").OfType<IMethodSymbol>().FirstOrDefault();

                        if (handleMethod is not null)
                            handlerMethodFqnByRequestFqn[requestFqn] = GetSymbolName(handleMethod);
                    }
                }
            }
        }

        // Pass 2: discover endpoints per project, in the same dependency order Code
        // Intelligence compiles them, resolving each handler against the index above.
        foreach (var project in orderedProjects)
        {
            var compilation = compilations[project.ProjectFilePath];

            foreach (var tree in compilation.SyntaxTrees)
            {
                if (tree.FilePath is null)
                    continue;

                var semanticModel = compilation.GetSemanticModel(tree);
                var root = tree.GetRoot();

                foreach (var observation in DiscoverMinimalApiEndpoints(semanticModel, root, tree.FilePath, documentIndex, repositoryExternalIdentifier, handlerMethodFqnByRequestFqn))
                {
                    Record(observation, entities, relationships, seenEntityIds, seenRelationshipKeys, ref discovered, ref resolved, ref unresolved);
                }

                foreach (var observation in DiscoverControllerActions(semanticModel, root, tree.FilePath, documentIndex, repositoryExternalIdentifier))
                {
                    Record(observation, entities, relationships, seenEntityIds, seenRelationshipKeys, ref discovered, ref resolved, ref unresolved);
                }
            }
        }

        return new ApiEndpointDiscoveryResult(entities, relationships, discovered, resolved, unresolved);
    }

    private sealed record EndpointObservation(
        IReadOnlyList<CodeEntityObservation> Entities,
        IReadOnlyList<CodeRelationshipObservation> Relationships,
        bool Resolved);

    private static void Record(
        EndpointObservation observation,
        List<CodeEntityObservation> entities,
        List<CodeRelationshipObservation> relationships,
        HashSet<string> seenEntityIds,
        HashSet<string> seenRelationshipKeys,
        ref int discovered,
        ref int resolved,
        ref int unresolved)
    {
        discovered++;
        if (observation.Resolved) resolved++; else unresolved++;

        foreach (var entity in observation.Entities)
        {
            if (seenEntityIds.Add(entity.ExternalIdentifier))
                entities.Add(entity);
        }

        foreach (var relationship in observation.Relationships)
        {
            var key = $"{relationship.SubjectExternalIdentifier}|{relationship.Predicate}|{relationship.ObjectExternalIdentifier}";
            if (seenRelationshipKeys.Add(key))
                relationships.Add(relationship);
        }
    }

    // ---------------------------------------------------------------- Minimal API discovery

    private static IEnumerable<EndpointObservation> DiscoverMinimalApiEndpoints(
        SemanticModel semanticModel,
        SyntaxNode root,
        string filePath,
        CodeFileDocumentIndex documentIndex,
        string repositoryExternalIdentifier,
        IReadOnlyDictionary<string, string> handlerMethodFqnByRequestFqn)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                continue;

            var methodName = memberAccess.Name.Identifier.Text;
            var isMapMethods = methodName == "MapMethods";

            if (!isMapMethods && !MinimalApiMapMethods.Contains(methodName))
                continue;

            if (invocation.ArgumentList.Arguments.Count < 2)
                continue;

            var routeArgument = invocation.ArgumentList.Arguments[0].Expression;
            if (!TryGetLiteralString(routeArgument, out var route))
                continue;

            var httpMethods = isMapMethods
                ? ExtractHttpMethodsFromCollection(invocation.ArgumentList.Arguments.ElementAtOrDefault(1)?.Expression)
                : new[] { methodName["Map".Length..].ToUpperInvariant() };

            if (httpMethods.Count == 0)
                continue;

            var handlerArgument = isMapMethods
                ? invocation.ArgumentList.Arguments.ElementAtOrDefault(2)?.Expression
                : invocation.ArgumentList.Arguments[1].Expression;

            if (handlerArgument is null)
                continue;

            var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var documentVersionId = ResolveDocumentVersionId(documentIndex, filePath);

            foreach (var httpMethod in httpMethods)
            {
                yield return BuildEndpointObservation(
                    httpMethod,
                    route,
                    repositoryExternalIdentifier,
                    documentIndex,
                    filePath,
                    documentVersionId,
                    line,
                    () => ResolveMinimalApiHandler(semanticModel, handlerArgument, handlerMethodFqnByRequestFqn));
            }
        }
    }

    private static IReadOnlyList<string> ExtractHttpMethodsFromCollection(ExpressionSyntax? collectionExpression)
    {
        if (collectionExpression is null)
            return Array.Empty<string>();

        var methods = new List<string>();
        foreach (var literal in collectionExpression.DescendantNodesAndSelf().OfType<LiteralExpressionSyntax>())
        {
            if (literal.IsKind(SyntaxKind.StringLiteralExpression))
                methods.Add(literal.Token.ValueText.ToUpperInvariant());
        }
        return methods;
    }

    /// <summary>
    /// Resolves a Minimal API handler argument to the set of repository-declared methods it
    /// statically invokes. A direct method-group reference resolves to exactly that method.
    /// A lambda resolves by walking its own call expressions: a MediatR
    /// <c>sender.Send(new XCommand(...))</c> resolves through the handler index; any other
    /// call whose target is declared in this repository resolves directly. Returns an empty
    /// list (not null) when nothing is resolvable, so the caller can tell "no calls found"
    /// (the lambda calls nothing we can see) apart from "argument not understood at all".
    /// </summary>
    private static (IReadOnlyList<string> TargetFqns, bool Unresolvable) ResolveMinimalApiHandler(
        SemanticModel semanticModel,
        ExpressionSyntax handlerArgument,
        IReadOnlyDictionary<string, string> handlerMethodFqnByRequestFqn)
    {
        if (handlerArgument is not AnonymousFunctionExpressionSyntax lambda)
        {
            // Method-group reference (e.g. MapGet("/x", Handlers.GetX)) or any other
            // non-lambda expression: resolve its symbol directly.
            var symbol = semanticModel.GetSymbolInfo(handlerArgument).Symbol;
            return symbol is IMethodSymbol methodSymbol
                ? (new[] { GetSymbolName(methodSymbol) }, false)
                : (Array.Empty<string>(), true);
        }

        var targets = new List<string>();
        var sawAnyCall = false;
        var sawUnresolvedMediatrSend = false;

        foreach (var innerInvocation in lambda.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            sawAnyCall = true;

            if (innerInvocation.Expression is MemberAccessExpressionSyntax memberAccess &&
                memberAccess.Name.Identifier.Text == "Send" &&
                innerInvocation.ArgumentList.Arguments.Count >= 1 &&
                innerInvocation.ArgumentList.Arguments[0].Expression is ObjectCreationExpressionSyntax creation)
            {
                var requestType = semanticModel.GetTypeInfo(creation).Type;

                if (requestType is not null &&
                    handlerMethodFqnByRequestFqn.TryGetValue(GetSymbolName(requestType), out var handlerMethodFqn))
                {
                    targets.Add(handlerMethodFqn);
                }
                else
                {
                    sawUnresolvedMediatrSend = true;
                }

                continue;
            }

            if (semanticModel.GetSymbolInfo(innerInvocation).Symbol is IMethodSymbol calledMethod &&
                IsDeclaredInSource(calledMethod))
            {
                targets.Add(GetSymbolName(calledMethod));
            }
        }

        if (targets.Count > 0)
            return (targets.Distinct(StringComparer.Ordinal).ToArray(), false);

        // A lambda that calls nothing resolvable is only "unresolvable" (worth flagging) if
        // it called something we could see but not pin down (an unmatched MediatR Send); a
        // lambda with literally no calls at all - or only calls to framework/BCL code - has
        // nothing to claim, resolved or not, and is left with zero relationships.
        _ = sawAnyCall; // a lambda with no calls at all has nothing to claim - not an "unresolvable" case.
        return (Array.Empty<string>(), sawUnresolvedMediatrSend);
    }

    // ---------------------------------------------------------------- Attribute-routed controllers

    private static IEnumerable<EndpointObservation> DiscoverControllerActions(
        SemanticModel semanticModel,
        SyntaxNode root,
        string filePath,
        CodeFileDocumentIndex documentIndex,
        string repositoryExternalIdentifier)
    {
        foreach (var classDeclaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (semanticModel.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol classSymbol)
                continue;

            if (!IsController(classSymbol))
                continue;

            var classPrefix = GetRouteAttributeTemplate(classSymbol.GetAttributes());

            foreach (var method in classDeclaration.Members.OfType<MethodDeclarationSyntax>())
            {
                if (semanticModel.GetDeclaredSymbol(method) is not IMethodSymbol methodSymbol)
                    continue;

                var httpAttribute = methodSymbol.GetAttributes()
                    .FirstOrDefault(attribute => HttpAttributeNames.Contains(attribute.AttributeClass?.Name));

                if (httpAttribute is null)
                    continue;

                var httpMethod = httpAttribute.AttributeClass!.Name["Http".Length..^"Attribute".Length].ToUpperInvariant();
                var actionRoute = httpAttribute.ConstructorArguments.Length > 0 &&
                                   httpAttribute.ConstructorArguments[0].Value is string explicitRoute
                    ? explicitRoute
                    : null;

                if (classPrefix is null && actionRoute is null)
                    continue; // No explicit route anywhere: convention-based routing is not discovered.

                var route = CombineRoute(classPrefix, actionRoute);
                var line = method.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                var documentVersionId = ResolveDocumentVersionId(documentIndex, filePath);

                yield return BuildEndpointObservation(
                    httpMethod,
                    route,
                    repositoryExternalIdentifier,
                    documentIndex,
                    filePath,
                    documentVersionId,
                    line,
                    () => (new[] { GetSymbolName(methodSymbol) }, false));
            }
        }
    }

    private static bool IsController(INamedTypeSymbol classSymbol)
    {
        if (classSymbol.GetAttributes().Any(attribute => attribute.AttributeClass?.Name == "ApiControllerAttribute"))
            return true;

        for (var type = classSymbol.BaseType; type is not null; type = type.BaseType)
        {
            if (type.Name is "ControllerBase" or "Controller")
                return true;
        }

        return false;
    }

    private static string? GetRouteAttributeTemplate(System.Collections.Immutable.ImmutableArray<AttributeData> attributes)
    {
        var routeAttribute = attributes.FirstOrDefault(attribute => attribute.AttributeClass?.Name == "RouteAttribute");

        return routeAttribute?.ConstructorArguments.Length > 0 &&
               routeAttribute.ConstructorArguments[0].Value is string template
            ? template
            : null;
    }

    private static string CombineRoute(string? prefix, string? actionRoute)
    {
        var segments = new[] { prefix, actionRoute }
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .Select(segment => segment!.Trim('/'));

        var combined = string.Join('/', segments);
        return combined.StartsWith('/') ? combined : "/" + combined;
    }

    // ---------------------------------------------------------------- shared construction

    private static EndpointObservation BuildEndpointObservation(
        string httpMethod,
        string route,
        string repositoryExternalIdentifier,
        CodeFileDocumentIndex documentIndex,
        string filePath,
        Guid? documentVersionId,
        int line,
        Func<(IReadOnlyList<string> TargetFqns, bool Unresolvable)> resolveHandler)
    {
        var canonicalName = $"{httpMethod} {route}";
        var endpointExternalId = $"api-endpoint:{repositoryExternalIdentifier}:{canonicalName}";
        var relativePath = RelativePath(documentIndex, filePath);
        var sourceLocation = $"{relativePath}:{line}-{line}";

        var entityList = new List<CodeEntityObservation>
        {
            new(documentVersionId, "ApiEndpoint", canonicalName, endpointExternalId, sourceLocation)
        };

        var relationshipList = new List<CodeRelationshipObservation>();
        var isResolved = false;

        if (documentVersionId is not null)
        {
            var (targetFqns, unresolvable) = resolveHandler();

            if (targetFqns.Count > 0)
            {
                isResolved = true;
                foreach (var targetFqn in targetFqns)
                {
                    // A placeholder so this relationship never silently drops at persistence
                    // for lack of a known object entity, regardless of whether Code
                    // Intelligence's own run (which creates the authoritative Method entity
                    // with this exact ExternalIdentifier) has executed yet - the two
                    // extractors are queued as independent, unordered background workers.
                    // Entity creation is idempotent by ExternalIdentifier either way.
                    entityList.Add(new CodeEntityObservation(null, "Method", targetFqn, $"csharp:{targetFqn}", null));

                    relationshipList.Add(new CodeRelationshipObservation(
                        documentVersionId.Value,
                        sourceLocation,
                        endpointExternalId,
                        $"csharp:{targetFqn}",
                        "ENDPOINT_INVOKES_METHOD",
                        ResolvedConfidence));
                }
            }
            else if (unresolvable)
            {
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{relativePath}|{line}|{canonicalName}")))[..32];
                var unresolvedId = $"api-unresolved:{hash}";

                entityList.Add(new CodeEntityObservation(
                    documentVersionId,
                    "ApiUnresolvedReference",
                    $"unresolved handler: {canonicalName}",
                    unresolvedId,
                    sourceLocation));

                relationshipList.Add(new CodeRelationshipObservation(
                    documentVersionId.Value,
                    sourceLocation,
                    endpointExternalId,
                    unresolvedId,
                    "UNRESOLVED_REFERENCE",
                    0m));
            }
        }

        return new EndpointObservation(entityList, relationshipList, isResolved);
    }

    private static Guid? ResolveDocumentVersionId(CodeFileDocumentIndex documentIndex, string absolutePath)
    {
        var relativePath = RelativePath(documentIndex, absolutePath);

        return documentIndex.DocumentVersionIdByRelativePath.TryGetValue(relativePath, out var versionId)
            ? versionId
            : null;
    }

    private static string RelativePath(CodeFileDocumentIndex documentIndex, string absolutePath) =>
        Path.GetRelativePath(documentIndex.RepositoryRootPath, absolutePath).Replace('\\', '/');

    private static bool TryGetLiteralString(ExpressionSyntax expression, out string value)
    {
        if (expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
        {
            value = literal.Token.ValueText;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool IsDeclaredInSource(ISymbol symbol) =>
        symbol.Locations.Any(location => location.IsInSource);

    private static string GetSymbolName(ISymbol symbol) =>
        symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
}
