# ParentalControl — End-to-End Test Plan & Checklist

## Context
The app is feature-complete. This document is a structured QA checklist covering every major
subsystem: install, profiles, screen time, app control, focus mode, web filter, dashboard,
settings, activity log, the browser extension, and the watchdog service.

Work through sections in order — each section builds on the previous (service up, DB clean, etc.).

---

## Pre-Test Setup
- [ ] Run `install.ps1` as Administrator from a clean state (existing DB deleted by installer)
- [ ] Confirm services running: `Get-Service ParentalControlService, ParentalControlWatchdog`
- [ ] Open UI (`ParentalControl.UI.exe`) — login prompt should appear
- [ ] Login with default password `parent1234`
- [ ] Confirm first-run flow completes without error

**Browser Extension (auto-installed — no manual steps required):**
- [ ] Open Edge — ParentGuard Web Filter extension appears automatically in `edge://extensions`
- [ ] Extension shows as enabled with no errors or warnings
- [ ] Open Chrome (if installed) — same extension appears automatically
- [ ] Extension ID in `edge://extensions` matches `lackpoggaaeodfcagkfcglokeilcfokg`
- [ ] Native messaging host connected: navigate to a blocked domain → redirected to `blocked.html` (not a browser error)

---

## 1. Authentication
- [ ] Default password `parent1234` accepted
- [ ] Wrong password shows error, does not proceed
- [ ] Change password: Settings → min 6 chars enforced, wrong current password rejected, success saves
- [ ] New password works on next login; old password rejected

---

## 2. Profiles Page
- [ ] Default profile (catch-all, WindowsUsername = "") appears automatically
- [ ] Add a new profile: enter DisplayName + Windows username → 7 ScreenTime + 7 FocusSchedule rows auto-created
- [ ] Duplicate Windows username prevented (or handled gracefully)
- [ ] AlwaysRelock toggle saves and persists
- [ ] IsEnabled toggle saves and persists
- [ ] Save sends ReloadRules IPC to service (no service restart needed)
- [ ] Delete profile removes associated rules, schedules, web filter tags

---

## 3. Screen Time Page
**Scheduling:**
- [ ] All 7 days editable per profile
- [ ] Daily Limit 0 = unlimited (no enforcement)
- [ ] AllowedFrom / AllowedUntil: valid 24h and 12h AM/PM formats accepted
- [ ] Invalid time input shows red error with accepted formats listed
- [ ] Logical ordering not enforced (AllowedFrom after AllowedUntil) — document as known limit
- [ ] Day Range selector: All/Weekdays/Weekends/Single Day correctly filters displayed rows

**Save operations:**
- [ ] Save: saves only current profile's schedule
- [ ] Save to All: saves to every profile

**Enforcement (requires active Windows user matching profile):**
- [ ] Screen time counter increments in service (check TodayUsedMinutes in DB after 5+ minutes)
- [ ] Lock fires at limit: session locked, ActivityType.ScreenTimeLocked logged
- [ ] Lock fires outside AllowedFrom–AllowedUntil window
- [ ] IsScreenTimeLocked persists across service restart (no double-lock same day)
- [ ] AlwaysRelock: each new login triggers re-lock, not just first daily lock

---

## 4. App Control Page
**App rules grid:**
- [ ] Quick Scan (Steam VDF) finds installed Steam games
- [ ] Deep Scan (drive recursive) finds non-Steam executables
- [ ] Access Mode: Block, ScreenTimeOnly, Allow all save and persist per profile
- [ ] AllowedInFocusMode toggle saves and persists
- [ ] ESRB Rating / Genres / Tags populated from RAWG API (requires API key in Settings)
- [ ] IsManuallyModified=1 after user edits; rescan does not overwrite manual changes

**App Time Limits:**
- [ ] 7-day schedule with per-day limits saves correctly
- [ ] Limit 0 = unlimited
- [ ] TodayAppTimeUsedMinutes increments only for ScreenTimeOnly apps

**Scan autoconfiguration cascade:**
- [ ] Setting Block rating to M: games rated M+ get AccessMode=Block
- [ ] Setting App Time rating to T: games T/M/AO (but not AO blocked by Block rule) get ScreenTimeOnly
- [ ] Block genres disable same genres in App Time and Focus Mode selectors
- [ ] App Time genres disable same genres in Focus Mode selector (no double-apply)
- [ ] Unrated default and Rated default correctly applied to unmatched games

**Enforcement:**
- [ ] AccessMode=Block: process killed within 5 seconds of launch; ActivityType.AppBlocked logged
- [ ] AccessMode=ScreenTimeOnly: app runs inside window, killed at limit or outside window
- [ ] App time limit reached: app killed, TodayAppTimeBonusMinutes check (bonus extends it)

---

## 5. Focus Mode Page
- [ ] Per-day schedule saves and persists (FocusFrom / FocusUntil)
- [ ] Invalid times handled same as Screen Time page
- [ ] Day Range selector works same as Screen Time
- [ ] FocusModeEnabled toggle (per profile) respected
- [ ] Save / Save to All work correctly

**Enforcement:**
- [ ] During active focus window: apps with AllowedInFocusMode=false killed within 5s
- [ ] Apps with AllowedInFocusMode=true continue running unaffected
- [ ] Focus ends at FocusUntil: previously-blocked apps can run again

---

## 6. Web Filter Page
**Manual domain rules:**
- [ ] Add domain (Block): paste "https://example.com/" → stripped to "example.com", saved
- [ ] Add domain (Allow): saved as allow rule
- [ ] Duplicate domain in same profile: prevented / error shown
- [ ] Remove one or multiple selected rows
- [ ] Save to This Profile / Save to All Profiles work correctly

**Allow Mode:**
- [ ] Toggle Allow Mode on: catch-all block rule active, only allowed list accessible
- [ ] Toggle Allow Mode off: only blocked list enforced

**Content Tags:**
- [ ] Adult Content / Gambling tags visible with domain counts
- [ ] Enable tag for profile → domains flow to extension on sync
- [ ] Sync All: downloads/refreshes all source-backed tags; progress shown per tag
- [ ] Sync error for one tag shows failure message; other tags continue
- [ ] KickStartupSync: domain download begins in background when UI opens (no need to navigate to page)
- [ ] View Domains button: opens viewer window showing all domains for a tag
- [ ] Copy Tags to All Profiles: all profiles receive same tag associations

**Extension enforcement (Edge/Chrome):**
- [ ] blocked.html served for blocked navigations (not a browser error page)
- [ ] blocked.html shows correct hostname of blocked site
- [ ] blocked.html applies current app theme colors (Raven, Deep Ocean, etc.)
- [ ] Sub-domain matching: blocking "example.com" also blocks "sub.example.com"
- [ ] Allow Mode in extension: allows only explicitly-listed domains
- [ ] Rule reload within ~1 second of saving rules in UI

---

## 7. Dashboard Page
**Display:**
- [ ] Active profile card shows correct Windows username match
- [ ] Screen Time remaining countdown is accurate and color-coded (green/yellow/red/purple)
- [ ] App Time remaining shown correctly
- [ ] Focus Mode status reflects current window (Active / Inactive / starting in N min)
- [ ] Web Filter shows sites blocked today count
- [ ] Recent Activity (last 50 entries) renders correctly
- [ ] All stats refresh every 30 seconds without user action

**Enforcement toggles:**
- [ ] Admin Mode off: all four toggles (ScreenTime / AppControl / FocusMode / WebFilter) active
- [ ] Admin Mode on: all four disabled simultaneously; IPC ReloadRules sent
- [ ] Individual toggles save their state and take effect within one service tick (5s)

**Time adjustments:**
- [ ] Screen Time Today +30: adds 30 bonus minutes; remaining updates on next refresh
- [ ] Screen Time Today -30: reduces bonus minutes (not below 0)
- [ ] Allowed Time Range +60: extends AllowedUntil by 60 minutes
- [ ] App Time Today +30: adds AppTimeBonusMinutes

**12h/24h display:**
- [ ] Settings → 12h format → Dashboard shows "8:00 PM" style
- [ ] Settings → 24h format → Dashboard shows "20:00" style

---

## 8. Activity Log Page
- [ ] Entries appear for: ServiceStarted, ScreenTimeLocked, AppBlocked, WebsiteBlocked
- [ ] Date filter: selecting a past range shows only entries in that range
- [ ] Type filter: selecting "AppBlocked" shows only app block entries
- [ ] Search box: entering text filters by detail string (case-insensitive)
- [ ] Clear Log: deletes all entries (confirmation required)
- [ ] Auto-refresh every 10 seconds

---

## 9. Settings Page
**Appearance:**
- [ ] 12h/24h toggle saves and updates all time displays immediately
- [ ] Theme dropdown: selecting Raven applies near-black + blue palette
- [ ] Theme dropdown: selecting Deep Ocean applies dark teal palette
- [ ] Dark Mode toggle changes palette within selected theme
- [ ] Custom theme: hex inputs for Primary/Secondary/Tertiary update preview live; Save applies
- [ ] UI Scale: 1080p/1440p/4K rescales window correctly

**Notifications:**
- [ ] Email mode: SMTP settings save, Test button sends email
- [ ] SMS mode: phone + carrier gateway save, Test button sends SMS
- [ ] Ntfy mode: topic saves, Test button sends push
- [ ] Notify on Screen Lock: notification fires when session locked
- [ ] Notify on App Block: notification fires when app killed

**RAWG API Key:**
- [ ] Masked by default (dots)
- [ ] Edit → unmasks → save re-masks
- [ ] Invalid/empty key: RAWG fetch fails gracefully in scan

**Reset tools:**
- [ ] Reset Screen Time Today: clears TodayUsedMinutes, bonus, IsScreenTimeLocked without touching limits
- [ ] Reset App Time Today: clears TodayAppTimeUsedMinutes, bonus
- [ ] Reset Settings: restores defaults, keeps password and rules
- [ ] Reset Data: deletes AppRules, ActivityEntries, disables ScreenTimeLimits
- [ ] Full Reset: double-confirmation, applies both above resets

---

## 10. Service & Watchdog Resilience
- [ ] Kill `ParentalControlService` manually → Watchdog restarts it within 5 seconds
- [ ] Kill `ParentalControlWatchdog` manually → Service detects and restarts it within 5 minutes
- [ ] Service restart: IsScreenTimeLocked persists (no double-lock same day)
- [ ] Service restart: all cached rules reloaded (no stale state)
- [ ] IPC named pipe reconnects after service restart (UI shows "Running" again)

---

## 11. Browser Extension (Edge)
- [ ] Extension icon visible in toolbar
- [ ] Navigating to a blocked domain redirects to `blocked.html` instantly
- [ ] `blocked.html` shows domain name and "Go Back" button
- [ ] "Go Back" button returns to previous page
- [ ] Theme colors on `blocked.html` match current app theme (verify by changing theme in UI, reloading)
- [ ] After saving new rule in UI: extension picks up change within ~1–2 seconds (version file poll)
- [ ] Tag domains (Adult Content enabled): adult site redirects to blocked page
- [ ] Subresource blocking: scripts/images from blocked domains are blocked too
- [ ] Allow Mode: non-listed site redirects to blocked page; listed site loads normally

---

## 12. Clean Install (Fresh User Scenario)
- [ ] Delete `%ProgramData%\ParentalControl\data.db` (installer does this automatically)
- [ ] Re-run `install.ps1` as Administrator
- [ ] Login with `parent1234`
- [ ] No "Default Profile" leftover from old code
- [ ] Web Filter: Adult Content and Gambling tags present with correct SourceUrl
- [ ] Opening Web Filter page triggers background sync → domain counts populate within 30s
- [ ] Navigate to blocked adult site → redirected to blocked page

---

## 13. Multi-Profile Scenarios
- [ ] Create two profiles (child1, child2) with different Windows usernames
- [ ] Set different screen time limits per profile
- [ ] Log in as child1 Windows account → child1 limits enforced
- [ ] Log in as child2 Windows account → child2 limits enforced
- [ ] User with no matching profile → Default profile rules apply
- [ ] Web filter: each profile can have different blocked domains and tags enabled

---

## Known Limits (Document, Don't Fix)
- AllowedFrom > AllowedUntil not validated in UI (spans midnight not supported)
- ProcessMonitor only monitors processes launched after service start
- Activity log limited to 500 entries per page load
- Native messaging messages capped at 1 MB (tag chunking handles this automatically)
- No support for multiple simultaneous logged-in Windows users enforced separately
