"use strict";

function parseTziakchaSessionId(input) {
  const trimmed = String(input || "").trim();
  if (!trimmed) {
    return null;
  }

  if (!trimmed.includes("?") && !trimmed.includes("/")) {
    return trimmed;
  }

  try {
    const url = new URL(trimmed, "https://tziakcha.net");
    return url.searchParams.get("id") || trimmed;
  } catch {
    return trimmed;
  }
}

module.exports = {
  parseTziakchaSessionId
};
