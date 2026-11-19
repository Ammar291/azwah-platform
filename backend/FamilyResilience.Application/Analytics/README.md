# Analytics module

**Owns:** De-identified, aggregated read models (`IAnalyticsQueries`) and program monitoring (`IMonitoringReadModel`);
`Suppression`; `ScoreDistributions`; `AnalyticsSettings`; export jobs (`IExportJobRepository`), `ExportCatalog`,
`Pseudonymizer`, `CsvWriter`, `IExportQueries`, `IExportFileStore`, `ExportSettings`

**Status:**
- `GET /api/v1/admin/analytics/overview` (researchers and program administrators, D-55): enrolment, assignment and
  completion counts, completion by version, a completion trend and completion by age band and employment status, counted
  in PostgreSQL, with demographic cells under `Analytics:MinimumGroupSize` (10, approved in D-58) suppressed.
- `GET /api/v1/admin/analytics/score-distributions` (researchers only, D-59, D-62): tier distributions of complete
  scores of approved rule sets for members with research-use consent, suppressed the same way.
- `GET /api/v1/admin/participation` and `GET /api/v1/admin/assessments` (program administrators): progress without
  answers or scores. Assessments filter by wave, household, instrument code, version number (with an instrument code),
  status and assigned days (D-74).
- The overview's `assignments.started` counts assignments with at least one session (D-74).
- Every read is audited.
- `POST /api/v1/admin/exports`, `GET /api/v1/admin/exports/{exportId}` and its `/file` (researchers; requester only,
  D-60): de-identified CSV exports of sessions or score results for members with research-use consent, written by the
  `analytics.export_requested.v1` outbox handler and deleted by maintenance after `Exports:RetentionDays`.
- `ExportQuotas`: at most two queued or running exports and twenty a day per researcher (D-80).
- `GET /api/v1/admin/exports` (the caller's own exports, newest first) and `GET /api/v1/admin/exports/catalog` (the
  datasets, columns and filters of `ExportCatalog`), so the dashboard never copies the allowlist (D-74).

**Other modules may use:** Nothing; this module only reads

**Must not:** Expose direct identifiers, individual answers or scores, or cells smaller than the configured minimum;
accept SQL, columns or expressions from a request
