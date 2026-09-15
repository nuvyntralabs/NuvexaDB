/** Parse a CLI JSON object. Pretty-printed or compact. Arrays are ignored. */
export function parseCliObject(text: string): Record<string, unknown> | undefined {
  try {
    const parsed = JSON.parse(text) as unknown;
    if (parsed && typeof parsed === "object" && !Array.isArray(parsed)) {
      return parsed as Record<string, unknown>;
    }
  } catch {
    /* not JSON */
  }
  return undefined;
}

export function cliField(obj: Record<string, unknown>, name: string): unknown {
  const lower = name.toLowerCase();
  for (const [key, value] of Object.entries(obj)) {
    if (key.toLowerCase() === lower) {
      return value;
    }
  }
  return undefined;
}

/** `nuvexa info` / open without a key: `{ "encrypted": true, "error": "..." }`. */
export function isEncryptedLockResponse(text: string): boolean {
  const obj = parseCliObject(text);
  if (!obj) {
    return false;
  }

  const encrypted = cliField(obj, "encrypted") === true;
  const error = cliField(obj, "error");
  return encrypted && typeof error === "string" && error.length > 0;
}

export function cliErrorMessage(text: string): string {
  const obj = parseCliObject(text);
  const error = obj ? cliField(obj, "error") : undefined;
  if (typeof error === "string" && error) {
    return error;
  }
  return text.trim();
}
