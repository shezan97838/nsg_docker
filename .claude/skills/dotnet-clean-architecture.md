# .NET 8 & Clean Architecture Skills

## Project Pattern
- Solution: `NSG.Mobile.API.sln` with 4 projects
- Target: `net8.0`
- Dependency injection via `DependencyInjection.cs` extension methods per layer
- `builder.Services.AddApplication()` + `builder.Services.AddInfrastructure(config)`

## Entity Pattern
```csharp
// Domain entity — pure C#, no EF attributes, no NuGet
public class MyEntity {
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
```

## Service Pattern
```csharp
// Interface in Application layer
public interface IMyService {
    Task<Result<MyDto>> DoSomethingAsync(MyRequestDto request, CancellationToken ct = default);
}

// Implementation in Application layer (or Infrastructure if it needs infra)
public class MyService : IMyService {
    // Constructor injection, Result<T> returns, CancellationToken everywhere
}
```

## DTO Pattern
- Request DTOs: mutable classes with `= string.Empty` defaults
- Response DTOs: mutable classes, NEVER expose sensitive fields
- Mapping: explicit extension methods in `Mappings/` folders

## Validation Pattern
```csharp
public class MyRequestValidator : AbstractValidator<MyRequestDto> {
    public MyRequestValidator() {
        RuleFor(x => x.Field).NotEmpty().MaximumLength(200);
    }
}
```

## Repository Pattern
- Interface in `Application/Interfaces/Repositories/`
- Implementation in `Infrastructure/Repositories/`
- Focused methods (not generic CRUD)
- `AsNoTracking()` for read-only queries
- Pagination for list operations
