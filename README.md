# PTFRegistrationApp-BE (Azure Functions CHMeetings Proxy)

Production-ready Azure Functions backend that provides **allowlisted same-origin endpoints** for Angular FE and proxies requests to CHMeetings (`https://api.chmeetings.com`).

## Architecture summary

- Azure Functions v4 (.NET 6, in-process) with explicit HTTP-triggered routes.
- `ChmProxyFunctions` validates request method + required payload/query fields before proxying.
- `ChmProxyService` forwards only required headers (`Authorization`, `Content-Type`, `x-correlation-id`), applies retry + timeout, and sanitizes upstream errors.
- Correlation IDs are propagated to logs, upstream calls, and response header (`x-correlation-id`).
- CORS is controlled by `ALLOWED_ORIGINS` environment variable and reflected only for allowlisted origins.
- `GET /api/health` endpoint for liveness checks.
- OpenAPI annotations added on health endpoint and extensible to all endpoints.

## Implemented endpoints

- `POST /Account/Signin`
- `GET /33CE95026156648A/Meetings/Event/ListEvents`
- `GET /33CE95026156648A/Meetings/Event/EventDetails`
- `GET /33CE95026156648A/Meetings/Event/ListEventSchedules`
- `POST /33CE95026156648A/Meetings/Schedule/AddOrRemoveAttendance`
- `POST /33CE95026156648A/Core/Member/ListMembers`
- `GET /33CE95026156648A/Meetings/Schedule/ListMemberSchedules`
- `GET /33CE95026156648A/Core/Member/GetMemberMeetingsInfo`
- `GET /api/health`

> Note: Service ID is validated against `CHM_SERVICE_ID`.

## Local run

1. Install .NET 6 SDK and Azure Functions Core Tools v4.
2. Copy settings:
   - `cp PTFRegistrationApp-BE/local.settings.json.example PTFRegistrationApp-BE/local.settings.json`
3. From repo root run:
   - `dotnet build PTFRegistrationApp-BE.slnx`
   - `func start --script-root PTFRegistrationApp-BE`

## Deploy to Azure Functions

1. Create Function App (v4, .NET 6) and Application Insights.
2. Set app settings:
   - `CHM_BASE_URL`
   - `CHM_SERVICE_ID`
   - `ALLOWED_ORIGINS`
   - `CHM_TIMEOUT_SECONDS`
   - `CHM_RETRY_COUNT`
3. Deploy via zip deploy or `func azure functionapp publish <app-name>`.
4. Configure CORS in platform if required (recommended to mirror `ALLOWED_ORIGINS`).

## Security notes

- This is **not an open proxy**: only explicitly coded routes are exposed.
- Attendance flows should first call `ListEventSchedules` to get the concrete `ScheduleId` for the selected event/date, then send that `ScheduleId` in `AddOrRemoveAttendance`.
- Service ID is validated for tenant isolation.
- Methods are strictly checked (`405` for mismatch).
- Required fields are validated (`400` on invalid input).
- Upstream errors are sanitized to avoid leaking internals.
- No secrets are hardcoded in source.

## curl examples

```bash
curl -i -X POST http://localhost:7071/Account/Signin \
  -H "Content-Type: application/json" \
  -d '{"UserName":"user","Password":"pass","Code":"","RememberMe":true,"ReturnUrl":"/Core/Event","ServiceId":"33CE95026156648A"}'

curl -i "http://localhost:7071/33CE95026156648A/Meetings/Event/ListEvents?StartDate=2024-01-01&EndDate=2024-01-31&CalendarsIds=null&SearchText=" \
  -H "Authorization: Bearer <token>"

curl -i "http://localhost:7071/33CE95026156648A/Meetings/Event/EventDetails?eventId=123" \
  -H "Authorization: Bearer <token>"

curl -i "http://localhost:7071/33CE95026156648A/Meetings/Event/ListEventSchedules?eventId=1120861&StartDate=2026-03-01&EndDate=2026-05-31&SearchText=" \
  -H "Authorization: Bearer <token>"

curl -i -X POST http://localhost:7071/33CE95026156648A/Meetings/Schedule/AddOrRemoveAttendance \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '[{"MemberId":1,"IsCheckIn":true,"ScheduleId":9,"FormUserEntryDto":null,"SubmissionId":null}]'

curl -i -X POST http://localhost:7071/33CE95026156648A/Core/Member/ListMembers \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"Page":1,"RowsPerPage":50,"SearchText":""}'

curl -i "http://localhost:7071/33CE95026156648A/Meetings/Schedule/ListMemberSchedules?MemberId=1&From=2024-01-01&To=null&IsCheckedInOnly=false" \
  -H "Authorization: Bearer <token>"

curl -i "http://localhost:7071/33CE95026156648A/Core/Member/GetMemberMeetingsInfo?MemberId=1&From=2024-01-01&To=null&IsCheckedInOnly=false" \
  -H "Authorization: Bearer <token>"

curl -i http://localhost:7071/api/health
```

## Postman collection

See `postman/PTFRegistrationApp-BE.postman_collection.json`.
