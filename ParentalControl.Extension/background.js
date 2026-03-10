// ParentGuard Web Filter — background service worker
// Communicates with the native host to get per-profile rules, then applies them
// via declarativeNetRequest dynamic rules.

const NATIVE_HOST = "com.parentalcontrol.host";

// ── Rule ID allocation ──────────────────────────────────────────────────────
// IDs 1–29000 are used for block-list entries.
// ID 30000 is used for the catch-all redirect rule in allow-list mode.
const REDIRECT_RULE_ID  = 30000;
const MAX_BLOCK_RULE_ID = 29000;

// ── Helpers ─────────────────────────────────────────────────────────────────

function blockedPageUrl() {
  return chrome.runtime.getURL("blocked.html");
}

/** Build a declarativeNetRequest rule that redirects main_frame to the blocked page. */
function makeRedirectRule(id, urlFilter) {
  return {
    id,
    priority: 1,
    action: {
      type: "redirect",
      redirect: { url: blockedPageUrl() }
    },
    condition: {
      urlFilter,
      resourceTypes: ["main_frame"]
    }
  };
}

/** Build a declarativeNetRequest rule that blocks sub-resources (images, scripts, etc.). */
function makeSubResourceBlockRule(id, urlFilter) {
  return {
    id: id + 15000, // offset to avoid collision with main_frame rules
    priority: 1,
    action: { type: "block" },
    condition: {
      urlFilter,
      resourceTypes: [
        "sub_frame", "script", "stylesheet", "image",
        "font", "xmlhttprequest", "other"
      ]
    }
  };
}

/**
 * Convert a domain pattern like "example.com" or "*.example.com"
 * into a declarativeNetRequest urlFilter string "||example.com^".
 */
function domainToFilter(domain) {
  const d = domain.replace(/^\*\./, "");
  return `||${d}^`;
}

// ── Rule sync ────────────────────────────────────────────────────────────────

async function syncRules() {
  let data;
  try {
    data = await chrome.runtime.sendNativeMessage(NATIVE_HOST, { type: "get_rules" });
  } catch (err) {
    console.warn("ParentGuard: native host unavailable —", err.message);
    return;
  }

  const { mode, domains } = data;
  const addRules = [];

  // Remove all existing dynamic rules first
  const existing = await chrome.declarativeNetRequest.getDynamicRules();
  const removeRuleIds = existing.map(r => r.id);

  if (mode === "block") {
    // Block each listed domain
    domains.slice(0, MAX_BLOCK_RULE_ID).forEach((domain, i) => {
      const filter = domainToFilter(domain);
      addRules.push(makeRedirectRule(i + 1, filter));
      addRules.push(makeSubResourceBlockRule(i + 1, filter));
    });
  } else {
    // Allow-list mode: block everything EXCEPT listed domains.
    // Add a catch-all block rule, then per-domain allow rules take higher priority.
    addRules.push({
      id: REDIRECT_RULE_ID,
      priority: 1,
      action: { type: "redirect", redirect: { url: blockedPageUrl() } },
      condition: { urlFilter: "||^", resourceTypes: ["main_frame"] }
    });

    // Allow rules for each whitelisted domain (higher priority overrides the catch-all)
    domains.slice(0, MAX_BLOCK_RULE_ID).forEach((domain, i) => {
      addRules.push({
        id: i + 1,
        priority: 2,
        action: { type: "allow" },
        condition: {
          urlFilter: domainToFilter(domain),
          resourceTypes: ["main_frame", "sub_frame", "script", "stylesheet",
                          "image", "font", "xmlhttprequest", "other"]
        }
      });
    });
  }

  await chrome.declarativeNetRequest.updateDynamicRules({ removeRuleIds, addRules });
  console.log(`ParentGuard: synced ${addRules.length} rules (mode=${mode})`);
}

// ── Event listeners ──────────────────────────────────────────────────────────

chrome.runtime.onInstalled.addListener(() => syncRules());

// Re-sync every 5 minutes to pick up rule changes made in the UI
chrome.alarms.create("syncRules", { periodInMinutes: 5 });
chrome.alarms.onAlarm.addListener(alarm => {
  if (alarm.name === "syncRules") syncRules();
});

// Also sync on browser startup
chrome.runtime.onStartup.addListener(() => syncRules());

// Allow the UI or NativeHost to trigger an immediate sync via chrome.runtime.sendMessage
chrome.runtime.onMessage.addListener((msg) => {
  if (msg?.type === "reload_rules") syncRules();
});

// Log blocked main_frame navigations back to the native host for the Activity Log
chrome.declarativeNetRequest.onRuleMatchedDebug?.addListener((info) => {
  if (info.request.type === "main_frame") {
    chrome.runtime.sendNativeMessage(NATIVE_HOST, {
      type: "blocked",
      url: info.request.url
    }).catch(() => {});
  }
});
