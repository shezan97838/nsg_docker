namespace NSG.Mobile.Domain.Exceptions;

/// <summary>
/// Base domain exception for business rule violations.
/// These are distinct from infrastructure/validation exceptions.
/// </summary>
public class DomainException : Exception
{
    public string Code { get; }

    public DomainException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public DomainException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }
}

/// <summary>Thrown when a requested entity does not exist.</summary>
public class EntityNotFoundException : DomainException
{
    public string EntityType { get; }
    public string EntityId { get; }

    public EntityNotFoundException(string entityType, string entityId)
        : base("ENTITY_NOT_FOUND", $"{entityType} with ID '{entityId}' was not found.")
    {
        EntityType = entityType;
        EntityId = entityId;
    }
}

/// <summary>Thrown when a business rule is violated (e.g., duplicate registration, invalid state transition).</summary>
public class BusinessRuleException : DomainException
{
    public BusinessRuleException(string code, string message)
        : base(code, message)
    {
    }
}

/// <summary>Thrown when an operation is attempted on an account with an incompatible status.</summary>
public class InvalidAccountStateException : DomainException
{
    public InvalidAccountStateException(string message)
        : base("INVALID_ACCOUNT_STATE", message)
    {
    }
}
