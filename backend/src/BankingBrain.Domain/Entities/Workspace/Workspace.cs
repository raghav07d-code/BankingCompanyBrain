namespace BankingBrain.Domain.Entities.Workspace;

public sealed class Workspace
{
    public Guid Id { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    private Workspace()
    {
    }

    private Workspace(
        Guid id,
        Guid ownerUserId,
        string name,
        string? description)
    {
        Id = id;
        OwnerUserId = ownerUserId;
        Name = name;
        Description = description;
        IsActive = true;
        CreatedOnUtc = DateTime.UtcNow;
    }

    public static Workspace Create(
        Guid ownerUserId,
        string name,
        string? description)
    {
        if (ownerUserId == Guid.Empty)
            throw new ArgumentException(
                "Owner user ID is required.",
                nameof(ownerUserId));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                "Workspace name is required.",
                nameof(name));

        return new Workspace(
            Guid.NewGuid(),
            ownerUserId,
            name.Trim(),
            string.IsNullOrWhiteSpace(description)
                ? null
                : description.Trim());
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                "Workspace name is required.",
                nameof(name));

        Name = name.Trim();
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public void Activate()
    {
        IsActive = true;
    }
}