# Audit module

**Owns:** `AuditEvent`, `IAuditLog`, `IAuditEventRepository`

**Status:** Built

**Other modules may use:** `IAuditLog.Stage` inside a use case (commits with the change); `IAuditLog.RecordAsync` for
standalone events. The actor is the signed-in caller, the System outside a request, or Anonymous; a record with
`BySystem = true` names the System actor (and no subject) even inside a signed-in request, for what the platform itself
did there (Safeguarding's detectors raising or repeating an alert)

**Must not:** Store participant answers or free text; update or delete events (the database refuses UPDATE and
TRUNCATE, and DELETE outside the reserved retention procedure, D-81)
