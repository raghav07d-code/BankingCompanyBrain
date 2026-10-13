using BankingBrain.Application.Common.CodeIntelligence.Projects;
using BankingBrain.Application.Common.CodeIntelligence.Semantics;

namespace BankingBrain.Application.Common.ApiIntelligence;

public interface IApiEndpointDiscoveryAnalyzer
{
    /// <summary>
    /// Discovers ASP.NET Core endpoints (Minimal API Map* registrations and
    /// attribute-routed controller actions) from a repository's own Roslyn compilations,
    /// and resolves each endpoint's handler to an existing Code Intelligence Method entity
    /// (<c>csharp:{FullyQualifiedName}</c>) wherever statically resolvable - directly for a
    /// method reference or controller action, through the handler lambda's own call
    /// expressions for a Minimal API lambda, and through MediatR's
    /// <c>IRequestHandler&lt;TRequest,TResponse&gt;</c> for the common <c>sender.Send(new
    /// XCommand(...))</c> dispatch shape. Never guesses: an unresolvable handler becomes an
    /// UNRESOLVED_REFERENCE relationship, not a fabricated ENDPOINT_INVOKES_METHOD edge.
    /// </summary>
    ApiEndpointDiscoveryResult Analyze(
        CodeRepositoryStructure repository,
        CodeFileDocumentIndex documentIndex,
        string repositoryExternalIdentifier);
}

public sealed record ApiEndpointDiscoveryResult(
    IReadOnlyList<CodeEntityObservation> Entities,
    IReadOnlyList<CodeRelationshipObservation> Relationships,
    int EndpointsDiscovered,
    int EndpointsResolved,
    int EndpointsUnresolved);
