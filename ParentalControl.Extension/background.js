// ParentGuard Web Filter — background service worker
// Uses a persistent connectNative port so the native host can push rule-change
// notifications instantly without waiting for the 5-minute alarm.

const NATIVE_HOST = "com.parentalcontrol.host";

// ── Rule IDs ─────────────────────────────────────────────────────────────────
// Using requestDomains arrays instead of one-rule-per-domain so we stay well
// under Chrome's 5,000 dynamic rule limit regardless of blocklist size.
const RULE_BLOCK_NAV         = 1;  // main_frame redirect for blocked domains
const RULE_BLOCK_SUBRESOURCE = 2;  // sub_frame/script/etc block for blocked domains
const RULE_ALLOW_CATCHALL    = 3;  // allow-mode: block every navigation (priority 1)
const RULE_ALLOW_DOMAINS     = 4;  // allow-mode: allow explicitly-allowed domains (priority 2)

// Tag domains are fetched in chunks to stay under the Chrome/Edge 1 MB native
// messaging message size limit.  174k adult-content domains ≈ 4 MB of JSON —
// exceeds the limit and causes Chrome to silently drop the port connection.
// 20k domains × ~28 bytes/entry ≈ 560 KB — safe margin below the 1 MB cap.
const TAG_DOMAIN_CHUNK = 20_000;

// ── Persistent native port ───────────────────────────────────────────────────

let _port          = null;
let _pendingResolve = null;
let _pendingReject  = null;
let _pendingTimer   = null;

function ensurePort() {
  if (_port) return _port;

  _port = chrome.runtime.connectNative(NATIVE_HOST);

  _port.onMessage.addListener(msg => {
    // Push notification from the native host: rules changed in the UI
    if (msg?.type === "reload") {
      syncRules();
      return;
    }
    // Response to a pending request
    if (_pendingResolve) {
      clearTimeout(_pendingTimer);
      const resolve = _pendingResolve;
      _pendingResolve = null;
      _pendingReject  = null;
      _pendingTimer   = null;
      resolve(msg);
    }
  });

  _port.onDisconnect.addListener(() => {
    _port = null;
    if (_pendingReject) {
      clearTimeout(_pendingTimer);
      const reject = _pendingReject;
      _pendingResolve = null;
      _pendingReject  = null;
      _pendingTimer   = null;
      reject(new Error(chrome.runtime.lastError?.message || "port disconnected"));
    }
    // Reconnect after a short delay so enforcement resumes if the host restarts
    setTimeout(ensurePort, 10_000);
  });

  return _port;
}

async function sendNativeRequest(message) {
  return new Promise((resolve, reject) => {
    const port = ensurePort();
    _pendingResolve = resolve;
    _pendingReject  = reject;
    _pendingTimer   = setTimeout(() => {
      if (_pendingReject) {
        const rej = _pendingReject;
        _pendingResolve = null;
        _pendingReject  = null;
        _pendingTimer   = null;
        rej(new Error("native host timeout"));
      }
    }, 30_000); // 30 s — large tag fetches can take a moment
    port.postMessage(message);
  });
}

// ── Helpers ──────────────────────────────────────────────────────────────────

function blockedPageUrl() {
  return chrome.runtime.getURL("blocked.html");
}

// ── Rule sync ────────────────────────────────────────────────────────────────

async function syncRules() {
  // 1. Fetch manual rules and tag domain count (small message, always under 1 MB).
  let data;
  try {
    data = await sendNativeRequest({ type: "get_rules" });
  } catch (err) {
    console.warn("ParentGuard: native host unavailable —", err.message);
    return;
  }

  const { allowMode, blocked: manualBlocked = [], allowed = [], tagBlockedCount = 0, theme } = data;
  if (theme) chrome.storage.local.set({ theme });

  // 2. Fetch tag domains in chunks to stay under the 1 MB native messaging limit.
  //    Each chunk is ≤40,000 domains (~920 KB JSON), safely under the cap.
  let tagBlocked = [];
  if (tagBlockedCount > 0) {
    let offset = 0;
    while (offset < tagBlockedCount) {
      let chunk;
      try {
        chunk = await sendNativeRequest({ type: "get_tag_domains", offset, limit: TAG_DOMAIN_CHUNK });
      } catch (err) {
        console.warn("ParentGuard: failed fetching tag domains at offset", offset, "—", err.message);
        break;
      }
      if (!chunk.domains || chunk.domains.length === 0) break;
      tagBlocked = tagBlocked.concat(chunk.domains);
      offset += chunk.domains.length;
      if (offset >= (chunk.total ?? tagBlockedCount)) break;
    }
  }

  // 3. Merge manual + tag domains (deduplicated).
  const blocked = manualBlocked.length > 0 || tagBlocked.length > 0
    ? [...new Set([...manualBlocked, ...tagBlocked])]
    : [];

  const addRules = [];

  // Remove all existing dynamic rules first
  const existing = await chrome.declarativeNetRequest.getDynamicRules();
  const removeRuleIds = existing.map(r => r.id);

  // Blocked domains — single rule per resource type using requestDomains array.
  // requestDomains matches the exact domain AND all its subdomains automatically.
  if (blocked.length > 0) {
    const blockPriority = allowMode ? 3 : 1;

    addRules.push({
      id: RULE_BLOCK_NAV,
      priority: blockPriority,
      action: {
        type: "redirect",
        redirect: { url: blockedPageUrl() }
      },
      condition: {
        requestDomains: blocked,
        resourceTypes: ["main_frame"]
      }
    });

    addRules.push({
      id: RULE_BLOCK_SUBRESOURCE,
      priority: blockPriority,
      action: { type: "block" },
      condition: {
        requestDomains: blocked,
        resourceTypes: [
          "sub_frame", "script", "stylesheet", "image",
          "font", "xmlhttprequest", "other"
        ]
      }
    });
  }

  if (allowMode) {
    addRules.push({
      id: RULE_ALLOW_CATCHALL,
      priority: 1,
      action: { type: "redirect", redirect: { url: blockedPageUrl() } },
      condition: { urlFilter: "*", resourceTypes: ["main_frame"] }
    });

    if (allowed.length > 0) {
      addRules.push({
        id: RULE_ALLOW_DOMAINS,
        priority: 2,
        action: { type: "allow" },
        condition: {
          requestDomains: allowed,
          resourceTypes: [
            "main_frame", "sub_frame", "script", "stylesheet",
            "image", "font", "xmlhttprequest", "other"
          ]
        }
      });
    }
  }

  // 4. Apply rules — wrapped in try/catch so failures are visible in the console.
  try {
    await chrome.declarativeNetRequest.updateDynamicRules({ removeRuleIds, addRules });
    console.log(`ParentGuard: synced ${addRules.length} rules (${blocked.length} blocked [${manualBlocked.length} manual + ${tagBlocked.length} tag], ${allowed.length} allowed, allowMode=${allowMode})`);
  } catch (err) {
    console.error("ParentGuard: updateDynamicRules failed —", err.message, err);
  }
}

// ── Event listeners ──────────────────────────────────────────────────────────

// Open the persistent port immediately on service worker start
ensurePort();

chrome.runtime.onInstalled.addListener(() => syncRules());

// Re-sync every 5 minutes as a fallback (in case the port disconnects)
chrome.alarms.create("syncRules", { periodInMinutes: 5 });
chrome.alarms.onAlarm.addListener(alarm => {
  if (alarm.name === "syncRules") syncRules();
});

// Also sync on browser startup
chrome.runtime.onStartup.addListener(() => syncRules());

// Allow other extension pages to trigger an immediate sync
chrome.runtime.onMessage.addListener((msg) => {
  if (msg?.type === "reload_rules") syncRules();
});

// Log blocked main_frame navigations back to the native host for the Activity Log
chrome.declarativeNetRequest.onRuleMatchedDebug?.addListener((info) => {
  if (info.request.type === "main_frame") {
    ensurePort().postMessage({ type: "blocked", url: info.request.url });
  }
});
