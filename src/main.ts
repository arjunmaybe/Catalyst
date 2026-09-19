import { invoke, Channel } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { getCurrentWebview } from "@tauri-apps/api/webview";
import { getCurrentWindow } from "@tauri-apps/api/window";

// ---------------------------------------------------------------------------
// Format model (mirrors the spec; WPF prototype is the behavior reference).
// ---------------------------------------------------------------------------

const IMAGE_TARGETS = [".jpg", ".png", ".bmp", ".gif"];
const VIDEO_TARGETS = [".mp4", ".mkv", ".mov", ".webm"];
const AUDIO_TARGETS = [".mp3", ".wav", ".m4a", ".flac"];

const IMAGE_READABLE = new Set([".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff"]);
const VIDEO_READABLE = new Set([".mp4", ".mkv", ".mov", ".avi", ".webm"]);
const AUDIO_READABLE = new Set([".mp3", ".wav", ".m4a", ".flac", ".ogg"]);

type Category = "image" | "video" | "audio";

function extOf(path: string): string {
  const base = path.split(/[\\/]/).pop() ?? path;
  const dot = base.lastIndexOf(".");
  return dot < 0 ? "" : base.slice(dot).toLowerCase();
}

function normalizeExt(ext: string): string {
  return ext === ".jpeg" ? ".jpg" : ext;
}

function categoryOf(ext: string): Category | null {
  if (IMAGE_READABLE.has(ext)) return "image";
  if (VIDEO_READABLE.has(ext)) return "video";
  if (AUDIO_READABLE.has(ext)) return "audio";
  return null;
}

function targetsFor(category: Category, excludeNormalized: string): string[] {
  const all =
    category === "video" ? VIDEO_TARGETS : category === "audio" ? AUDIO_TARGETS : IMAGE_TARGETS;
  return all.filter((t) => t !== excludeNormalized);
}

/// Valid ring targets derived from the first dropped file. Null when there
/// is nothing to offer (empty or unsupported drop).
function candidatesForFirstFile(paths: string[]): string[] | null {
  if (paths.length === 0) return null;
  const firstExt = extOf(paths[0]);
  const cat = categoryOf(firstExt);
  if (cat === null) return null;
  return targetsFor(cat, normalizeExt(firstExt));
}

// ---------------------------------------------------------------------------
// Widget view: nucleus + up to 4 ring nodes on a 260x260 stage.
// Slot centers match the WPF prototype geometry.
// ---------------------------------------------------------------------------

const SLOT_CENTERS: Array<{ x: number; y: number }> = [
  { x: 130, y: 40 }, // top
  { x: 220, y: 130 }, // right
  { x: 130, y: 220 }, // bottom
  { x: 40, y: 130 }, // left
];
// Beam pointer angles (deg, 0 = top, clockwise) per SLOT_CENTERS index.
// Slots are axis-aligned at radius 90, so these are exact — keep in sync
// with SLOT_CENTERS above.
const SLOT_ANGLES = [0, 90, 180, 270];
const NODE_SIZE = 44;
const RESET_DELAY_MS = 2000;
const ERROR_RESET_DELAY_MS = 6000;
// Max chars of a backend error shown inside the 92px nucleus. The full
// message is kept on `nucleus.title` (hover tooltip) + console.
const ERROR_SHORT_MAX = 40;

const nucleus = document.getElementById("nucleus") as HTMLDivElement;
const statusEl = document.getElementById("status") as HTMLSpanElement;
const nucleusIcon = document.getElementById("nucleus-icon") as HTMLSpanElement;
const guideRing = document.getElementById("guide-ring") as HTMLDivElement;
const guideBeam = document.getElementById("guide-beam") as HTMLDivElement;
const nodeEls: HTMLDivElement[] = [0, 1, 2, 3].map(
  (i) => document.getElementById(`node-${i}`) as HTMLDivElement,
);

let visibleTargets: string[] = [];
let highlightedIndex = -1;
let isConverting = false;
let resetTimer: number | undefined;
// Ring fade generation: guards the delayed `hidden = true` so a quick
// re-enter can't be hidden by a stale fade-out timeout.
let ringGeneration = 0;
let hideTimer: number | undefined;
const RING_FADE_MS = 180;

const ICONS: Record<string, string> = {
  image:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="3" y="3" width="18" height="18" rx="2"/><circle cx="9" cy="9" r="2"/><path d="m21 15-5-5L5 21"/></svg>',
  video:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="2" y="4" width="20" height="16" rx="2"/><path d="m10 9 5 3-5 3z"/></svg>',
  audio:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M9 18V5l12-2v13"/><circle cx="6" cy="18" r="3"/><circle cx="18" cy="16" r="3"/></svg>',
  check:
    '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M20 6 9 17l-5-5"/></svg>',
};

function categoryForFirstFile(paths: string[]): Category | null {
  if (paths.length === 0) return null;
  return categoryOf(extOf(paths[0]));
}

function setNucleusIcon(name: string | null): void {
  if (name === null || !(name in ICONS)) {
    nucleusIcon.innerHTML = "";
    nucleusIcon.hidden = true;
    return;
  }
  nucleusIcon.innerHTML = ICONS[name];
  nucleusIcon.hidden = false;
}

// Continuous beam angle (deg, unwrapped): normalized to the shortest path
// on every retarget so e.g. left (270°) → top (0°) sweeps forward 90°
// instead of spinning back 270°.
let beamAngleDeg = 0;

function setBeamTarget(index: number): void {
  if (index < 0) {
    guideBeam.classList.remove("beam-on");
    return;
  }
  let target = SLOT_ANGLES[index];
  target += 360 * Math.round((beamAngleDeg - target) / 360);
  beamAngleDeg = target;
  guideBeam.style.setProperty("--beam-angle", `${target}deg`);
  guideBeam.classList.add("beam-on");
}

function clearNucleusVisuals(): void {
  nucleus.classList.remove("active", "deciding", "dragover", "success");
  setNucleusIcon(null);
}

function showSuccess(text: string): void {
  setStatus(text);
  nucleus.classList.remove("deciding", "dragover", "error");
  nucleus.classList.add("success");
  setNucleusIcon("check");
}

for (let i = 0; i < nodeEls.length; i++) {
  const slot = SLOT_CENTERS[i];
  nodeEls[i].style.left = `${slot.x - NODE_SIZE / 2}px`;
  nodeEls[i].style.top = `${slot.y - NODE_SIZE / 2}px`;
}

function setStatus(text: string, tooltip?: string): void {
  statusEl.textContent = text;
  // #status has pointer-events:none so the hover tooltip must live on the
  // nucleus itself. Empty tooltip clears any previous error message.
  nucleus.title = tooltip ?? "";
  const isError = tooltip !== undefined && tooltip !== "";
  statusEl.classList.toggle("error", isError);
  nucleus.classList.toggle("error", isError);
}

// Extract a readable message from a Tauri `invoke` rejection. Backend
// commands return `Result<_, String>`, so this is usually a plain string,
// but handle Error objects / `{ message }` shapes defensively.
function backendMessage(err: unknown): string {
  if (typeof err === "string" && err.trim() !== "") return err;
  if (err instanceof Error && err.message) return err.message;
  if (typeof err === "object" && err !== null && "message" in err) {
    const m = (err as { message: unknown }).message;
    if (typeof m === "string" && m.trim() !== "") return m;
  }
  try {
    const s = String(err);
    return s.trim() !== "" ? s : "conversion failed";
  } catch {
    return "conversion failed";
  }
}

// Collapse whitespace and truncate for the tiny nucleus label. Returns the
// full readable message when short enough, otherwise `"<head>…"` so the
// widget status stays legible without devtools.
function shortError(message: string, max: number = ERROR_SHORT_MAX): string {
  const flat = message.trim().replace(/\s+/g, " ");
  if (flat.length <= max) return flat;
  return `${flat.slice(0, max).trimEnd()}…`;
}

// Map raw backend failures to a clear widget label. Image-decode failures
// (e.g. a progressive-JPEG variant the `image` crate can't parse) surface
// raw decoder text like "Format error decoding Jpeg: ... Illegal start
// bytes" — unreadable in the 92px nucleus. Show a friendly label instead;
// the full technical message stays in the hover tooltip + console.
function friendlyDisplayError(fullMessage: string, file: string): string {
  const lower = fullMessage.toLowerCase();
  const isDecodeFailure =
    lower.includes("open failed") ||
    lower.includes("format error") ||
    lower.includes("decoding") ||
    lower.includes("parsing image") ||
    lower.includes("illegal start") ||
    lower.includes("invalid jpeg") ||
    lower.includes("unsupported image");
  if (isDecodeFailure) {
    const ext = extOf(file);
    if (ext === ".jpg" || ext === ".jpeg") return "unsupported JPEG variant";
    return "couldn't read this image format";
  }
  return shortError(fullMessage);
}

function scheduleReset(delay: number = RESET_DELAY_MS): void {
  window.clearTimeout(resetTimer);
  resetTimer = window.setTimeout(() => {
    setStatus("drop file");
    clearNucleusVisuals();
  }, delay);
}

function showTargets(candidates: string[], category: Category | null = null): void {
  // Cancel any pending fade-out so a quick re-enter never gets hidden.
  ringGeneration += 1;
  window.clearTimeout(hideTimer);
  visibleTargets = candidates;
  highlightedIndex = -1;
  for (let i = 0; i < nodeEls.length; i++) {
    if (i < candidates.length) {
      const label = nodeEls[i].querySelector(".node-label") as HTMLSpanElement;
      label.textContent = candidates[i].slice(1).toUpperCase();
      nodeEls[i].hidden = false;
      nodeEls[i].classList.remove("highlight");
      // Force layout so the .visible add below triggers the fade/scale
      // transition instead of applying instantly.
      void nodeEls[i].offsetWidth;
      nodeEls[i].classList.add("visible");
    } else {
      nodeEls[i].hidden = true;
      nodeEls[i].classList.remove("highlight", "visible");
    }
  }
  nucleus.classList.add("active", "deciding", "dragover");
  nucleus.classList.remove("success");
  setNucleusIcon(category);
  // Guide track + sweep: unhide, force layout so the fade runs, then show.
  // Beam stays off until the first highlight (highlightedIndex is -1 here);
  // snap its angle back to top while invisible.
  guideRing.hidden = false;
  void guideRing.offsetWidth;
  guideRing.classList.add("visible");
  guideBeam.classList.remove("beam-on");
  beamAngleDeg = 0;
  guideBeam.style.setProperty("--beam-angle", "0deg");
}

function hideNodes(): void {
  visibleTargets = [];
  highlightedIndex = -1;
  const generation = ringGeneration + 1;
  ringGeneration = generation;
  for (const el of nodeEls) {
    // Fade out first; `hidden` is applied after the transition so the
    // ring disappears smoothly instead of snapping away.
    el.classList.remove("visible", "highlight");
  }
  // Same fade treatment for the guide track/sweep, under the same
  // generation guard so a fast re-enter can't strand it hidden.
  guideRing.classList.remove("visible");
  guideBeam.classList.remove("beam-on");
  nucleus.classList.remove("deciding", "dragover");
  setNucleusIcon(null);
  window.clearTimeout(hideTimer);
  hideTimer = window.setTimeout(() => {
    if (ringGeneration !== generation) return;
    guideRing.hidden = true;
    for (const el of nodeEls) {
      el.hidden = true;
      el.classList.remove("highlight");
      // Clear stale labels too, so a wrongly-visible node can never show an
      // outdated format (or an empty circle for an unused slot).
      const label = el.querySelector(".node-label");
      if (label) label.textContent = "";
    }
  }, RING_FADE_MS);
}

function highlightNearest(cursor: { x: number; y: number }): void {
  let nearest = -1;
  let best = Number.MAX_VALUE;
  for (let i = 0; i < visibleTargets.length; i++) {
    const dx = cursor.x - SLOT_CENTERS[i].x;
    const dy = cursor.y - SLOT_CENTERS[i].y;
    const d2 = dx * dx + dy * dy;
    if (d2 < best) {
      best = d2;
      nearest = i;
    }
  }
  if (nearest === highlightedIndex) return;
  if (highlightedIndex >= 0) nodeEls[highlightedIndex].classList.remove("highlight");
  if (nearest >= 0) nodeEls[nearest].classList.add("highlight");
  highlightedIndex = nearest;
  setBeamTarget(nearest);
}

// ---------------------------------------------------------------------------
// Conversion.
// ---------------------------------------------------------------------------

async function convertOne(
  path: string,
  category: Category,
  targetExt: string,
  labelPrefix: string,
): Promise<string> {
  if (category === "image") {
    setStatus(`${labelPrefix}...`);
    // Tauri v2 maps camelCase JS keys -> snake_case Rust params, so
    // `targetExt` here binds to `target_ext` in `convert_image`.
    return await invoke<string>("convert_image", { path, targetExt });
  }
  const channel = new Channel<number>();
  channel.onmessage = (p) => setStatus(`${labelPrefix}... ${Math.round(p * 100)}%`);
  setStatus(`${labelPrefix}... 0%`);
  return await invoke<string>("convert_media", {
    path,
    targetExt,
    onProgress: channel,
  });
}

async function convertDrop(paths: string[], targetExt: string): Promise<void> {
  // Simplified v1: only files matching the first file's category convert.
  const firstCat = categoryOf(extOf(paths[0]));
  if (firstCat === null) {
    setStatus("unsupported");
    scheduleReset();
    return;
  }

  const matching = paths.filter((p) => categoryOf(extOf(p)) === firstCat);
  let skipped = paths.length - matching.length;
  let converted = 0;
  let lastError: string | null = null;
  let lastErrorFile = "";
  isConverting = true;
  nucleus.classList.add("active");

  try {
    for (let i = 0; i < matching.length; i++) {
      const file = matching[i];
      if (normalizeExt(extOf(file)) === targetExt) {
        skipped += 1; // already in the target format
        continue;
      }
      const label = matching.length > 1 ? `file ${i + 1}/${matching.length}` : "converting";
      try {
        await convertOne(file, firstCat, targetExt, label);
        converted += 1;
      } catch (err) {
        const message = backendMessage(err);
        lastError = message;
        lastErrorFile = file;
        // Log the full technical error for debugging; the widget shows
        // only the friendly label via friendlyDisplayError().
        // eslint-disable-next-line no-console
        console.error("conversion failed:", file, message);
        skipped += 1;
      }
    }
  } finally {
    isConverting = false;
  }

  nucleus.classList.remove("active");
  // Surface the backend error in the widget status so failures are visible
  // without devtools. Friendly label on the nucleus; full message stays on
  // hover (nucleus.title) + console. Green success shows on any conversion
  // (pure or partial); error styling is reserved for zero conversions.
  if (converted > 0 && skipped > 0) {
    if (lastError !== null) {
      setStatus(
        `done (${converted}), err: ${friendlyDisplayError(lastError, lastErrorFile)}`,
        lastError,
      );
      scheduleReset(ERROR_RESET_DELAY_MS);
    } else {
      showSuccess(`done (${converted}), skipped (${skipped})`);
      scheduleReset();
    }
  } else if (converted > 0) {
    showSuccess(`done (${converted})`);
    scheduleReset();
  } else if (lastError !== null) {
    setStatus(`error: ${friendlyDisplayError(lastError, lastErrorFile)}`, lastError);
    scheduleReset(ERROR_RESET_DELAY_MS);
  } else {
    setStatus("skipped");
    scheduleReset();
  }
}

// ---------------------------------------------------------------------------
// Drag-and-drop wiring.
// ---------------------------------------------------------------------------

type DragPayload =
  | { type: "enter"; paths: string[]; position: { x: number; y: number } }
  | { type: "over"; position: { x: number; y: number } }
  | { type: "drop"; paths: string[]; position: { x: number; y: number } }
  | { type: "leave" };

async function toCssPixels(pos: { x: number; y: number }): Promise<{ x: number; y: number }> {
  const factor = await getCurrentWindow().scaleFactor();
  return { x: pos.x / factor, y: pos.y / factor };
}

async function setupDragDrop(): Promise<void> {
  await getCurrentWebview().onDragDropEvent(async (event) => {
    const payload = event.payload as DragPayload;

    if (payload.type === "enter") {
      if (isConverting || payload.paths.length === 0) return;
      const candidates = candidatesForFirstFile(payload.paths);
      if (candidates === null) {
        setStatus("unsupported");
        return;
      }
      showTargets(candidates, categoryForFirstFile(payload.paths));
      setStatus("pick a format");
    } else if (payload.type === "over") {
      if (visibleTargets.length === 0) return;
      highlightNearest(await toCssPixels(payload.position));
    } else if (payload.type === "drop") {
      if (isConverting) {
        setStatus("busy...");
        return;
      }
      highlightNearest(await toCssPixels(payload.position));
      const picked = highlightedIndex >= 0 ? visibleTargets[highlightedIndex] : null;
      // Re-derive valid targets from the dropped files rather than trusting
      // the visible ring: a stale ring (wrong category, or no drag-enter for
      // this drop) must never drive a cross-category conversion or a skip.
      const candidates = candidatesForFirstFile(payload.paths);
      hideNodes();
      if (candidates === null) {
        setStatus("unsupported");
        clearNucleusVisuals();
        scheduleReset();
        return;
      }
      if (picked === null || !candidates.includes(picked)) {
        setStatus("no format");
        clearNucleusVisuals();
        scheduleReset();
        return;
      }
      await convertDrop(payload.paths, picked);
    } else {
      // leave
      if (isConverting) return;
      hideNodes();
      clearNucleusVisuals();
      setStatus("drop file");
    }
  });
}

// ---------------------------------------------------------------------------
// Window interactions: drag-to-move, tray placeholder notice.
// ---------------------------------------------------------------------------

function setupNucleusDrag(): void {
  nucleus.addEventListener("mousedown", (e) => {
    if (e.button === 0) void getCurrentWindow().startDragging();
  });
}

async function setupTrayNotice(): Promise<void> {
  await listen("tray-settings", () => {
    if (isConverting) return;
    window.clearTimeout(resetTimer);
    setStatus("settings soon");
    scheduleReset();
  });
}

void setupDragDrop();
setupNucleusDrag();
void setupTrayNotice();
