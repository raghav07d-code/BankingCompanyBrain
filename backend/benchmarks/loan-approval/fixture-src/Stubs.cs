namespace MediatR
{
    public interface IRequestHandler<TRequest, TResponse>
    {
        Task<TResponse> Handle(TRequest request, CancellationToken cancellationToken);
    }

    public interface ISender
    {
        Task<TResponse> Send<TResponse>(object request, CancellationToken cancellationToken = default);
    }
}

namespace Microsoft.EntityFrameworkCore
{
    public class DbContext
    {
    }

    public class DbSet<T>
    {
        public void Add(T entity) { }
    }
}

namespace Microsoft.EntityFrameworkCore.Metadata.Builders
{
    public class EntityTypeBuilder<T>
    {
        public EntityTypeBuilder<T> ToTable(string name) => this;
    }
}

namespace LoanApprovalBenchmark
{
    public interface IEntityTypeConfiguration<T>
    {
        void Configure(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> builder);
    }

    public class EndpointRouteBuilderStub
    {
        public void MapPost(string pattern, object handler) { }
        public void MapGet(string pattern, object handler) { }
    }
}
