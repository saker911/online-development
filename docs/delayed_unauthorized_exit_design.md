# Delayed Unauthorized Exit Design

## What Changed

- Unauthorized employee exit attempts for permits of type `دخول فقط` no longer become a final violation immediately.
- The first blocked attempt is stored as `PendingUnauthorizedExit` with a sequence id.
- Each permit activity now stores gate operator audit data:
  - operator display name
  - operator account
  - gate name
  - IP or device when available
  - execution method
  - classification status

## Active Classification Flow

1. `PendingUnauthorizedExit`
   - created when an employee tries to leave during work hours without an active leave window or temporary exit permission.
   - gate decision remains immediate: exit is denied.

2. `DeniedAttemptClosed`
   - created when the same pending attempt is resolved without a final violation.
   - current implementation closes it automatically when:
     - the employee later exits through an authorized flow the same day, or
     - the employee remains until work-end closure.

3. `UnauthorizedExitConfirmed`
   - created when a later return is recorded while the permit still carries a pending unauthorized-exit sequence and the movement history proves the employee was actually outside.

4. `UnauthorizedExitNeedsReview`
   - created during the next synchronization cycle if the day ended and the pending event was not resolved automatically.
   - this increments the escalation counter and notifies the manager.

5. `UnauthorizedExitStopped`
   - created after repeated unresolved or confirmed violations according to the existing escalation threshold.

## What Stayed the Same

- Gate blocking remains immediate.
- Existing permit, leave-request, notification, and work-end closure services remain in place.
- `CurrentState` still exists for fast display and compatibility, but the unauthorized-exit path now depends on event sequencing instead of a single instantaneous state.

## Current Scope

- Implemented now:
  - pending unauthorized-exit event lifecycle
  - end-of-day automatic closure without violation
  - next-day escalation to administrative review
  - full gate-operator audit metadata on permit activities
  - report and detail-screen exposure for the new fields
   - administrative review queue inside the movement report
   - grouped timeline view by `SequenceId`
   - explicit administrative resolution actions:
      - `AdministrativeReviewDismissed`
      - `AdministrativeReviewConfirmed`
   - dedicated user permission for administrative review decisions, separated from stop/reactivate authority
      - live browser validation for both cases:
         - positive path: a user with the review permission can close a pending review successfully
         - negative path: a user without the review permission cannot see review tools and is blocked server-side from the review action

- Planned next:
  - richer analytics by gate, operator, and manual override reason
   - printable grouped sequence report for investigations

   ## Phase Status

   - Phase 1: completed
      - delayed classification model, pending sequence handling, end-of-day closure, and review escalation are live.

   - Phase 2: completed
      - administrative review queue, grouped sequence timeline, explicit review decisions, dedicated review permission, and live browser validation are complete.

## Development Mechanism

1. Backend first
   - stabilize the event model and audit fields.
   - keep `IPermitService` as the facade so controllers and views remain thin.

2. Reporting second
   - surface sequence id, classification status, and operator audit in the activity report.

3. Review workflow third
   - expose the unresolved review queue and close/confirm decisions from the same report context.

4. Policy tuning last
   - once enough real usage data exists, tune escalation thresholds and end-of-day review rules.