# .claude/ — AI Development Governance

Governance structure for AI-assisted development of the NSG Mobile API.

## Structure

| Directory | Purpose |
|-----------|---------|
| `agents/` | Role-specific AI agent instructions |
| `skills/` | Technology-specific project guidance |
| `workflows/` | Development process workflows |
| `rules/` | Architecture, coding, security rules |
| `security/` | Security baseline, threat model, controls |
| `memory/` | Stable project context and decisions |
| `requirements/` | Functional, technical, security requirements |
| `architecture/` | Current architecture and ADR index |
| `quality/` | Checklists, DoD, review criteria |
| `templates/` | Templates for common artifacts |

## Usage

1. AI sessions start by reading `CLAUDE.md` (root)
2. Role-specific agents read their `agents/` file
3. Task-specific workflows guide the development process
4. Rules are always enforced; skills are referenced as needed
