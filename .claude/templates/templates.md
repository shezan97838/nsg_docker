# Templates

## New Entity Template

```csharp
// src/NSG.Mobile.Domain/Entities/MyEntity.cs
namespace NSG.Mobile.Domain.Entities;

public class MyEntity
{
    public int Id { get; set; }
    // Properties here
    public DateTime CreatedAtIst { get; set; } = IndianStandardTime.Now;
    public DateTime UpdatedAtIst { get; set; } = IndianStandardTime.Now;
}
```

## New EF Configuration Template

```csharp
// src/NSG.Mobile.Infrastructure/Persistence/Configurations/MyEntityConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class MyEntityConfiguration : IEntityTypeConfiguration<MyEntity>
{
    public void Configure(EntityTypeBuilder<MyEntity> builder)
    {
        builder.ToTable("MyEntities");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).UseIdentityColumn();
        // Column configs, indexes, relationships
        builder.Property(e => e.CreatedAt).IsRequired();
    }
}
```

## New Repository Interface Template

```csharp
// src/NSG.Mobile.Application/Interfaces/Repositories/IMyEntityRepository.cs
namespace NSG.Mobile.Application.Interfaces.Repositories;

public interface IMyEntityRepository
{
    Task<MyEntity?> GetByIdAsync(int id, CancellationToken ct = default);
    Task AddAsync(MyEntity entity, CancellationToken ct = default);
    Task UpdateAsync(MyEntity entity, CancellationToken ct = default);
}
```

## New Service Interface Template

```csharp
// src/NSG.Mobile.Application/{Feature}/Interfaces/IMyService.cs
public interface IMyService
{
    Task<Result<MyResponseDto>> DoSomethingAsync(MyRequestDto request, CancellationToken ct = default);
}
```

## New DTO Template

```csharp
// Request DTO — mutable, validated by FluentValidation
public class MyRequestDto
{
    public string Field { get; set; } = string.Empty;
}

// Response DTO — NEVER expose PasswordHash, tokens, OTP, sensitive PII
public class MyResponseDto
{
    public int Id { get; set; }
    public string Field { get; set; } = string.Empty;
}
```

## New Validator Template

```csharp
public class MyRequestValidator : AbstractValidator<MyRequestDto>
{
    public MyRequestValidator()
    {
        RuleFor(x => x.Field).NotEmpty().MaximumLength(200);
    }
}
```

## New Controller Template

```csharp
[ApiController]
[Route("api/v1/my-resource")]
[Authorize]
[Produces("application/json")]
public class MyController : ControllerBase
{
    private readonly IMyService _service;
    public MyController(IMyService service) => _service = service;

    [HttpGet]
    [ProducesResponseType(typeof(MyResponseDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var result = await _service.DoSomethingAsync(ct);
        return result.IsSuccess ? Ok(result.Data) : BadRequest(/* ProblemDetails */);
    }
}
```

## New ADR Template

```markdown
# ADR-NNN: Title

**Status**: Proposed | Accepted | Deprecated | Superseded
**Date**: YYYY-MM-DD
**SOW Reference**: §X.Y

## Context
Why is this decision needed?

## Decision
What was decided?

## Consequences
What are the trade-offs?
```
