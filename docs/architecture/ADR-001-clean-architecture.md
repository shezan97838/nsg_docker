# ADR-001: Clean Architecture

**Status**: Accepted  
**Date**: 2025-08-01  
**SOW Reference**: §4 Technical Requirements — "ASP.NET Core Web API / .NET 8"

## Context
The NSG Mobile API requires a maintainable, testable, VAPT-ready architecture serving 10,000+ users across Android and iOS.

## Decision
Adopt Clean Architecture with four projects: Domain, Application, Infrastructure, API. Strict unidirectional dependency: API → Application → Domain; Infrastructure → Application + Domain.

## Consequences
- Domain has zero external dependencies (pure C#)
- Business logic is testable without infrastructure
- Infrastructure is swappable (e.g., Redis provider, SMS gateway)
- Slightly more boilerplate than a monolithic approach
