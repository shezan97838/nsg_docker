# Open Questions

Questions requiring client/stakeholder clarification. Do NOT resolve silently.

## SOW Contradictions

| ID | Question | SOW Refs | Impact |
|----|----------|----------|--------|
| OQ-001 | Salary archive: 7 years (§3.3) vs 24 months (§6 compliance matrix)? | §3.3, §6 | Data retention, storage sizing |
| OQ-002 | JWT sufficient or full OAuth 2.0 authorization server required? | §3.8, §4 | Architecture, infrastructure |
| OQ-003 | Login MFA: all 3 factors every time, or Password + one of (OTP, Biometric)? | §3.8, §3.7 | Auth flow complexity |

## Technical Questions

| ID | Question | Context |
|----|----------|---------|
| OQ-004 | Biometric server verification: device-bound proof sufficient? | Server never sees raw biometric data |
| OQ-005 | Platform-safe device ID acceptable instead of raw IMEI? | IMEI restricted on Android 10+, iOS |
| OQ-006 | NIC SMS Gateway API contract (URL, auth, schema, templates)? | Required for OTP delivery |
| OQ-007 | HRMS API contract (URL, auth, endpoints, data schema)? | Required for PIS and onboarding |
| OQ-008 | Payroll API contract (URL, auth, salary/Form-16 format)? | Required for salary/Form-16 modules |
| OQ-009 | FCM/APNs configuration (project ID, credentials)? | Required for push notifications |

## Business Logic Questions

| ID | Question | Context |
|----|----------|---------|
| OQ-010 | Complete role/permission matrix per module? | Currently minimal: Employee, ITAdmin, NodalOfficer, SystemAdmin |
| OQ-011 | Grievance routing rules by category? | SOW mentions Nodal Officer + HQ NSG |
| OQ-012 | Which PIS fields are user-editable vs approval-required? | SOW §3.1 mentions "photo or contact info" |
| OQ-013 | New device registration: requires IT approval or re-auth sufficient? | Currently re-auth only |
| OQ-014 | Data residency: NSG infrastructure or NIC Cloud? | Deployment configuration |
| OQ-015 | Form-16 archive: 7 financial years or 7 calendar years? | §3.4 says "7-year" |
