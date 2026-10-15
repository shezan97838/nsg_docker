# ADR-012: File Security

**Status**: Accepted  
**Date**: 2025-08-01  
**SOW Reference**: §3.3 (Salary PDFs), §3.4 (Form-16 PDFs), §3.5 (Circulars), §3.6 (Digital Library), §3.8 (Grievance documents)

## Context
The application handles sensitive documents: salary slips, Form-16, SOPs, grievance evidence.

## Decision
- Validate: file extension, MIME type, maximum size (10MB default, configurable)
- Generate server-side file names (GUID-based) — never trust client-provided names
- Store files **outside the web root** in a dedicated storage directory
- Prevent path traversal via `Path.GetFullPath` validation
- Authorization checked before every download (ownership or admin access)
- Audit sensitive document downloads
- Never expose internal storage paths in API responses
- Storage key format: `{category}/{yyyy/MM}/{guid}{ext}`

### File Access Pattern
```
Client → GET /api/v1/salary/slips/{id}
       → Controller checks authenticated user owns this document
       → Service retrieves file via storage key
       → Returns file stream with correct Content-Type
```

## Consequences
- Files are inaccessible via direct URL guessing
- Authorization is enforced at the application layer
- Storage mechanism is swappable (local → NFS → cloud)
