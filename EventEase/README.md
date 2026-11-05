# EventEase

A Blazor WebAssembly (.NET 10) front end for EventEase, a fictional company that manages corporate and social events.

## Activity 1: Event Card component and routing

### Event Card (`Components/EventCard.razor`)
- Shows the event **name**, **date**, and **location**, plus category, seats left and an optional description.
- **Two-way binding:** parent pages use `<EventCard @bind-Event="evt" />`. The card edits a *draft copy*
  (`Event.Clone()`), shows a live preview while you type (`@bind:event="oninput"`), and only raises
  `EventChanged` on **Save**. **Cancel** throws the draft away.
- Validation on edit uses data annotations on `Models/Event.cs`.

### Data
- `Models/Event.cs`, `Models/Registration.cs`: simple data models with validation attributes.
- `Services/EventService.cs`: in-memory mock data, registered as a singleton.
  State resets when the page is fully reloaded.

### Routing
| Route | Page |
|---|---|
| `/` | `Pages/Home.razor` |
| `/events` | `Pages/EventList.razor` (search and category filter) |
| `/events/{Id:int}` | `Pages/EventDetails.razor` |
| `/events/{Id:int}/register` | `Pages/Register.razor` (uses `Components/RegistrationForm.razor`) |
| `/my-registrations` | `Pages/MyRegistrations.razor` (sign in, session, cancel) |
| `/attendance` | `Pages/Attendance.razor` (overview) |
| `/attendance/{EventId:int}` | `Pages/EventAttendance.razor` (check-in) |
| anything else | `Pages/NotFound.razor` |

Routing best practices used:
- **Route constraints** (`{Id:int}`). `/events/abc` goes to the Not Found page instead of crashing.
- **Central URL helpers** in `AppRoutes.cs`, so links are never hand-written strings.
- **Relative hrefs** (no leading `/`), so the app also works under a sub-path such as GitHub Pages.
- `NavLink` with `Match.All` for Home and `Match.Prefix` for Events. Events stays highlighted on the details and register pages.
- Pages load data in `OnParametersSet`, so moving from `/events/1` to `/events/2` reloads correctly.
- An unknown id shows the shared Not Found page (see Activity 2).
- `NavigationManager.NavigateTo` is used for navigation from code after registering.

## Run
```bash
dotnet run
```

Run the tests (from `final-project`):
```bash
dotnet test EventEase.Tests
```

## Activity 2: Debugging and optimization

### Bugs found and fixed

**Data binding and validation**
| Bug | Fix |
|---|---|
| `@bind-Value:event="oninput"` on `InputText` crashed when saving (`ArgumentException`). The plain `<input>` used as a workaround skipped form validation entirely. | New `InputTextOnInput` component: updates live **and** validates each field as you type. |
| `[Required]` on a `DateTime` does nothing, so `01/01/0001` and past dates were accepted. | Custom `[UpcomingDate]` attribute: today up to 5 years ahead. A readable parse error replaces the generic one. |
| `"  Gala  "` was saved with the spaces; `"  ab  "` slipped past the length check. | `Normalize()` trims the text *before* validating, both in the card and in the service. |
| A `null` event crashed the whole app. | The card shows an "Event details are unavailable" placeholder. |
| Save stayed enabled after an unparseable date was typed. | The card re-renders whenever the form's validation state changes; Save is disabled while there are errors. |
| `EventService.Register` trusted its caller: **negative tickets added seats back**. `Update` stored invalid events. | The service validates with the same DataAnnotations rules (`ModelValidator`) and returns a `RegistrationResult` (not found / invalid / past / not enough seats). |
| `[EmailAddress]` accepted `a@b`. | An added regex also requires a domain with a dot. |
| Double-clicking Confirm could register twice; past events could still be registered for. | Submit guard plus a "Registration is closed" message. |

**Routing**
| Bug | Fix |
|---|---|
| Unknown ids (`/events/0`, `/events/99`) were handled separately in each page. | `NavigationManager.NotFound()` (.NET 10) shows the Router's `NotFoundPage` and keeps the URL. |
| The Not Found page kept showing the *first* bad URL, because the Router reuses the component. | The path is read on every render and refreshed on `LocationChanged`. |
| Any exception in a page showed the "An unhandled error has occurred" bar, and the user had to reload. | An `ErrorBoundary` in `MainLayout` shows a friendly message and recovers automatically on navigation. |
| A query like `?count=abc` could throw when parsed as `int?`, and a huge count could freeze the browser. | Read as a string with `int.TryParse`; capped at 20,000. |

**Performance (event list)**
| Bottleneck | Fix |
|---|---|
| All N cards were rendered into the DOM. | `<Virtualize>` renders only the visible rows (3 cards per row). |
| The filter was a computed property, so the whole LINQ query ran 3× per render. | Filtered once into a cached list, only when the search or category changes. |
| The list filtered and re-rendered on every keystroke. | 250 ms debounce. |
| Every card re-rendered whenever the list re-rendered (complex parameter). | `EventCard.ShouldRender` compares a snapshot of what the card shows. |
| `GetById` scanned the whole list, and `GetAll` re-sorted on every call. | `Dictionary` lookup (O(1)) and a cached sorted list. |

### Performance results
Release build, measured in the browser with `/events?count=N` (search times include the 250 ms debounce):

| | Before (5,000) | After (1,000) | After (5,000) | After (20,000) |
|---|---|---|---|---|
| Time until cards show | 5,447 ms | 416 ms | 899 ms | 3,044 ms* |
| DOM elements | 95,006 | 513 | 688 | 688 |
| Cards in DOM | 5,000 | 24 | 33 | 33 |
| Type a search | 594–1,484 ms | 361 ms | 381 ms | 595 ms |
| Clear the search | 2,724 ms | 296 ms | 293 ms | 320 ms |

\* Mostly the time to *generate* 20,000 mock objects in WebAssembly, not rendering.
The DOM size now stays flat no matter how many events there are.

### Tests added in Activity 2 (64 tests)
- `ModelValidationTests`: empty, whitespace, too-short and too-long values; default, past and far-future dates; bad emails; ticket bounds.
- `EventServiceTests`: negative tickets, overbooking, sold-out and past events, unknown ids, trimming, the 20,000-item cap, and lookup speed.
- `EventCardTests`: rendering, a null event, invalid input disabling Save, parse errors, the live preview, Cancel, a two-way binding round trip, and skipped re-renders.
- `RoutingTests`: route templates, URL helpers, not-found ids, invalid query values, the reused Not Found page, the debounced search, and the category filter.

## Activity 3: Registration, sessions, attendance and production readiness

### Registration Form (`Components/RegistrationForm.razor`)
- Fields: full name, email, optional phone, tickets, a **required** "accept terms" checkbox, and "keep me signed in".
- Each field shows its own error, and a summary of all errors appears after a failed submit (`role="alert"` for screen readers).
- Rules from the service are shown on the matching field: not enough seats → Tickets, already registered → Email.
  Other errors (event in the past or removed) show as a message on the form.
- Graceful failure: unexpected exceptions are logged and shown as "Something went wrong… try again".
  Double submits are ignored, and the form is disabled while saving.
- Trims input before validating. The same email can't register twice for the same event (checked case-insensitively).

### State management (`Services/UserSessionState.cs`)
- Holds the signed-in user and raises `Changed`. `SessionStatus`, `MyRegistrations`, `Register` and `EventDetails` all react to it.
- **Session continuity:**
  - "Keep me signed in" → `localStorage`, with a 7-day sliding expiry (each visit extends it).
  - Unticked → `sessionStorage`: survives a reload, cleared when the tab closes.
- Restored in `Program.cs` **before the first render**, so a reload never flashes a signed-out state.
- Expired sessions and corrupt data are cleared. If storage throws (private mode, quota), the app keeps working in memory.
- `IBrowserStorage` wraps the browser Storage API through `IJSRuntime` (no extra package), so the services are unit-testable.

### Attendance Tracker (`Services/AttendanceService.cs`, `/attendance`, `/attendance/{id}`)
- Stores registrations and their check-in and cancellation state in `localStorage`. On reload, seat counts are restored.
- Overview: totals and an attendance-rate bar for each event. Event page: stats, search, status filter, Check in / Undo, all virtualized.
- `My registrations`: the signed-in user's registrations, with a cancel step that asks for confirmation (gives the seats back).
- **Performance:** registrations are indexed per event, JSON is source-generated, and storage writes are **batched**
  (the screen updates right away, and quick clicks in a row share one write):

| 2,000 registrations | Before batching | After |
|---|---|---|
| Register all 2,000 | 4,115 ms | 72 ms |
| 500 check-ins | 1,441 ms | 4 ms |
| Summaries for 499 events | 3 ms | 3 ms |
| Reload from storage | 42 ms | 39 ms |

### Production readiness
| Change | Why |
|---|---|
| Removed the unused `HttpClient` registration and the `System.Net.Http`, `WebAssembly.Http` and `JSInterop` usings | Leftover template code that nothing uses. |
| Deleted 43 unused Bootstrap files (JS, RTL, grid/reboot/utilities, source maps) and `icon-192.png` | Only `bootstrap.min.css` is linked. `wwwroot/lib` went from 8.4 MB to 236 KB. |
| `InvariantGlobalization` | Drops the ICU culture data. The app uses one fixed date and number format. |
| Removed `coverlet.collector` from the tests | Coverage collection isn't used. |
| `TreatWarningsAsErrors` | Warnings can't pile up unnoticed. |
| Source-generated `System.Text.Json` (`EventEaseJsonContext`) | Faster, and safe when the build trims unused code. |
| The `?count=N` mock generator is enabled only in Development (`AppOptions`) | A test tool shouldn't be reachable in production. |
| Log levels by environment; `ErrorBoundary` logs caught exceptions | Diagnosable in production without noise. |
| Meta description and `<noscript>` message in `index.html` | Basic web hygiene. |

Publish output (`dotnet publish -c Release`):

| | Before | After |
|---|---|---|
| `_framework` download (Brotli) | 3,192 KB | **2,584 KB** (−19%) |
| ICU culture files | 9 | 0 |
| Files in `wwwroot` | 302 | 160 |
| `wwwroot` on disk | 29.0 MB | 13.8 MB |

### Tests: 126 in total, all passing
- `RegistrationFormTests`: empty, whitespace, bad email/phone, unchecked terms, unparseable tickets, too many tickets,
  duplicates, errors clearing on edit, prefill from the session, remember-me storage, broken storage, and an event that disappeared.
- `UserSessionStateTests`: restore after a reload, `sessionStorage` vs `localStorage`, expiry, sliding expiry,
  corrupt data, broken storage, and sign-out.
- `AttendanceServiceTests`: seats, duplicates, cancel, check-in/undo, summaries, persistence across a reload,
  damaged/orphan records, batched writes, and a 2,000-registration performance test.
- `PageTests`: live session status, the session restored on startup, state shared across pages,
  the full registration flow, My registrations (sign in, cancel), the attendance check-in, filters, and Not Found.

## Summary

EventEase is a Blazor WebAssembly app for browsing events, registering for them, and tracking attendance.
It was built in three steps:
1. **Foundation:** a reusable Event Card with two-way data binding, and routing between the event list, details, and registration pages.
2. **Debugging and optimization:** stricter validation, graceful handling of invalid routes, and a virtualized event list
   that stays fast with thousands of events.
3. **Features and production readiness:** a validated Registration Form, user sessions that survive a reload,
   an Attendance Tracker, and a smaller, cleaner production build. Everything is covered by 126 automated tests.

## How GitHub Copilot helped

- **Scaffolding:** Copilot generated the first version of the Event Card, the models, and the page routes.
  I then refined the binding syntax and moved all URLs into `AppRoutes` so links can't drift out of sync with the routes.
- **Finding bugs:** When saving an edit crashed, I asked Copilot to explain the `ArgumentException`. It pointed out
  that `@bind-Value:event="oninput"` doesn't work on `InputText`, which led to the `InputTextOnInput` component.
  Reviewing the code with Copilot also surfaced the negative-ticket bug, the `[Required]`-on-`DateTime` gap,
  and the Not Found page showing the wrong path.
- **Routing best practices:** Copilot suggested route constraints (`{Id:int}`), `NavLinkMatch`, `NavigationManager.NotFound()`,
  and an `ErrorBoundary`, and helped me check each route against its link.
- **Performance:** Copilot suggested `<Virtualize>`, caching the filter results, debouncing the search, and `ShouldRender`
  for the event list, and later batching storage writes for the Attendance Tracker. I measured each change before and after
  to confirm it actually helped.
- **New features:** Copilot drafted the session service, the attendance service and the Registration Form, including validation
  rules such as the email/phone patterns and the "must accept terms" check. I adjusted them to fit the app's existing services.
- **Testing:** Copilot generated xUnit/bUnit test cases for edge cases I might have missed (empty and whitespace input,
  corrupt storage data, expired sessions, duplicate registrations). The tests caught two real bugs that I then fixed.
- **Production cleanup:** Copilot helped identify unused dependencies (the `HttpClient` setup, the extra Bootstrap files, ICU culture data)
  and suggested source-generated JSON and treating warnings as errors.

**What I learned:** Copilot is fastest at producing a first draft and spotting likely problems, but its suggestions still need
to be verified. Building, testing and measuring every change is what turned its suggestions into working, reliable code.
