const state = {
  wallId: getWallId(),
  wall: null,
  mode: "wall",
  inspectorTarget: "tile",
  selectedTileId: null,
  messages: {},
  locale: localStorage.getItem("stream-wall-locale") || "en",
  auth: { enabled: false, authenticated: true, readOnly: false },
  session: { editor: true, editorProtectionEnabled: false },
  players: new Map(),
  statusByTile: new Map(),
  testResults: new Map(),
  tileDrafts: new Map(),
  wallDraft: null,
  toastTimer: null,
  statusTimer: null
};

const missingLocaleKeys = new Set();
let activeColorPicker = null;
let colorPickerElement = null;

const iconPaths = {
  wall: '<rect class="icon-body" x="3" y="4" width="18" height="16" rx="1"></rect><path class="icon-grid" d="M3 9h18M9 4v16M15 4v16"></path>',
  edit: '<path class="icon-body" d="M4 17.5V20h2.5L18.8 7.7a1.8 1.8 0 0 0-2.5-2.5L4 17.5Z"></path><path class="icon-detail" d="m14.8 6.2 2.5 2.5"></path>',
  settings: '<path class="icon-hole" d="M12 15.2a3.2 3.2 0 1 0 0-6.4 3.2 3.2 0 0 0 0 6.4Z"></path><path class="icon-gear" d="m19.4 13.7 1.2.9-1.6 2.7-1.4-.6a7.3 7.3 0 0 1-1.8 1l-.2 1.6h-3.2l-.2-1.6a7.3 7.3 0 0 1-1.8-1l-1.4.6-1.6-2.7 1.2-.9a7.5 7.5 0 0 1 0-2.1l-1.2-.9 1.6-2.7 1.4.6a7.3 7.3 0 0 1 1.8-1l.2-1.6h3.2l.2 1.6a7.3 7.3 0 0 1 1.8 1l1.4-.6 1.6 2.7-1.2.9a7.5 7.5 0 0 1 0 2.1Z"></path>',
  plus: '<path class="icon-glyph" d="M12 5v14M5 12h14"></path>',
  refresh: '<path class="icon-refresh-top" d="M20 11a8 8 0 0 0-14.7-4L4 9"></path><path class="icon-refresh-top" d="M4 4v5h5"></path><path class="icon-refresh-bottom" d="M4 13a8 8 0 0 0 14.7 4L20 15"></path><path class="icon-refresh-bottom" d="M20 20v-5h-5"></path>',
  close: '<path class="icon-glyph" d="m6 6 12 12M18 6 6 18"></path>',
  trash: '<path class="icon-lid" d="M4 7h16"></path><path class="icon-body" d="M10 11v5M14 11v5M6 7l1 13h10l1-13"></path><path class="icon-handle" d="M9 7V4h6v3"></path>',
  check: '<path class="icon-glyph" d="m5 12 4 4L19 6"></path>',
  fullscreen: '<path class="icon-corners" d="M8 3H3v5M16 3h5v5M8 21H3v-5M21 16v5h-5"></path>',
  layoutOne: '<rect class="icon-cell" x="4" y="4" width="16" height="16"></rect>',
  layoutTwo: '<rect class="icon-cell" x="4" y="4" width="7" height="7"></rect><rect class="icon-cell" x="13" y="4" width="7" height="7"></rect><rect class="icon-cell" x="4" y="13" width="7" height="7"></rect><rect class="icon-cell" x="13" y="13" width="7" height="7"></rect>',
  layoutThree: '<path class="icon-cells" d="M4 4h4v4H4zM10 4h4v4h-4zM16 4h4v4h-4zM4 10h4v4H4zM10 10h4v4h-4zM16 10h4v4h-4zM4 16h4v4H4zM10 16h4v4h-4zM16 16h4v4h-4z"></path>',
  layoutFocus: '<rect class="icon-primary-cell" x="3" y="3" width="13" height="13"></rect><path class="icon-secondary-cells" d="M18 7h3v3M18 14h3v3M18 10v4"></path>',
  layoutTwoPlusOne: '<rect class="icon-primary-cell" x="4" y="4" width="16" height="7"></rect><rect class="icon-cell" x="4" y="13" width="7" height="7"></rect><rect class="icon-cell" x="13" y="13" width="7" height="7"></rect>',
  layoutCustom: '<path class="icon-cells" d="M4 4h5v5H4zM15 4h5v5h-5zM4 15h5v5H4zM15 15h5v5h-5z"></path><path class="icon-grid" d="M11 4v16M4 11h16"></path>',
  video: '<rect class="icon-body" x="3" y="5" width="18" height="14" rx="1"></rect><path class="icon-play" d="m10 9 5 3-5 3V9Z"></path>',
  link: '<path class="icon-link-first" d="M10 13a5 5 0 0 0 7.1.1l2-2a5 5 0 0 0-7.1-7.1l-1.1 1.1"></path><path class="icon-link-second" d="M14 11a5 5 0 0 0-7.1-.1l-2 2A5 5 0 0 0 12 20l1.1-1.1"></path>',
  save: '<path class="icon-body" d="M5 4h12l2 2v14H5z"></path><path class="icon-detail" d="M8 4v6h8V4M8 20v-6h8v6"></path>',
  move: '<path class="icon-axes" d="M12 3v18M3 12h18"></path><path class="icon-arrows" d="m8 7 4-4 4 4M8 17l4 4 4-4M7 8l-4 4 4 4M17 8l4 4-4 4"></path>',
  resize: '<path class="icon-corners" d="M5 5h6M5 5v6M19 19h-6M19 19v-6"></path><path class="icon-diagonals" d="m5 5 6 6M19 19l-6-6"></path>',
  lock: '<rect class="icon-body" x="5" y="10" width="14" height="10" rx="1"></rect><path class="icon-shackle" d="M8 10V7a4 4 0 0 1 8 0v3"></path>',
  unlock: '<rect class="icon-body" x="5" y="10" width="14" height="10" rx="1"></rect><path class="icon-shackle" d="M8 10V7a4 4 0 0 1 7-2"></path>',
  upload: '<path class="icon-arrow" d="M12 16V4M7 9l5-5 5 5"></path><path class="icon-tray" d="M5 14v5h14v-5"></path>',
  download: '<path class="icon-arrow" d="M12 4v12M7 11l5 5 5-5"></path><path class="icon-tray" d="M5 19h14"></path>',
  circle: '<circle class="icon-glyph" cx="12" cy="12" r="8"></circle>'
};

const iconAliases = {
  "layout-one": "layoutOne",
  "layout-two": "layoutTwo",
  "layout-three": "layoutThree",
  "layout-focus": "layoutFocus",
  "layout-two-plus-one": "layoutTwoPlusOne",
  "layout-custom": "layoutCustom"
};

document.addEventListener("DOMContentLoaded", init);

async function init() {
  wireStaticActions();
  document.addEventListener("visibilitychange", handleVisibilityChange);
  window.addEventListener("online", resumePlayers);
  window.addEventListener("offline", suspendPlayers);

  try {
    await loadLocale(state.locale);
    await loadAuth();
    if (!state.auth.authenticated) return;
    await loadSession();
    await loadWall();
    state.statusTimer = window.setInterval(refreshStatuses, 5000);
  } catch (error) {
    console.error(error);
    showToast(t("messages.loadFailed"));
  }
}

function wireStaticActions() {
  renderLayoutPresets();

  document.querySelectorAll("button[data-mode]").forEach(button => {
    button.addEventListener("click", () => setMode(button.dataset.mode));
  });

  document.getElementById("web-settings")?.addEventListener("click", () => {
    state.inspectorTarget = state.session.editor ? "wall" : "access";
    setMode("edit");
  });
  document.getElementById("lock-editor")?.addEventListener("click", lockEditor);
  document.getElementById("logout-access")?.addEventListener("click", logoutAccess);
  document.getElementById("access-login-form")?.addEventListener("submit", event => {
    event.preventDefault();
    loginAccess(new FormData(event.currentTarget));
  });

  document.getElementById("add-video")?.addEventListener("click", addVideo);
  document.getElementById("refresh-wall")?.addEventListener("click", async () => {
    discardEditorDrafts();
    await loadWall();
    showToast(t("messages.refreshed"));
  });
  document.getElementById("save-layout")?.addEventListener("click", saveLayoutPreset);
  document.getElementById("export-layouts")?.addEventListener("click", exportSavedLayouts);
  document.getElementById("import-layouts")?.addEventListener("click", () => {
    document.getElementById("import-layouts-file")?.click();
  });
  document.getElementById("import-layouts-file")?.addEventListener("change", importSavedLayouts);

  document.querySelectorAll("[data-layout]").forEach(button => {
    button.addEventListener("click", () => applyLayout(button.dataset.layout));
  });
}

async function loadLocale(locale) {
  const requested = locale || "en";
  const fallbackResponse = await fetch("/locales/en.json", { cache: "no-store" });
  if (!fallbackResponse.ok)
    throw new Error(`English locale returned ${fallbackResponse.status}`);
  const fallback = await fallbackResponse.json();

  try {
    if (requested === "en") {
      state.messages = fallback;
    } else {
      const response = await fetch(`/locales/${encodeURIComponent(requested)}.json`, { cache: "no-store" });
      if (!response.ok) throw new Error(`Locale ${requested} returned ${response.status}`);
      state.messages = mergeLocale(fallback, await response.json());
    }
    state.locale = requested;
  } catch (error) {
    if (requested !== "en") {
      state.messages = fallback;
      state.locale = "en";
      showToast(t("messages.localeFallback"));
    } else {
      throw error;
    }
  }

  applyLabels(document);
}

function mergeLocale(fallback, selected) {
  if (!selected || typeof selected !== "object" || Array.isArray(selected))
    return fallback;

  const merged = { ...fallback };
  Object.entries(selected).forEach(([key, value]) => {
    if (value && typeof value === "object" && !Array.isArray(value) &&
        fallback[key] && typeof fallback[key] === "object" && !Array.isArray(fallback[key]))
      merged[key] = mergeLocale(fallback[key], value);
    else
      merged[key] = value;
  });
  return merged;
}

async function loadAuth() {
  const response = await fetch("/api/auth/session", { cache: "no-store" });
  if (!response.ok) throw await createApiError(response);
  state.auth = await response.json();
  updateAccessGate();
}

function updateAccessGate() {
  const locked = state.auth.enabled && !state.auth.authenticated;
  const readOnly = !!state.auth.readOnly;
  if (readOnly && state.mode === "edit") state.mode = "wall";
  document.querySelectorAll('[data-mode="edit"]').forEach(button => { button.hidden = readOnly; });
  for (const id of ["web-settings", "add-video"])
    document.getElementById(id)?.toggleAttribute("hidden", readOnly);
  document.getElementById("wall-body").hidden = locked;
  document.getElementById("access-gate").hidden = !locked;
  const logout = document.getElementById("logout-access");
  if (logout) logout.hidden = !state.auth.enabled || !state.auth.authenticated;
  if (locked) {
    window.setTimeout(() => document.getElementById("access-token")?.focus(), 0);
  }
}

function showAccessGate(message = "") {
  if (!state.auth.enabled) return;
  state.auth.authenticated = false;
  stopPlayers();
  updateAccessGate();
  const errorElement = document.getElementById("access-login-error");
  if (errorElement) {
    errorElement.textContent = message;
    errorElement.hidden = !message;
  }
}

async function loginAccess(form) {
  const button = document.querySelector("#access-login-form button[type=submit]");
  const errorElement = document.getElementById("access-login-error");
  if (button) button.disabled = true;
  if (errorElement) errorElement.hidden = true;

  try {
    const response = await fetch("/api/auth/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ token: String(form.get("token") || "") })
    });
    if (!response.ok) throw await createApiError(response);
    state.auth = await response.json();
    updateAccessGate();
    await loadSession();
    await loadWall();
    if (!state.statusTimer) state.statusTimer = window.setInterval(refreshStatuses, 5000);
  } catch (error) {
    console.error(error);
    if (errorElement) {
      errorElement.textContent = errorMessage(error, t("messages.authFailed"));
      errorElement.hidden = false;
    }
  } finally {
    if (button) button.disabled = false;
  }
}

async function logoutAccess() {
  try {
    const response = await fetch("/api/auth/session", { method: "DELETE" });
    if (!response.ok) throw await createApiError(response);
    state.auth = await response.json();
    state.wall = null;
    state.statusByTile.clear();
    stopPlayers();
    updateAccessGate();
    showToast(t("messages.signedOut"));
  } catch (error) {
    console.error(error);
    showToast(errorMessage(error, t("messages.saveFailed")));
  }
}

async function loadWall({ renderPage = true, refresh = true } = {}) {
  const wall = await api(`/api/walls/${encodeURIComponent(state.wallId)}`);
  state.wall = wall;
  state.testResults = new Map([...state.testResults].filter(([tileId]) =>
    wall.tiles.some(tile => tile.id === tileId)));
  if (!state.selectedTileId || !wall.tiles.some(tile => tile.id === state.selectedTileId))
    state.selectedTileId = wall.tiles[0]?.id || null;
  state.locale = wall.locale || state.locale || "en";
  if (renderPage) render();
  if (refresh) await refreshStatuses();
  return wall;
}

async function loadSession() {
  state.session = await api(`/api/walls/${encodeURIComponent(state.wallId)}/session`);
}

function render({ preserveCanvas = false } = {}) {
  if (!state.wall) return;

  const app = document.getElementById("app");
  const wall = getWallRenderValues();
  const theme = applyWallSurface(wall);
  app.dataset.mode = state.mode;
  updateWallHeader(wall);

  const editorMode = state.mode === "edit" && state.session.editor;
  document.getElementById("layout-strip").hidden = !editorMode;
  const lockButton = document.getElementById("lock-editor");
  if (lockButton) lockButton.hidden = !(state.session.editorProtectionEnabled && state.session.editor);
  renderSavedLayouts();
  document.querySelectorAll("button[data-mode]").forEach(button => {
    const active = button.dataset.mode === state.mode;
    button.classList.toggle("is-active", active);
    button.setAttribute("aria-pressed", String(active));
  });
  document.querySelectorAll("[data-layout]").forEach(button => {
    button.classList.toggle("is-active", button.dataset.layout === wall.layoutPreset);
  });

  if (!preserveCanvas || !patchCanvas(wall)) renderCanvas();
  renderInspector();
  applyLabels(document);
}

function getWallRenderValues() {
  const wall = { ...state.wall, ...(state.wallDraft || {}) };
  const columns = clamp(Number(wall.gridColumns) || state.wall.gridColumns, 1, 12);
  const rows = clamp(Number(wall.gridRows) || state.wall.gridRows, 1, 12);
  const tileGap = Number(wall.tileGap);
  wall.gridColumns = columns;
  wall.gridRows = rows;
  wall.tileGap = Number.isFinite(tileGap) ? clamp(tileGap, 0, 64) : 5;
  if (columns !== state.wall.gridColumns || rows !== state.wall.gridRows)
    wall.layoutPreset = "custom";
  return wall;
}

function applyWallSurface(wall) {
  const app = document.getElementById("app");
  const theme = getThemeSurface(wall);
  document.documentElement.style.colorScheme = theme.colorScheme;
  app.style.colorScheme = theme.colorScheme;
  [document.documentElement, app].forEach(element => {
    element.style.setProperty("--wall-bg", theme.backgroundColor);
    element.style.setProperty("--wall-text", theme.foregroundColor);
    element.style.setProperty("--wall-shell", theme.panelColor);
    element.style.setProperty("--wall-panel", theme.panelColor);
    element.style.setProperty("--wall-panel-strong", theme.panelStrongColor);
    element.style.setProperty("--wall-line", theme.lineColor);
    element.style.setProperty("--wall-muted", theme.mutedColor);
    element.style.setProperty("--wall-accent", theme.accentColor);
    element.style.setProperty("--wall-focus", theme.focusColor);
  });
  document.documentElement.lang = wall.locale || state.locale || "en";
  return theme;
}

function updateWallHeader(wall) {
  const name = document.getElementById("wall-name");
  const meta = document.getElementById("wall-meta");
  if (name) name.textContent = wall.name;
  if (meta) {
    meta.textContent = t("wall.meta", {
      count: state.wall.tiles.length,
      layout: layoutLabel(wall.layoutPreset)
    });
  }
}

function renderSavedLayouts() {
  const container = document.getElementById("saved-layouts");
  if (!container) return;
  const presets = state.wall?.presets || [];
  container.setAttribute("aria-label", t("labels.savedLayouts"));
  container.innerHTML = presets.map(preset => `<div class="saved-layout">
    <button class="preset-button" type="button" data-preset-id="${escapeAttribute(preset.id)}" aria-pressed="false">${escapeHtml(preset.name)}</button>
    <button class="icon-button preset-delete" type="button" data-preset-delete="${escapeAttribute(preset.id)}" data-label-key="actions.delete" data-tooltip-key="tooltips.deletePreset"><span data-icon="trash" aria-hidden="true"></span></button>
  </div>`).join("");
  applyLabels(container);
  container.querySelectorAll("[data-preset-id]").forEach(button => {
    button.addEventListener("click", () => applySavedLayout(button.dataset.presetId));
  });
  container.querySelectorAll("[data-preset-delete]").forEach(button => {
    button.addEventListener("click", () => deleteSavedLayout(button.dataset.presetDelete));
  });
}

function renderCanvas() {
  stopPlayers();
  const canvas = document.getElementById("wall-canvas");
  const wall = getWallRenderValues();
  const editorMode = state.mode === "edit" && state.session.editor;
  applyCanvasLayout(canvas, wall, editorMode);
  canvas.setAttribute("aria-label", wall.name);

  const visibleTiles = getVisibleTiles(wall);
  canvas.innerHTML = visibleTiles.map(tile => renderTile(tile, wall)).join("");
  renderIcons(canvas);

  canvas.querySelectorAll(".stream-tile").forEach(tileElement => {
    const tileId = tileElement.dataset.tileId;
    wireTileElement(tileElement, state.wall.tiles.find(item => item.id === tileId), editorMode);
  });

  updateTileSelection();
}

function patchCanvas(wall = getWallRenderValues()) {
  const canvas = document.getElementById("wall-canvas");
  if (!canvas || !state.wall) return false;

  const editorMode = state.mode === "edit" && state.session.editor;
  const visibleTiles = getVisibleTiles(wall);

  applyCanvasLayout(canvas, wall, editorMode);
  canvas.setAttribute("aria-label", wall.name);

  const existingTiles = [...canvas.querySelectorAll(".stream-tile")];
  const existingById = new Map(existingTiles.map(element => [element.dataset.tileId, element]));
  const visibleIds = new Set(visibleTiles.map(tile => tile.id));
  existingTiles.forEach(tileElement => {
    if (visibleIds.has(tileElement.dataset.tileId)) return;
    const player = state.players.get(tileElement.dataset.tileId);
    player?.stop();
    state.players.delete(tileElement.dataset.tileId);
    tileElement.remove();
  });

  visibleTiles.forEach((tile, index) => {
    let tileElement = existingById.get(tile.id);
    if (!tileElement) {
      const template = document.createElement("template");
      template.innerHTML = renderTile(tile, wall).trim();
      tileElement = template.content.firstElementChild;
      if (!tileElement) return;
      renderIcons(tileElement);
      wireTileElement(tileElement, state.wall.tiles.find(item => item.id === tile.id), editorMode);
    }
    const anchor = canvas.children[index] || null;
    if (anchor !== tileElement)
      canvas.insertBefore(tileElement, anchor);
    updateTileElement(tileElement, tile, wall);
    syncTileEditorChrome(tileElement, tile, editorMode);
  });
  updateTileSelection();
  return true;
}

function applyCanvasLayout(canvas, wall, editorMode) {
  const freeform = isFreeformWall(wall);
  canvas.classList.toggle("is-editing", editorMode);
  canvas.classList.toggle("is-freeform", freeform);
  canvas.style.setProperty("--wall-columns", String(wall.gridColumns));
  canvas.style.setProperty("--wall-gap", `${wall.tileGap}px`);
  canvas.style.gridTemplateRows = freeform
    ? ""
    : `repeat(${wall.gridRows}, minmax(180px, 1fr))`;
}

function wireTileElement(tileElement, tile, editorMode) {
  if (!tile) return;
  const tileId = tileElement.dataset.tileId;
  tileElement.addEventListener("click", () => {
    state.selectedTileId = tileId;
    state.inspectorTarget = "tile";
    if (state.mode === "edit") renderInspector();
    else updateTileSelection();
  });
  tileElement.addEventListener("keydown", event => {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      tileElement.click();
    }
  });
  tileElement.querySelector("[data-action=fullscreen]")?.addEventListener("click", event => {
    event.stopPropagation();
    tileElement.requestFullscreen?.();
  });

  if (editorMode)
    wireTileEditor(tileElement, tile);

  startTilePlayer(tileElement, tile);
}

function getTileRenderValues(tile, wall = state.wall, includeDraft = true) {
  const draft = includeDraft ? (state.tileDrafts.get(tile.id) || {}) : {};
  const view = { ...tile };
  ["title", "protocol", "column", "row", "columnSpan", "rowSpan", "aspect",
    "sizeUnit", "x", "y", "width", "height", "borderWidth", "borderColor", "showTitle",
    "titleBackground", "titleBackgroundColor"]
    .forEach(name => {
      if (Object.prototype.hasOwnProperty.call(draft, name)) view[name] = draft[name];
    });

  const columns = Math.max(1, Number(wall?.gridColumns) || 1);
  const rows = Math.max(1, Number(wall?.gridRows) || 1);
  view.column = clamp(Number(view.column) || 0, 0, columns - 1);
  view.row = clamp(Number(view.row) || 0, 0, rows - 1);
  view.columnSpan = clamp(Number(view.columnSpan) || 1, 1, columns - view.column);
  view.rowSpan = clamp(Number(view.rowSpan) || 1, 1, rows - view.row);
  view.sizeUnit = view.sizeUnit === "pixels" ? "pixels" : "percent";
  const defaults = defaultTileGeometry(view, wall);
  view.x = geometryNumber(view.x, defaults.x);
  view.y = geometryNumber(view.y, defaults.y);
  view.width = geometryNumber(view.width, defaults.width);
  view.height = geometryNumber(view.height, defaults.height);
  normalizeClientGeometry(view);
  view.borderWidth = clamp(Number(view.borderWidth) || 0, 0, 24);
  view.showTitle = Boolean(view.showTitle);
  return view;
}

function applyTileStyle(tileElement, tile, wall = state.wall) {
  if (isFreeformWall(wall)) {
    const unit = tile.sizeUnit === "pixels" ? "px" : "%";
    tileElement.style.position = "absolute";
    tileElement.style.left = `${formatNumber(tile.x)}${unit}`;
    tileElement.style.top = `${formatNumber(tile.y)}${unit}`;
    tileElement.style.width = `${formatNumber(tile.width)}${unit}`;
    tileElement.style.height = `${formatNumber(tile.height)}${unit}`;
    tileElement.style.removeProperty("grid-column");
    tileElement.style.removeProperty("grid-row");
  } else {
    tileElement.style.removeProperty("position");
    tileElement.style.removeProperty("left");
    tileElement.style.removeProperty("top");
    tileElement.style.removeProperty("width");
    tileElement.style.removeProperty("height");
    tileElement.style.gridColumn = `${tile.column + 1} / span ${Math.max(1, tile.columnSpan)}`;
    tileElement.style.gridRow = `${tile.row + 1} / span ${Math.max(1, tile.rowSpan)}`;
  }
  tileElement.style.setProperty("--tile-border", safeColor(tile.borderColor, "#e7a346"));
  tileElement.style.setProperty("--tile-border-width", `${tile.borderWidth}px`);
  tileElement.style.setProperty("--tile-title-background", tile.showTitle ? getTileTitleBackground(tile) : "transparent");
  tileElement.style.setProperty("--tile-object-fit", tile.aspect === "cover" ? "cover" : tile.aspect === "stretch" ? "fill" : "contain");
}

function getTileTitleBackground(tile) {
  return tile.titleBackground === "customColor"
    ? safeColor(tile.titleBackgroundColor, tile.borderColor)
    : tile.titleBackground === "transparent" ? "transparent" : safeColor(tile.borderColor, "#e7a346");
}

function updateTileElement(tileElement, baseTile, wall = state.wall) {
  if (!tileElement || !baseTile) return;
  const tile = getTileRenderValues(baseTile, wall);
  const configured = hasConfiguredSource(baseTile);
  applyTileStyle(tileElement, tile, wall);
  tileElement.setAttribute("aria-selected", String(tile.id === state.selectedTileId));

  const titleElement = tileElement.querySelector(".tile-title");
  if (tile.showTitle) {
    const title = titleElement || document.createElement("div");
    title.className = "tile-title";
    title.innerHTML = `<span></span><small></small>`;
    title.querySelector("span").textContent = tile.title || t("labels.video");
    title.querySelector("small").textContent = optionLabel(tile.protocol);
    if (!titleElement) tileElement.insertBefore(title, tileElement.querySelector(".tile-status"));
  } else {
    titleElement?.remove();
  }

  const mediaSlot = tileElement.querySelector(".media-slot");
  if (!mediaSlot) return;
  const empty = mediaSlot.querySelector(".tile-empty");
  if (configured) {
    empty?.remove();
  } else if (!mediaSlot.querySelector("img, canvas") && !empty) {
    mediaSlot.innerHTML = `<div class="tile-empty">${icon("video")}<span>${escapeHtml(t("status.configure"))}</span></div>`;
  }
}

function syncTileEditorChrome(tileElement, tile, editorMode) {
  const moveHandle = tileElement.querySelector("[data-action=move]");
  const resizeHandle = tileElement.querySelector("[data-action=resize]");
  if (!editorMode) {
    moveHandle?.remove();
    resizeHandle?.remove();
    return;
  }

  if (!moveHandle)
    tileElement.insertAdjacentHTML("afterbegin", `<button class="tile-edit-handle tile-drag-handle" type="button" data-action="move" data-label-key="actions.moveTile" data-tooltip-key="tooltips.moveTile"><span data-icon="move" aria-hidden="true"></span></button>`);
  if (!resizeHandle)
    tileElement.insertAdjacentHTML("beforeend", `<button class="tile-edit-handle tile-resize-handle" type="button" data-action="resize" data-label-key="actions.resizeTile" data-tooltip-key="tooltips.resizeTile"><span data-icon="resize" aria-hidden="true"></span></button>`);
  const handlesWereAdded = !moveHandle || !resizeHandle;
  if (handlesWereAdded) {
    applyLabels(tileElement);
    wireTileEditor(tileElement, tile);
  }
}

function hasConfiguredSource(tile) {
  return Boolean(tile?.enabled && tile?.host && Number(tile?.port) > 0);
}

function startTilePlayer(tileElement, tile) {
  if (!tileElement || !tile || !hasConfiguredSource(tile)) return;
  const tileId = tile.id;
  const player = new StreamPlayer(tileElement.querySelector(".media-slot"), tile, status => {
    if (tileElement.isConnected) setVisualTileStatus(tileElement, status.state, status.detail);
  });
  state.players.set(tileId, player);
}

function restartTilePlayer(tileId) {
  const previous = state.players.get(tileId);
  if (previous) {
    previous.stop();
    state.players.delete(tileId);
  }

  const tile = state.wall?.tiles.find(item => item.id === tileId);
  const tileElement = document.querySelector(`.stream-tile[data-tile-id="${escapeAttribute(tileId)}"]`);
  if (!tile || !tileElement) return;
  updateTileElement(tileElement, tile);
  startTilePlayer(tileElement, tile);
  updateTileElement(tileElement, tile);
}

function sourceConfigKey(tile) {
  return JSON.stringify([tile?.enabled, tile?.protocol, tile?.host, tile?.port, tile?.path,
    tile?.username, tile?.password, tile?.credentialReference, tile?.transport, tile?.codec,
    tile?.h264SpsPpsBase64, tile?.h265VpsSpsPpsBase64]);
}

function renderTile(baseTile, wall = state.wall) {
  const tile = getTileRenderValues(baseTile, wall);
  const selected = tile.id === state.selectedTileId;
  const editorMode = state.mode === "edit" && state.session.editor;
  const configured = hasConfiguredSource(baseTile);
  const objectFit = tile.aspect === "cover" ? "cover" : tile.aspect === "stretch" ? "fill" : "contain";
  const titleBackground = tile.showTitle ? getTileTitleBackground(tile) : "transparent";
  const status = state.statusByTile.get(tile.id);
  const stateName = status?.state?.toLowerCase() || (configured ? "idle" : "idle");
  const statusText = configured ? statusLabel(status?.state || "Idle") : t("status.configure");
  const detected = status?.detectedCodec && status.detectedCodec !== "Auto"
    ? status.detectedCodec
    : baseTile.codec === "auto" ? "" : optionLabel(baseTile.codec);
  const position = tilePositionStyle(tile, wall);

  return `<article class="stream-tile" tabindex="0" role="button" data-tile-id="${escapeAttribute(tile.id)}"
      aria-selected="${selected}" style="${position} --tile-border:${safeColor(tile.borderColor, "#e7a346")}; --tile-border-width:${tile.borderWidth}px; --tile-title-background:${titleBackground}; --tile-object-fit:${objectFit}">
    <div class="media-slot">
      ${configured ? "" : `<div class="tile-empty">${icon("video")}<span>${escapeHtml(statusText)}</span></div>`}
    </div>
    ${tile.showTitle ? `<div class="tile-title"><span>${escapeHtml(tile.title || t("labels.video"))}</span><small>${escapeHtml(optionLabel(tile.protocol))}</small></div>` : ""}
      ${editorMode ? `<button class="tile-edit-handle tile-drag-handle" type="button" data-action="move" data-label-key="actions.moveTile" data-tooltip-key="tooltips.moveTile"><span data-icon="move" aria-hidden="true"></span></button>
      <button class="tile-edit-handle tile-resize-handle" type="button" data-action="resize" data-label-key="actions.resizeTile" data-tooltip-key="tooltips.resizeTile"><span data-icon="resize" aria-hidden="true"></span></button>` : ""}
    <button class="tile-action" type="button" data-action="fullscreen" data-label-key="actions.fullScreen" data-tooltip-key="tooltips.fullScreen">
      <span data-icon="fullscreen" aria-hidden="true"></span>
    </button>
    <div class="tile-status" data-state="${escapeAttribute(stateName)}">
      <strong><span class="status-dot"></span><span data-status-text>${escapeHtml(statusText)}</span></strong>
      <span data-detected-codec>${escapeHtml(detected)}</span>
    </div>
  </article>`;
}

function tilePositionStyle(tile, wall = state.wall) {
  if (isFreeformWall(wall)) {
    const unit = tile.sizeUnit === "pixels" ? "px" : "%";
    return `position:absolute; left:${formatNumber(tile.x)}${unit}; top:${formatNumber(tile.y)}${unit}; width:${formatNumber(tile.width)}${unit}; height:${formatNumber(tile.height)}${unit};`;
  }
  return `grid-column: ${tile.column + 1} / span ${Math.max(1, tile.columnSpan)}; grid-row: ${tile.row + 1} / span ${Math.max(1, tile.rowSpan)};`;
}

function tilePlacement(tile, wall = state.wall) {
  const view = getTileRenderValues(tile, wall);
  return {
    column: view.column,
    row: view.row,
    columnSpan: view.columnSpan,
    rowSpan: view.rowSpan
  };
}

function tileGeometry(tile, wall = state.wall) {
  const view = getTileRenderValues(tile, wall);
  return {
    sizeUnit: view.sizeUnit,
    x: view.x,
    y: view.y,
    width: view.width,
    height: view.height
  };
}

function setTilePlacement(tile, placement) {
  Object.assign(tile, placement);
  const draft = state.tileDrafts.get(tile.id);
  if (draft) Object.assign(draft, placement);
}

function sameTilePlacement(first, second) {
  return first.column === second.column && first.row === second.row &&
    first.columnSpan === second.columnSpan && first.rowSpan === second.rowSpan;
}

function setTileGeometry(tile, geometry) {
  Object.assign(tile, geometry);
  normalizeClientGeometry(tile);
  const draft = state.tileDrafts.get(tile.id);
  if (draft) {
    draft.sizeUnit = tile.sizeUnit;
    draft.x = tile.x;
    draft.y = tile.y;
    draft.width = tile.width;
    draft.height = tile.height;
  }
}

function sameTileGeometry(first, second) {
  return first.sizeUnit === second.sizeUnit && first.x === second.x && first.y === second.y &&
    first.width === second.width && first.height === second.height;
}

function tileGeometryRect(geometry, canvasRect) {
  const unit = geometry.sizeUnit === "pixels" ? "pixels" : "percent";
  const horizontal = value => unit === "pixels" ? value : value / 100 * Math.max(1, canvasRect.width);
  const vertical = value => unit === "pixels" ? value : value / 100 * Math.max(1, canvasRect.height);
  const left = horizontal(geometry.x);
  const top = vertical(geometry.y);
  return {
    left,
    top,
    right: left + horizontal(geometry.width),
    bottom: top + vertical(geometry.height)
  };
}

function tileGeometriesOverlap(first, second, canvasRect) {
  const firstRect = tileGeometryRect(first, canvasRect);
  const secondRect = tileGeometryRect(second, canvasRect);
  return firstRect.left < secondRect.right && firstRect.right > secondRect.left &&
    firstRect.top < secondRect.bottom && firstRect.bottom > secondRect.top;
}

function geometryIntersectionArea(first, second, canvasRect) {
  const firstRect = tileGeometryRect(first, canvasRect);
  const secondRect = tileGeometryRect(second, canvasRect);
  return Math.max(0, Math.min(firstRect.right, secondRect.right) - Math.max(firstRect.left, secondRect.left)) *
    Math.max(0, Math.min(firstRect.bottom, secondRect.bottom) - Math.max(firstRect.top, secondRect.top));
}

function findTileGeometryCollision(tileId, candidate, wall, canvasRect) {
  return getVisibleTiles(wall)
    .filter(tile => tile.id !== tileId)
    .map(tile => ({ tile, geometry: tileGeometry(tile, wall) }))
    .filter(item => tileGeometriesOverlap(candidate, item.geometry, canvasRect))
    .sort((first, second) => geometryIntersectionArea(candidate, second.geometry, canvasRect) -
      geometryIntersectionArea(candidate, first.geometry, canvasRect))[0] || null;
}

function tilePlacementsOverlap(first, second) {
  return first.column < second.column + second.columnSpan &&
    first.column + first.columnSpan > second.column &&
    first.row < second.row + second.rowSpan &&
    first.row + first.rowSpan > second.row;
}

function placementIntersectionArea(first, second) {
  const width = Math.max(0, Math.min(first.column + first.columnSpan, second.column + second.columnSpan) -
    Math.max(first.column, second.column));
  const height = Math.max(0, Math.min(first.row + first.rowSpan, second.row + second.rowSpan) -
    Math.max(first.row, second.row));
  return width * height;
}

function findTilePlacementCollision(tileId, candidate, wall) {
  return getVisibleTiles(wall)
    .filter(tile => tile.id !== tileId)
    .map(tile => ({ tile, placement: tilePlacement(tile, wall) }))
    .filter(item => tilePlacementsOverlap(candidate, item.placement))
    .sort((first, second) => placementIntersectionArea(candidate, second.placement) -
      placementIntersectionArea(candidate, first.placement))[0] || null;
}

function previewTilePlacement(tile, placement, wall) {
  setTilePlacement(tile, placement);
  const tileElement = document.querySelector(`.stream-tile[data-tile-id="${escapeAttribute(tile.id)}"]`);
  if (tileElement)
    applyTileStyle(tileElement, getTileRenderValues(tile, wall), wall);
  if (tile.id === state.selectedTileId)
    syncPlacementFields(tile);
}

function previewTileGeometry(tile, geometry, wall) {
  setTileGeometry(tile, geometry);
  const tileElement = document.querySelector(`.stream-tile[data-tile-id="${escapeAttribute(tile.id)}"]`);
  if (tileElement)
    applyTileStyle(tileElement, getTileRenderValues(tile, wall), wall);
  if (tile.id === state.selectedTileId)
    syncPlacementFields(tile);
}

function wireTileEditor(tileElement, tile) {
  if (!tile) return;
  const handles = [
    [tileElement.querySelector("[data-action=move]"), "move"],
    [tileElement.querySelector("[data-action=resize]"), "resize"]
  ];
  handles.forEach(([handle, mode]) => {
    if (!handle || handle.dataset.tileEditorWired === "true") return;
    handle.dataset.tileEditorWired = "true";
    handle.addEventListener("pointerdown", event => beginTilePointer(event, tileElement, tile, mode));
    handle.addEventListener("click", event => {
      event.preventDefault();
      event.stopPropagation();
    });
  });
}

function beginTilePointer(event, tileElement, tile, mode) {
  event.preventDefault();
  event.stopPropagation();
  const canvas = document.getElementById("wall-canvas");
  const canvasRect = canvas.getBoundingClientRect();
  const styles = getComputedStyle(canvas);
  const columnGap = parseFloat(styles.columnGap) || 0;
  const rowGap = parseFloat(styles.rowGap) || 0;
  const wall = getWallRenderValues();
  const freeform = isFreeformWall(wall);
  const draft = state.tileDrafts.get(tile.id);
  const currentTile = getTileRenderValues(tile, wall);
  const columns = Math.max(1, wall.gridColumns);
  const rows = Math.max(1, wall.gridRows);
  const columnStep = (canvasRect.width - columnGap * (columns - 1)) / columns + columnGap;
  const rowStep = (canvasRect.height - rowGap * (rows - 1)) / rows + rowGap;
  let start = freeform
    ? {
        sizeUnit: currentTile.sizeUnit,
        x: currentTile.x,
        y: currentTile.y,
        width: currentTile.width,
        height: currentTile.height
      }
    : {
        column: currentTile.column,
        row: currentTile.row,
        columnSpan: currentTile.columnSpan,
        rowSpan: currentTile.rowSpan
      };
  let dragOriginX = event.clientX;
  let dragOriginY = event.clientY;
  const originalPlacements = new Map([[tile.id, { ...start }]]);
  const changedTileIds = new Set([tile.id]);
  const pointerId = event.pointerId;
  const handle = event.currentTarget;
  const movementThreshold = event.pointerType === "touch" || event.pointerType === "pen" ? 8 : 3;
  let hasMoved = false;
  let swapLocked = false;
  handle.setPointerCapture?.(pointerId);

  const update = moveEvent => {
    if (!hasMoved && Math.hypot(moveEvent.clientX - event.clientX, moveEvent.clientY - event.clientY) < movementThreshold)
      return;
    if (!hasMoved) {
      hasMoved = true;
      handle.classList.add("is-dragging");
      tileElement.classList.add("is-dragging");
    }
    if (freeform) {
      if (mode === "move" && swapLocked)
        return;
      const unit = start.sizeUnit === "pixels" ? "pixels" : "percent";
      const horizontalDelta = unit === "pixels"
        ? moveEvent.clientX - dragOriginX
        : (moveEvent.clientX - dragOriginX) / Math.max(1, canvasRect.width) * 100;
      const verticalDelta = unit === "pixels"
        ? moveEvent.clientY - dragOriginY
        : (moveEvent.clientY - dragOriginY) / Math.max(1, canvasRect.height) * 100;
      if (mode === "move") {
        const candidate = {
          sizeUnit: start.sizeUnit,
          x: start.x + horizontalDelta,
          y: start.y + verticalDelta,
          width: start.width,
          height: start.height
        };
        normalizeClientGeometry(candidate);
        const currentGeometry = tileGeometry(tile, wall);
        const collision = sameTileGeometry(candidate, currentGeometry)
          ? null
          : findTileGeometryCollision(tile.id, candidate, wall, canvasRect);
        if (collision) {
          if (!originalPlacements.has(collision.tile.id))
            originalPlacements.set(collision.tile.id, tileGeometry(collision.tile, wall));
          const movingGeometry = { ...currentGeometry };
          previewTileGeometry(tile, collision.geometry, wall);
          previewTileGeometry(collision.tile, movingGeometry, wall);
          changedTileIds.add(collision.tile.id);
          swapLocked = true;
          start = { ...tileGeometry(tile, wall) };
          dragOriginX = moveEvent.clientX;
          dragOriginY = moveEvent.clientY;
        } else {
          previewTileGeometry(tile, candidate, wall);
        }
      } else {
        tile.width = start.width + horizontalDelta;
        tile.height = start.height + verticalDelta;
      }
      normalizeClientGeometry(tile);
      if (draft) {
        draft.sizeUnit = tile.sizeUnit;
        draft.x = tile.x;
        draft.y = tile.y;
        draft.width = tile.width;
        draft.height = tile.height;
      }
      applyTileStyle(tileElement, getTileRenderValues(tile, wall), wall);
      syncPlacementFields(tile);
      return;
    }
    const deltaColumn = Math.round((moveEvent.clientX - dragOriginX) / columnStep);
    const deltaRow = Math.round((moveEvent.clientY - dragOriginY) / rowStep);
    if (mode === "move") {
      const candidate = {
        column: clamp(start.column + deltaColumn, 0, columns - start.columnSpan),
        row: clamp(start.row + deltaRow, 0, rows - start.rowSpan),
        columnSpan: start.columnSpan,
        rowSpan: start.rowSpan
      };
      const currentPlacement = tilePlacement(tile, wall);
      const collision = sameTilePlacement(candidate, currentPlacement)
        ? null
        : findTilePlacementCollision(tile.id, candidate, wall);
      if (collision) {
        if (!originalPlacements.has(collision.tile.id))
          originalPlacements.set(collision.tile.id, tilePlacement(collision.tile, wall));
        const movingPlacement = { ...currentPlacement };
        previewTilePlacement(tile, collision.placement, wall);
        previewTilePlacement(collision.tile, movingPlacement, wall);
        changedTileIds.add(collision.tile.id);
        start = { ...tilePlacement(tile, wall) };
        dragOriginX = moveEvent.clientX;
        dragOriginY = moveEvent.clientY;
      } else {
        previewTilePlacement(tile, candidate, wall);
      }
    } else {
      tile.columnSpan = clamp(start.columnSpan + deltaColumn, 1, columns - start.column);
      tile.rowSpan = clamp(start.rowSpan + deltaRow, 1, rows - start.row);
    }
    if (draft) {
      draft.column = tile.column;
      draft.row = tile.row;
      draft.columnSpan = tile.columnSpan;
      draft.rowSpan = tile.rowSpan;
    }
    applyTileStyle(tileElement, getTileRenderValues(tile, wall), wall);
    syncPlacementFields(tile);
  };

  const finish = async cancelled => {
    document.removeEventListener("pointermove", update);
    document.removeEventListener("pointerup", onPointerUp);
    document.removeEventListener("pointercancel", onPointerCancel);
    handle.classList.remove("is-dragging");
    tileElement.classList.remove("is-dragging");
    if (!hasMoved) return;
    if (cancelled) {
      const restoredWall = getWallRenderValues();
      originalPlacements.forEach((placement, tileId) => {
        const originalTile = state.wall.tiles.find(item => item.id === tileId);
        if (!originalTile) return;
        if (freeform) previewTileGeometry(originalTile, placement, restoredWall);
        else previewTilePlacement(originalTile, placement, restoredWall);
      });
      syncPlacementFields(tile);
      return;
    }
    try {
      if (freeform) {
        const wallForSave = getWallRenderValues();
        const additionalGeometries = [...changedTileIds]
          .filter(tileId => tileId !== tile.id)
          .map(tileId => {
            const changedTile = state.wall.tiles.find(item => item.id === tileId);
            return changedTile ? { tileId, ...tileGeometry(changedTile, wallForSave) } : null;
          })
          .filter(Boolean);
        await api(`/api/walls/${encodeURIComponent(state.wallId)}/tiles/${encodeURIComponent(tile.id)}/geometry`, {
          method: "PUT",
          body: JSON.stringify({
            ...tileGeometry(tile, wallForSave),
            additionalGeometries
          })
        });
        return;
      }
      const wallForSave = getWallRenderValues();
      const additionalPlacements = [...changedTileIds]
        .filter(tileId => tileId !== tile.id)
        .map(tileId => {
          const changedTile = state.wall.tiles.find(item => item.id === tileId);
          return changedTile ? { tileId, ...tilePlacement(changedTile, wallForSave) } : null;
        })
        .filter(Boolean);
      await api(`/api/walls/${encodeURIComponent(state.wallId)}/tiles/${encodeURIComponent(tile.id)}/placement`, {
        method: "PUT",
        body: JSON.stringify({
          ...tilePlacement(tile, wallForSave),
          additionalPlacements
        })
      });
    } catch (error) {
      const restoredWall = getWallRenderValues();
      originalPlacements.forEach((placement, tileId) => {
        const originalTile = state.wall.tiles.find(item => item.id === tileId);
        if (!originalTile) return;
        if (freeform) previewTileGeometry(originalTile, placement, restoredWall);
        else previewTilePlacement(originalTile, placement, restoredWall);
      });
      syncPlacementFields(tile);
      console.error(error);
      showToast(t("messages.saveFailed"));
    }
  };
  const onPointerUp = () => finish(false);
  const onPointerCancel = () => finish(true);
  document.addEventListener("pointermove", update);
  document.addEventListener("pointerup", onPointerUp, { once: true });
  document.addEventListener("pointercancel", onPointerCancel, { once: true });
}

function syncPlacementFields(tile) {
  const view = getTileRenderValues(tile, getWallRenderValues());
  const values = {
    "tile-size-unit": view.sizeUnit,
    "tile-x": formatNumber(view.x),
    "tile-y": formatNumber(view.y),
    "tile-width": formatNumber(view.width),
    "tile-height": formatNumber(view.height)
  };
  Object.entries(values).forEach(([id, value]) => {
    const input = document.getElementById(id);
    if (input) input.value = value;
  });
}

function clamp(value, minimum, maximum) {
  return Math.min(Math.max(value, minimum), Math.max(minimum, maximum));
}

function isFreeformWall(wall = state.wall) {
  return String(wall?.layoutPreset || "").toLowerCase() === "freeform";
}

function geometryNumber(value, fallback) {
  if (value === "" || value == null) return fallback;
  const number = Number(value);
  return Number.isFinite(number) ? number : fallback;
}

function formatNumber(value, fallback = 0) {
  const number = geometryNumber(value, fallback);
  return String(Math.round(number * 100) / 100);
}

function defaultTileGeometry(tile, wall = state.wall) {
  const columns = Math.max(1, Number(wall?.gridColumns) || 1);
  const rows = Math.max(1, Number(wall?.gridRows) || 1);
  const column = clamp(Number(tile.column) || 0, 0, columns - 1);
  const row = clamp(Number(tile.row) || 0, 0, rows - 1);
  const columnSpan = clamp(Number(tile.columnSpan) || 1, 1, columns - column);
  const rowSpan = clamp(Number(tile.rowSpan) || 1, 1, rows - row);
  return {
    x: 100 * column / columns,
    y: 100 * row / rows,
    width: 100 * columnSpan / columns,
    height: 100 * rowSpan / rows
  };
}

function normalizeClientGeometry(tile) {
  const pixels = tile.sizeUnit === "pixels";
  const coordinateMaximum = pixels ? 10000 : 99;
  const sizeMaximum = pixels ? 10000 : 100;
  tile.x = clamp(geometryNumber(tile.x, 0), 0, coordinateMaximum);
  tile.y = clamp(geometryNumber(tile.y, 0), 0, coordinateMaximum);
  tile.width = clamp(geometryNumber(tile.width, 1), 1, sizeMaximum);
  tile.height = clamp(geometryNumber(tile.height, 1), 1, sizeMaximum);
  if (!pixels) {
    tile.width = clamp(tile.width, 1, Math.max(1, 100 - tile.x));
    tile.height = clamp(tile.height, 1, Math.max(1, 100 - tile.y));
  }
  return tile;
}

function renderInspector() {
  closeColorPicker();
  const inspector = document.getElementById("inspector");
  const shouldShow = state.mode === "edit";
  inspector.hidden = !shouldShow;
  if (!shouldShow) {
    inspector.innerHTML = "";
    return;
  }

  if (state.session.editorProtectionEnabled && !state.session.editor)
    state.inspectorTarget = "access";

  if (state.inspectorTarget === "access")
    inspector.innerHTML = renderAccessInspector();
  else if (state.inspectorTarget === "wall")
    inspector.innerHTML = renderWallInspector();
  else
    inspector.innerHTML = renderTileInspector();

  applyLabels(inspector);
  wireInspector(inspector);
}

function renderWallInspector() {
  const values = { ...state.wall, ...(state.wallDraft || {}) };
  const layoutOnlyDisabled = isFreeformWall(values) ? " disabled" : "";
  const activeTheme = getActiveThemePreset(values);
  return `<form id="wall-settings-form">
    <div class="inspector-head">
      <div><h2 data-i18n="labels.wallSettings"></h2><p data-i18n="app.tagline"></p></div>
      <button class="icon-button" type="button" data-action="close" data-label-key="actions.close" data-tooltip-key="tooltips.close">
        <span data-icon="close" aria-hidden="true"></span>
      </button>
    </div>
    <section class="inspector-section">
      <p class="section-label" data-i18n="labels.quickTheme"></p>
      <div class="theme-presets" role="group" data-i18n-aria-label="labels.quickTheme">
        ${themePresetOrder.map(key => {
          const preset = themePresets[key];
          const active = key === activeTheme ? " is-active" : "";
          return `<button class="theme-preset${active}" type="button" data-theme-preset="${key}" data-label-key="themes.${key}" aria-pressed="${key === activeTheme}">
            <span class="theme-swatch" aria-hidden="true" style="--theme-preview-bg:${preset.backgroundColor}; --theme-preview-panel:${preset.panelColor}; --theme-preview-accent:${preset.accentColor};"></span>
            <span class="theme-preset-label" data-i18n="themes.${key}"></span>
          </button>`;
        }).join("")}
      </div>
    </section>
    <section class="inspector-section">
      <p class="section-label" data-i18n="labels.page"></p>
      <div class="field-grid">
        <div class="field full"><label for="wall-name-input" data-i18n="labels.title"></label><input id="wall-name-input" name="name" value="${escapeAttribute(values.name)}"></div>
        <div class="field"><label for="wall-language" data-i18n="labels.language"></label><select id="wall-language" name="locale"><option value="en">English</option></select></div>
        <div class="field"><label for="wall-grid-columns" data-i18n="labels.gridColumns"></label><input id="wall-grid-columns" name="gridColumns" type="number" min="1" max="12" value="${escapeAttribute(values.gridColumns)}"${layoutOnlyDisabled}></div>
        <div class="field"><label for="wall-grid-rows" data-i18n="labels.gridRows"></label><input id="wall-grid-rows" name="gridRows" type="number" min="1" max="12" value="${escapeAttribute(values.gridRows)}"${layoutOnlyDisabled}></div>
        <div class="field"><label for="wall-video-spacing" data-i18n="labels.videoSpacing"></label><input id="wall-video-spacing" name="tileGap" type="number" min="0" max="64" value="${escapeAttribute(values.tileGap ?? 5)}"${layoutOnlyDisabled}></div>
        <div class="field"><label for="wall-background" data-i18n="labels.background"></label>${colorControlMarkup("wall-background", "backgroundColor", values.backgroundColor, "labels.background", "#0d1114")}</div>
        <div class="field"><label for="wall-foreground" data-i18n="labels.foreground"></label>${colorControlMarkup("wall-foreground", "foregroundColor", values.foregroundColor, "labels.foreground", "#edf3f4")}</div>
        <div class="field"><label for="wall-panel" data-i18n="labels.panel"></label>${colorControlMarkup("wall-panel", "panelColor", values.panelColor, "labels.panel", "#151a1f")}</div>
        <div class="field"><label for="wall-accent" data-i18n="labels.accent"></label>${colorControlMarkup("wall-accent", "accentColor", values.accentColor, "labels.accent", "#e7a346")}</div>
      </div>
    </section>
    <section class="inspector-section access-summary">
      <p class="section-label" data-i18n="labels.editorAccess"></p>
      <p class="access-status" data-i18n="${state.session.editorProtectionEnabled ? "messages.editorPinEnabled" : "messages.editorOpen"}"></p>
      <button class="icon-button" type="button" data-action="access" data-label-key="actions.manageAccess" data-tooltip-key="tooltips.manageAccess">
        <span data-icon="${state.session.editorProtectionEnabled ? "lock" : "unlock"}" aria-hidden="true"></span>
      </button>
    </section>
    <section class="inspector-section access-summary">
      <p class="section-label" data-i18n="labels.backup"></p>
      <p class="access-status" data-i18n="messages.backupHint"></p>
      <button class="icon-button" type="button" data-action="backup" data-label-key="actions.backup" data-tooltip-key="tooltips.backup">
        <span data-icon="download" aria-hidden="true"></span>
      </button>
    </section>
    <div class="form-actions">
      <button class="icon-button" type="button" data-action="close" data-label-key="actions.close" data-tooltip-key="tooltips.close"><span data-icon="close" aria-hidden="true"></span></button>
      <button class="icon-button primary" type="submit" data-label-key="actions.save" data-tooltip-key="tooltips.save"><span data-icon="check" aria-hidden="true"></span></button>
    </div>
  </form>`;
}

function renderAccessInspector() {
  const unlocked = state.session.editor;
  const protectedWall = state.session.editorProtectionEnabled;
  const heading = unlocked ? t("labels.editorAccess") : t("messages.editorUnlock");
  const description = unlocked
    ? (protectedWall ? t("messages.editorPinChange") : t("messages.editorOpenDescription"))
    : t("messages.editorPinRequired");
  const placeholder = unlocked && protectedWall ? t("messages.pinClearHint") : "";
  return `<form id="access-form">
    <div class="inspector-head">
      <div><h2>${escapeHtml(heading)}</h2><p>${escapeHtml(description)}</p></div>
      <button class="icon-button" type="button" data-action="close" data-label-key="actions.close" data-tooltip-key="tooltips.close">
        <span data-icon="close" aria-hidden="true"></span>
      </button>
    </div>
    <section class="inspector-section">
      <p class="section-label" data-i18n="labels.editorAccess"></p>
      <div class="field-grid">
        <div class="field full"><label for="editor-pin" data-i18n="${unlocked && protectedWall ? "labels.newEditorPin" : "labels.editorPin"}"></label><input id="editor-pin" name="pin" type="password" autocomplete="${unlocked ? "new-password" : "current-password"}" placeholder="${escapeAttribute(placeholder)}"></div>
      </div>
    </section>
    <div class="form-actions">
      <button class="icon-button" type="button" data-action="close" data-label-key="actions.close" data-tooltip-key="tooltips.close"><span data-icon="close" aria-hidden="true"></span></button>
      <button class="icon-button primary" type="submit" data-label-key="actions.unlock" data-tooltip-key="tooltips.unlock"><span data-icon="${unlocked ? "check" : "unlock"}" aria-hidden="true"></span></button>
    </div>
  </form>`;
}

function renderTileInspector() {
  const tile = state.wall.tiles.find(item => item.id === state.selectedTileId) || state.wall.tiles[0];
  if (!tile)
    return `<div class="inspector-head"><div><h2>${escapeHtml(t("status.waiting"))}</h2></div></div>`;

  state.selectedTileId = tile.id;
  const draft = state.tileDrafts.get(tile.id) || {};
  const value = name => Object.prototype.hasOwnProperty.call(draft, name) ? draft[name] : tile[name];
  const wall = getWallRenderValues();
  const freeform = isFreeformWall(wall);
  const tileView = getTileRenderValues(tile, wall, freeform);
  const geometryDisabled = freeform ? "" : " disabled";
  const geometryHint = freeform ? "messages.freeformGeometryHint" : "messages.layoutGeometryHint";
  const geometryUnit = tileView.sizeUnit === "pixels" ? "pixels" : "percent";
  const geometryStep = geometryUnit === "pixels" ? "1" : "0.1";
  const geometryMaximum = geometryUnit === "pixels" ? "10000" : "100";
  const title = String(value("title") ?? "");
  const tileIndex = state.wall.tiles.indexOf(tile) + 1;
  const testResult = state.testResults.get(tile.id);
  const testClass = testResult?.running ? "is-running" : testResult ? (testResult.success ? "is-success" : "is-error") : "";
  return `<form id="tile-form">
    <div class="inspector-head">
      <div><h2>${escapeHtml(title || t("labels.video"))}</h2><p>${escapeHtml(t("labels.stream"))} ${tileIndex} | ${escapeHtml(statusLabel(state.statusByTile.get(tile.id)?.state || "Idle"))}</p></div>
      <button class="icon-button" type="button" data-action="delete" data-label-key="actions.delete" data-tooltip-key="tooltips.delete"><span data-icon="trash" aria-hidden="true"></span></button>
    </div>
    <section class="inspector-section">
      <p class="section-label" data-i18n="labels.source"></p>
      <div class="field-grid">
        <div class="field"><label for="tile-protocol" data-i18n="labels.protocol"></label><select id="tile-protocol" name="protocol">${selectOptions([["rtsp", "options.rtsp"], ["rtp", "options.rtp"], ["udp", "options.udp"]], value("protocol"))}</select></div>
        <div class="field"><label for="tile-port" data-i18n="labels.port"></label><input id="tile-port" name="port" type="number" min="1" max="65535" value="${escapeAttribute(value("port"))}"></div>
        <div class="field full"><label for="tile-host" data-i18n="labels.host"></label><input id="tile-host" name="host" value="${escapeAttribute(value("host"))}"></div>
        <div class="field full"><label for="tile-path" data-i18n="labels.path"></label><input id="tile-path" name="path" value="${escapeAttribute(value("path"))}"></div>
        <div class="field"><label for="tile-username" data-i18n="labels.username"></label><input id="tile-username" name="username" value="${escapeAttribute(value("username"))}"></div>
        <div class="field"><label for="tile-password" data-i18n="labels.password"></label><input id="tile-password" name="password" type="password" value="${escapeAttribute(value("password") || "")}" placeholder="${escapeAttribute(tile.hasPassword ? t("messages.passwordKeep") : "")}"></div>
        <div class="field"><label for="tile-transport" data-i18n="labels.transport"></label><select id="tile-transport" name="transport">${selectOptions([["auto", "options.auto"], ["tcp", "options.tcp"], ["udp", "options.udp"], ["multicast", "options.multicast"], ["rtp", "options.rtp"], ["mpegTs", "options.mpegTs"]], value("transport"))}</select></div>
        <div class="field"><label for="tile-codec" data-i18n="labels.codec"></label><select id="tile-codec" name="codec">${selectOptions([["auto", "options.auto"], ["h264", "options.h264"], ["h265", "options.h265"], ["mjpeg", "options.mjpeg"]], value("codec"))}</select></div>
      </div>
      <div class="source-actions">
        <button id="tile-test" class="icon-button" type="button" data-action="test" data-label-key="actions.testSource" data-tooltip-key="tooltips.testSource"><span data-icon="link" aria-hidden="true"></span></button>
        <span id="tile-test-result" class="test-result ${testClass}" role="status" aria-live="polite">${escapeHtml(testResult ? sourceTestMessage(testResult) : t("messages.testNotRun"))}</span>
      </div>
    </section>
    <section class="inspector-section">
      <p class="section-label" data-i18n="labels.presentation"></p>
      <div class="field-grid">
        <div class="field full"><label for="tile-title" data-i18n="labels.title"></label><input id="tile-title" name="title" value="${escapeAttribute(value("title"))}"></div>
        <div class="field full"><label for="tile-size-unit" data-i18n="labels.sizeUnit"></label><select id="tile-size-unit" name="sizeUnit"${geometryDisabled}>${selectOptions([["pixels", "options.pixels"], ["percent", "options.percent"]], geometryUnit)}</select></div>
        <p class="field-hint full" data-i18n="${geometryHint}"></p>
        <div class="field"><label for="tile-x" data-i18n="labels.positionX"></label><input id="tile-x" name="x" type="number" min="0" max="${geometryUnit === "pixels" ? "10000" : "99"}" step="${geometryStep}" value="${escapeAttribute(formatNumber(tileView.x))}"${geometryDisabled}></div>
        <div class="field"><label for="tile-y" data-i18n="labels.positionY"></label><input id="tile-y" name="y" type="number" min="0" max="${geometryUnit === "pixels" ? "10000" : "99"}" step="${geometryStep}" value="${escapeAttribute(formatNumber(tileView.y))}"${geometryDisabled}></div>
        <div class="field"><label for="tile-width" data-i18n="labels.width"></label><input id="tile-width" name="width" type="number" min="1" max="${geometryMaximum}" step="${geometryStep}" value="${escapeAttribute(formatNumber(tileView.width))}"${geometryDisabled}></div>
        <div class="field"><label for="tile-height" data-i18n="labels.height"></label><input id="tile-height" name="height" type="number" min="1" max="${geometryMaximum}" step="${geometryStep}" value="${escapeAttribute(formatNumber(tileView.height))}"${geometryDisabled}></div>
        <div class="field full"><label for="tile-aspect" data-i18n="labels.aspect"></label><select id="tile-aspect" name="aspect">${selectOptions([["respectSource", "options.respectSource"], ["contain", "options.contain"], ["cover", "options.cover"], ["stretch", "options.stretch"]], value("aspect"))}</select></div>
      </div>
    </section>
    <section class="inspector-section">
      <p class="section-label" data-i18n="labels.frameTitle"></p>
      <div class="field-grid">
        <div class="field"><label for="tile-border-width" data-i18n="labels.borderSize"></label><input id="tile-border-width" name="borderWidth" type="number" min="0" max="24" value="${escapeAttribute(value("borderWidth"))}"></div>
        <div class="field"><label for="tile-border-color" data-i18n="labels.borderColor"></label>${colorControlMarkup("tile-border-color", "borderColor", value("borderColor"), "labels.borderColor", "#e7a346")}</div>
        <div class="field full check-row"><label for="tile-show-title" data-i18n="labels.showTitle"></label><input id="tile-show-title" name="showTitle" type="checkbox" ${value("showTitle") ? "checked" : ""}></div>
        <div class="field full"><label for="tile-title-background" data-i18n="labels.titleBackground"></label><select id="tile-title-background" name="titleBackground">${selectOptions([["borderColor", "options.borderColor"], ["customColor", "options.customColor"], ["transparent", "options.transparent"]], value("titleBackground"))}</select></div>
        <div class="field full"><label for="tile-title-background-color" data-i18n="labels.titleBackgroundColor"></label>${colorControlMarkup("tile-title-background-color", "titleBackgroundColor", value("titleBackgroundColor") || value("borderColor"), "labels.titleBackgroundColor", value("borderColor") || "#e7a346")}</div>
      </div>
    </section>
    <div class="form-actions">
      <button class="icon-button" type="button" data-action="close" data-label-key="actions.close" data-tooltip-key="tooltips.close"><span data-icon="close" aria-hidden="true"></span></button>
      <button class="icon-button primary" type="submit" data-label-key="actions.save" data-tooltip-key="tooltips.save"><span data-icon="check" aria-hidden="true"></span></button>
    </div>
  </form>`;
}

function captureEditorDraft(form) {
  if (form.id === "tile-form") {
    const tileId = state.selectedTileId;
    if (!tileId) return;

    syncColorControlValues(form);
    const draft = {};
    ["title", "protocol", "host", "port", "path", "username", "password", "transport", "codec",
      "sizeUnit", "x", "y", "width", "height", "aspect", "borderWidth", "borderColor", "showTitle",
      "titleBackground", "titleBackgroundColor"].forEach(name => {
      const field = form.elements.namedItem(name);
      if (!field) return;
      draft[name] = field.type === "checkbox" ? field.checked : field.value;
    });
    state.tileDrafts.set(tileId, draft);
    applyTileDraftPreview(tileId);
    return;
  }

  if (form.id === "wall-settings-form") {
    syncColorControlValues(form);
    const draft = {};
    ["name", "locale", "gridColumns", "gridRows", "tileGap", "backgroundColor", "foregroundColor", "panelColor", "accentColor"]
      .forEach(name => {
        const field = form.elements.namedItem(name);
        if (field) draft[name] = field.value;
      });
    state.wallDraft = draft;
    applyWallDraftPreview();
  }
}

function applyTileDraftPreview(tileId) {
  if (!state.wall || !tileId) return;
  const tile = state.wall.tiles.find(item => item.id === tileId);
  const tileElement = document.querySelector(`.stream-tile[data-tile-id="${escapeAttribute(tileId)}"]`);
  if (!tile || !tileElement) return;
  updateTileElement(tileElement, tile, getWallRenderValues());
  updateTileSelection();
}

function applyWallDraftPreview() {
  if (!state.wall) return;
  const wall = getWallRenderValues();
  applyWallSurface(wall);
  updateWallHeader(wall);
  patchCanvas(wall);
}

function discardEditorDrafts() {
  state.tileDrafts.clear();
  state.wallDraft = null;
}

function wireSubmitButton(form) {
  const button = form?.querySelector("button[type=submit]");
  if (!button || button.dataset.submitButtonWired === "true") return;
  button.dataset.submitButtonWired = "true";
  button.addEventListener("click", event => {
    if (button.disabled) {
      event.preventDefault();
      return;
    }
    event.preventDefault();
    form.requestSubmit(button);
  });
}

function wireInspector(inspector) {
  inspector.querySelectorAll("[data-action=close]").forEach(button => {
    button.addEventListener("click", () => setMode("wall"));
  });
  inspector.querySelector("[data-action=access]")?.addEventListener("click", () => {
    state.inspectorTarget = "access";
    render({ preserveCanvas: true });
  });
  inspector.querySelector("[data-action=delete]")?.addEventListener("click", deleteSelectedTile);
  inspector.querySelector("[data-action=test]")?.addEventListener("click", testSelectedTile);
  inspector.querySelector("[data-action=backup]")?.addEventListener("click", createAndDownloadBackup);
  inspector.querySelectorAll("[data-theme-preset]").forEach(button => {
    button.addEventListener("click", () => applyThemePreset(button.dataset.themePreset));
  });
  const tileForm = inspector.querySelector("#tile-form");
  ["input", "change"].forEach(eventName => tileForm?.addEventListener(eventName, event => {
    captureEditorDraft(event.currentTarget);
  }));
  tileForm?.elements.namedItem("sizeUnit")?.addEventListener("change", event => {
    updateGeometryInputConstraints(tileForm, event.currentTarget.value);
  });
  tileForm?.addEventListener("submit", async event => {
    event.preventDefault();
    captureEditorDraft(event.currentTarget);
    const button = event.currentTarget.querySelector("button[type=submit]");
    if (button?.disabled) return;
    if (button) button.disabled = true;
    try {
      await saveTile(event.currentTarget);
    } finally {
      if (button?.isConnected) button.disabled = false;
    }
  });
  wireSubmitButton(tileForm);
  const wallForm = inspector.querySelector("#wall-settings-form");
  ["input", "change"].forEach(eventName => wallForm?.addEventListener(eventName, event => {
    captureEditorDraft(event.currentTarget);
  }));
  wallForm?.addEventListener("submit", async event => {
    event.preventDefault();
    captureEditorDraft(event.currentTarget);
    const button = event.currentTarget.querySelector("button[type=submit]");
    if (button?.disabled) return;
    if (button) button.disabled = true;
    try {
      await saveWallSettings(event.currentTarget);
    } finally {
      if (button?.isConnected) button.disabled = false;
    }
  });
  wireSubmitButton(wallForm);
  wireColorPickers(inspector);
  const accessForm = inspector.querySelector("#access-form");
  accessForm?.addEventListener("submit", async event => {
    event.preventDefault();
    const button = event.currentTarget.querySelector("button[type=submit]");
    if (button?.disabled) return;
    if (button) button.disabled = true;
    try {
      await saveAccessSettings(new FormData(event.currentTarget));
    } finally {
      if (button?.isConnected) button.disabled = false;
    }
  });
  wireSubmitButton(accessForm);
  inspector.querySelector("#wall-language")?.addEventListener("change", async event => {
    const locale = event.currentTarget.value;
    localStorage.setItem("stream-wall-locale", locale);
    captureEditorDraft(event.currentTarget.form);
    await loadLocale(locale);
    render({ preserveCanvas: true });
  });
}

function updateGeometryInputConstraints(form, unit) {
  const pixels = unit === "pixels";
  const coordinateMaximum = pixels ? "10000" : "99";
  const sizeMaximum = pixels ? "10000" : "100";
  const step = pixels ? "1" : "0.1";
  ["x", "y"].forEach(name => {
    const input = form?.elements?.namedItem(name);
    if (!input) return;
    input.min = "0";
    input.max = coordinateMaximum;
    input.step = step;
  });
  ["width", "height"].forEach(name => {
    const input = form?.elements?.namedItem(name);
    if (!input) return;
    input.min = "1";
    input.max = sizeMaximum;
    input.step = step;
  });
}

function colorControlMarkup(id, name, value, labelKey, fallback) {
  const color = normalizeColorHex(value, fallback);
  return `<div class="color-control" data-color-control data-color-fallback="${escapeAttribute(fallback)}">
    <button id="${escapeAttribute(id)}" class="color-trigger" type="button" data-color-trigger data-label-key="${escapeAttribute(labelKey)}" aria-haspopup="dialog" aria-controls="color-picker" aria-expanded="false">
      <span class="color-trigger-swatch" aria-hidden="true" style="--color-value:${color}"></span>
      <span class="color-trigger-value">${escapeHtml(color)}</span>
    </button>
    <input id="${escapeAttribute(id)}-value" name="${escapeAttribute(name)}" type="hidden" data-color-input value="${escapeAttribute(color)}">
  </div>`;
}

function wireColorPickers(root) {
  syncColorControlValues(root);
  root.querySelectorAll("[data-color-control]").forEach(control => {
    const trigger = control.querySelector("[data-color-trigger]");
    if (!trigger || trigger.dataset.colorPickerWired === "true") return;
    trigger.dataset.colorPickerWired = "true";
    trigger.addEventListener("click", event => {
      event.preventDefault();
      event.stopPropagation();
      if (activeColorPicker?.control === control) {
        closeColorPicker();
      } else {
        openColorPicker(control);
      }
    });
  });
}

function syncColorControlValues(root) {
  root?.querySelectorAll("[data-color-control]").forEach(control => {
    const source = control.querySelector("[data-color-input]");
    if (!source) return;
    const color = normalizeColorHex(source.value, control.dataset.colorFallback || "#000000");
    source.value = color;
    control.querySelector(".color-trigger-swatch")?.style.setProperty("--color-value", color);
    const value = control.querySelector(".color-trigger-value");
    if (value) value.textContent = color;
  });
}

function ensureColorPicker() {
  if (colorPickerElement) return colorPickerElement;

  const picker = document.createElement("div");
  picker.id = "color-picker";
  picker.className = "color-picker-popover";
  picker.setAttribute("role", "dialog");
  picker.setAttribute("aria-labelledby", "color-picker-title");
  picker.hidden = true;
  picker.innerHTML = `
    <div class="color-picker-head">
      <strong id="color-picker-title" data-i18n="labels.colorPicker"></strong>
      <button class="icon-button" type="button" data-color-close data-label-key="actions.close" data-tooltip-key="tooltips.close">
        <span data-icon="close" aria-hidden="true"></span>
      </button>
    </div>
    <div class="color-picker-saturation" data-color-saturation role="slider" tabindex="0" data-i18n-aria-label="labels.saturation" aria-valuemin="0" aria-valuemax="100" aria-valuenow="100">
      <span class="color-picker-saturation-thumb" data-color-saturation-thumb aria-hidden="true"></span>
    </div>
    <label class="color-picker-hue">
      <span data-i18n="labels.hue"></span>
      <input type="range" data-color-hue min="0" max="360" step="1" value="30" data-i18n-aria-label="labels.hue">
    </label>
    <div class="color-picker-footer">
      <span class="color-picker-preview" data-color-preview aria-hidden="true"></span>
      <label class="color-picker-hex">
        <span data-i18n="labels.hexColor"></span>
        <input type="text" data-color-hex maxlength="7" inputmode="text" autocapitalize="off" autocomplete="off" spellcheck="false">
      </label>
    </div>`;
  document.body.appendChild(picker);
  colorPickerElement = picker;

  applyLabels(picker);
  picker.querySelector("[data-color-close]").addEventListener("click", closeColorPicker);

  const saturation = picker.querySelector("[data-color-saturation]");
  const updateFromPointer = event => {
    if (!activeColorPicker || !saturation.hasPointerCapture?.(event.pointerId)) return;
    const bounds = saturation.getBoundingClientRect();
    activeColorPicker.saturation = clamp((event.clientX - bounds.left) / bounds.width, 0, 1);
    activeColorPicker.value = clamp(1 - (event.clientY - bounds.top) / bounds.height, 0, 1);
    syncColorPicker(true);
  };
  saturation.addEventListener("pointerdown", event => {
    if (!activeColorPicker || (event.button !== undefined && event.button !== 0)) return;
    event.preventDefault();
    saturation.setPointerCapture?.(event.pointerId);
    const bounds = saturation.getBoundingClientRect();
    activeColorPicker.saturation = clamp((event.clientX - bounds.left) / bounds.width, 0, 1);
    activeColorPicker.value = clamp(1 - (event.clientY - bounds.top) / bounds.height, 0, 1);
    syncColorPicker(true);
  });
  saturation.addEventListener("pointermove", updateFromPointer);
  saturation.addEventListener("pointerup", event => saturation.releasePointerCapture?.(event.pointerId));
  saturation.addEventListener("pointercancel", event => saturation.releasePointerCapture?.(event.pointerId));
  saturation.addEventListener("keydown", event => {
    if (!activeColorPicker) return;
    const step = event.shiftKey ? 0.1 : 0.02;
    let handled = true;
    if (event.key === "ArrowLeft") activeColorPicker.saturation -= step;
    else if (event.key === "ArrowRight") activeColorPicker.saturation += step;
    else if (event.key === "ArrowUp") activeColorPicker.value += step;
    else if (event.key === "ArrowDown") activeColorPicker.value -= step;
    else handled = false;
    if (handled) {
      event.preventDefault();
      activeColorPicker.saturation = clamp(activeColorPicker.saturation, 0, 1);
      activeColorPicker.value = clamp(activeColorPicker.value, 0, 1);
      syncColorPicker(true);
    }
  });

  picker.querySelector("[data-color-hue]").addEventListener("input", event => {
    if (!activeColorPicker) return;
    activeColorPicker.hue = Number(event.currentTarget.value) || 0;
    syncColorPicker(true);
  });
  picker.querySelector("[data-color-hex]").addEventListener("input", event => {
    if (!activeColorPicker) return;
    const parsed = parseHexColor(event.currentTarget.value);
    event.currentTarget.classList.toggle("is-invalid", !parsed);
    if (!parsed) return;
    const hsv = rgbToHsv(parsed);
    activeColorPicker.hue = hsv.h;
    activeColorPicker.saturation = hsv.s;
    activeColorPicker.value = hsv.v;
    syncColorPicker(true);
  });
  picker.querySelector("[data-color-hex]").addEventListener("blur", () => {
    if (!activeColorPicker) return;
    syncColorPicker(false);
  });

  document.addEventListener("pointerdown", event => {
    if (!activeColorPicker || picker.hidden) return;
    if (picker.contains(event.target) || activeColorPicker.trigger.contains(event.target)) return;
    closeColorPicker();
  }, true);
  document.addEventListener("keydown", event => {
    if (event.key === "Escape" && activeColorPicker) {
      event.preventDefault();
      closeColorPicker();
    }
  });
  window.addEventListener("resize", positionColorPicker);
  window.addEventListener("scroll", positionColorPicker, true);
  return picker;
}

function openColorPicker(control) {
  closeColorPicker();
  const source = control.querySelector("[data-color-input]");
  const trigger = control.querySelector("[data-color-trigger]");
  if (!source || !trigger) return;

  const hsv = rgbToHsv(parseHexColor(source.value) || { r: 0, g: 0, b: 0 });
  const picker = ensureColorPicker();
  activeColorPicker = {
    control,
    source,
    trigger,
    hue: hsv.h,
    saturation: hsv.s,
    value: hsv.v
  };
  applyLabels(picker);
  picker.hidden = false;
  picker.style.visibility = "hidden";
  trigger.setAttribute("aria-expanded", "true");
  syncColorPicker(false);
  positionColorPicker();
  picker.style.visibility = "";
}

function closeColorPicker() {
  if (!activeColorPicker) return;
  activeColorPicker.trigger?.setAttribute("aria-expanded", "false");
  if (colorPickerElement) {
    colorPickerElement.hidden = true;
    colorPickerElement.style.visibility = "";
  }
  activeColorPicker = null;
}

function positionColorPicker() {
  if (!activeColorPicker || !colorPickerElement || colorPickerElement.hidden) return;
  const triggerBounds = activeColorPicker.trigger.getBoundingClientRect();
  const pickerBounds = colorPickerElement.getBoundingClientRect();
  const margin = 8;
  const gap = 6;
  let left = triggerBounds.left;
  let top = triggerBounds.bottom + gap;
  if (left + pickerBounds.width > window.innerWidth - margin)
    left = window.innerWidth - pickerBounds.width - margin;
  left = Math.max(margin, left);
  if (top + pickerBounds.height > window.innerHeight - margin && triggerBounds.top - pickerBounds.height - gap >= margin)
    top = triggerBounds.top - pickerBounds.height - gap;
  top = Math.max(margin, Math.min(top, window.innerHeight - pickerBounds.height - margin));
  colorPickerElement.style.left = `${Math.round(left)}px`;
  colorPickerElement.style.top = `${Math.round(top)}px`;
}

function syncColorPicker(dispatchInput) {
  if (!activeColorPicker || !colorPickerElement) return;
  const color = hsvToHex(activeColorPicker.hue, activeColorPicker.saturation, activeColorPicker.value);
  const saturation = colorPickerElement.querySelector("[data-color-saturation]");
  const thumb = colorPickerElement.querySelector("[data-color-saturation-thumb]");
  const hue = colorPickerElement.querySelector("[data-color-hue]");
  const hex = colorPickerElement.querySelector("[data-color-hex]");
  const preview = colorPickerElement.querySelector("[data-color-preview]");
  const swatch = activeColorPicker.trigger.querySelector(".color-trigger-swatch");
  const value = activeColorPicker.trigger.querySelector(".color-trigger-value");

  activeColorPicker.source.value = color;
  swatch.style.setProperty("--color-value", color);
  value.textContent = color;
  saturation.style.background = `linear-gradient(to top, #000, transparent), linear-gradient(to right, #fff, hsl(${activeColorPicker.hue}, 100%, 50%))`;
  saturation.setAttribute("aria-valuenow", String(Math.round(activeColorPicker.saturation * 100)));
  saturation.setAttribute("aria-valuetext", color);
  thumb.style.left = `${activeColorPicker.saturation * 100}%`;
  thumb.style.top = `${(1 - activeColorPicker.value) * 100}%`;
  hue.value = String(Math.round(activeColorPicker.hue));
  hex.value = color;
  hex.classList.remove("is-invalid");
  preview.style.setProperty("--color-value", color);
  if (dispatchInput)
    activeColorPicker.source.dispatchEvent(new Event("input", { bubbles: true }));
}

function normalizeColorHex(value, fallback) {
  const parsed = parseHexColor(value);
  return parsed ? rgbToHex(parsed) : fallback;
}

function parseHexColor(value) {
  let hex = String(value || "").trim().replace(/^#/, "");
  if (hex.length === 3) hex = hex.split("").map(character => character + character).join("");
  if (!/^[0-9a-f]{6}$/i.test(hex)) return null;
  return {
    r: Number.parseInt(hex.slice(0, 2), 16),
    g: Number.parseInt(hex.slice(2, 4), 16),
    b: Number.parseInt(hex.slice(4, 6), 16)
  };
}

function rgbToHsv({ r, g, b }) {
  const red = r / 255;
  const green = g / 255;
  const blue = b / 255;
  const maximum = Math.max(red, green, blue);
  const minimum = Math.min(red, green, blue);
  const delta = maximum - minimum;
  let hue = 0;
  if (delta > 0) {
    if (maximum === red) hue = 60 * (((green - blue) / delta) % 6);
    else if (maximum === green) hue = 60 * ((blue - red) / delta + 2);
    else hue = 60 * ((red - green) / delta + 4);
    if (hue < 0) hue += 360;
  }
  return {
    h: hue,
    s: maximum === 0 ? 0 : delta / maximum,
    v: maximum
  };
}

function hsvToHex(hue, saturation, value) {
  const h = ((hue % 360) + 360) % 360;
  const sector = h / 60;
  const index = Math.floor(sector);
  const fraction = sector - index;
  const p = value * (1 - saturation);
  const q = value * (1 - saturation * fraction);
  const t = value * (1 - saturation * (1 - fraction));
  const channels = [
    [value, t, p],
    [q, value, p],
    [p, value, t],
    [p, q, value],
    [t, p, value],
    [value, p, q]
  ][index % 6];
  return rgbToHex({
    r: Math.round(channels[0] * 255),
    g: Math.round(channels[1] * 255),
    b: Math.round(channels[2] * 255)
  });
}

function rgbToHex({ r, g, b }) {
  return `#${[r, g, b].map(channel => Math.round(channel).toString(16).padStart(2, "0")).join("")}`;
}

function readFormValue(form, name, fallback = "") {
  const field = form?.elements?.namedItem?.(name);
  if (field)
    return field.type === "checkbox" ? field.checked : field.value;

  if (typeof form?.get === "function") {
    const value = form.get(name);
    return value == null ? fallback : value;
  }

  return fallback;
}

function readFormNumber(form, name, fallback) {
  const raw = readFormValue(form, name, fallback);
  if (raw === "" || raw == null) return fallback;
  const value = Number(raw);
  return Number.isFinite(value) ? value : fallback;
}

async function saveTile(form) {
  const tile = state.wall.tiles.find(item => item.id === state.selectedTileId);
  if (!tile) return;

  syncColorControlValues(form);
  const wall = getWallRenderValues();
  const freeform = isFreeformWall(wall);
  const tileView = getTileRenderValues(tile, wall, freeform);
  const host = String(readFormValue(form, "host", tile.host) || "").trim();
  const borderColor = normalizeColorHex(readFormValue(form, "borderColor", tile.borderColor),
    safeColor(tile.borderColor, "#e7a346"));
  const payload = {
    ...tile,
    enabled: Boolean(host),
    title: String(readFormValue(form, "title", tile.title) || ""),
    protocol: String(readFormValue(form, "protocol", tile.protocol) || "rtsp"),
    host,
    port: readFormNumber(form, "port", tile.port || 554),
    path: String(readFormValue(form, "path", tile.path) || ""),
    username: String(readFormValue(form, "username", tile.username) || ""),
    transport: String(readFormValue(form, "transport", tile.transport) || "auto"),
    codec: String(readFormValue(form, "codec", tile.codec) || "auto"),
    column: readFormNumber(form, "column", tile.column || 0),
    row: readFormNumber(form, "row", tile.row || 0),
    columnSpan: readFormNumber(form, "columnSpan", tile.columnSpan || 1),
    rowSpan: readFormNumber(form, "rowSpan", tile.rowSpan || 1),
    sizeUnit: freeform ? String(readFormValue(form, "sizeUnit", tileView.sizeUnit) || tileView.sizeUnit) : tileView.sizeUnit,
    x: freeform ? readFormNumber(form, "x", tileView.x) : tileView.x,
    y: freeform ? readFormNumber(form, "y", tileView.y) : tileView.y,
    width: freeform ? readFormNumber(form, "width", tileView.width) : tileView.width,
    height: freeform ? readFormNumber(form, "height", tileView.height) : tileView.height,
    aspect: String(readFormValue(form, "aspect", tile.aspect) || "respectSource"),
    borderWidth: readFormNumber(form, "borderWidth", tile.borderWidth || 0),
    borderColor,
    showTitle: Boolean(readFormValue(form, "showTitle", tile.showTitle)),
    titleBackground: String(readFormValue(form, "titleBackground", tile.titleBackground) || "borderColor"),
    titleBackgroundColor: normalizeColorHex(
      readFormValue(form, "titleBackgroundColor", tile.titleBackgroundColor),
      borderColor)
  };
  const password = String(readFormValue(form, "password", "") || "");
  if (password) payload.password = password;

  const previousSourceKey = sourceConfigKey(tile);
  const nextSourceKey = sourceConfigKey(payload);
  try {
    await api(`/api/walls/${encodeURIComponent(state.wallId)}/tiles/${encodeURIComponent(tile.id)}`, {
      method: "PUT",
      body: JSON.stringify(payload)
    });
    state.tileDrafts.delete(tile.id);
    await loadWall({ renderPage: false, refresh: false });
    state.mode = "edit";
    state.inspectorTarget = "tile";
    render({ preserveCanvas: true });
    if (previousSourceKey !== nextSourceKey)
      restartTilePlayer(tile.id);
    await refreshStatuses();
    showToast(t("messages.saved"));
  } catch (error) {
    console.error(error);
    showToast(errorMessage(error, t("messages.saveFailed")));
  }
}

async function applyThemePreset(presetKey) {
  const preset = themePresets[presetKey];
  const form = document.getElementById("wall-settings-form");
  if (!preset || !form) return;

  const buttons = [...form.querySelectorAll("[data-theme-preset]")];
  buttons.forEach(button => { button.disabled = true; });
  ["backgroundColor", "foregroundColor", "panelColor", "accentColor"].forEach(name => {
    const input = form.elements.namedItem(name);
    if (input) input.value = preset[name];
  });
  captureEditorDraft(form);

  try {
    await saveWallSettings(form, "messages.themeApplied");
  } finally {
    buttons.forEach(button => { button.disabled = false; });
  }
}

async function saveWallSettings(form, successMessage = "messages.saved") {
  if (!state.wall) return;
  syncColorControlValues(form);
  const nextWall = {
    ...state.wall,
    tiles: state.wall.tiles.map(tile => ({ ...tile }))
  };
  nextWall.name = String(readFormValue(form, "name", nextWall.name) || t("app.name"));
  nextWall.locale = String(readFormValue(form, "locale", nextWall.locale) || "en");
  const nextColumns = clamp(readFormNumber(form, "gridColumns", nextWall.gridColumns), 1, 12);
  const nextRows = clamp(readFormNumber(form, "gridRows", nextWall.gridRows), 1, 12);
  if (nextColumns !== nextWall.gridColumns || nextRows !== nextWall.gridRows) {
    nextWall.layoutPreset = "custom";
    nextWall.tiles.forEach(tile => normalizeClientPlacement(tile, nextColumns, nextRows));
  }
  nextWall.gridColumns = nextColumns;
  nextWall.gridRows = nextRows;
  nextWall.tileGap = clamp(readFormNumber(form, "tileGap", nextWall.tileGap ?? 5), 0, 64);
  nextWall.backgroundColor = normalizeColorHex(readFormValue(form, "backgroundColor", nextWall.backgroundColor), "#0d1114");
  nextWall.foregroundColor = normalizeColorHex(readFormValue(form, "foregroundColor", nextWall.foregroundColor), "#edf3f4");
  nextWall.panelColor = normalizeColorHex(readFormValue(form, "panelColor", nextWall.panelColor), "#151a1f");
  nextWall.accentColor = normalizeColorHex(readFormValue(form, "accentColor", nextWall.accentColor), "#e7a346");
  try {
    state.wall = await api(`/api/walls/${encodeURIComponent(state.wallId)}`, {
      method: "PUT",
      body: JSON.stringify(nextWall)
    });
    state.wallDraft = null;
    await loadLocale(state.wall.locale || "en");
    render({ preserveCanvas: true });
    await refreshStatuses();
    showToast(t(successMessage));
  } catch (error) {
    console.error(error);
    showToast(errorMessage(error, t("messages.saveFailed")));
  }
}

async function createAndDownloadBackup(event) {
  const button = event.currentTarget;
  button.disabled = true;
  try {
    const backup = await api(`/api/walls/${encodeURIComponent(state.wallId)}/backups`, { method: "POST" });
    const response = await fetch(`/api/walls/${encodeURIComponent(state.wallId)}/backups/${encodeURIComponent(backup.fileName)}`);
    if (!response.ok) throw await createApiError(response);
    const blob = await response.blob();
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = backup.fileName;
    link.click();
    URL.revokeObjectURL(url);
    showToast(t("messages.backupCreated"));
  } catch (error) {
    console.error(error);
    showToast(errorMessage(error, t("messages.backupFailed")));
  } finally {
    button.disabled = false;
  }
}

async function saveAccessSettings(form) {
  const pin = String(form.get("pin") || "").trim();
  try {
    if (!state.session.editor) {
      const session = await api(`/api/walls/${encodeURIComponent(state.wallId)}/session/editor`, {
        method: "POST",
        body: JSON.stringify({ pin })
      });
      state.session = session;
      state.inspectorTarget = "wall";
      render({ preserveCanvas: true });
      showToast(t("messages.editorUnlocked"));
      return;
    }

    state.session = await api(`/api/walls/${encodeURIComponent(state.wallId)}/access`, {
      method: "PUT",
      body: JSON.stringify({ editorPin: pin })
    });
    state.inspectorTarget = "wall";
    render({ preserveCanvas: true });
    showToast(t("messages.accessSaved"));
  } catch (error) {
    console.error(error);
    showToast(errorMessage(error, t("messages.saveFailed")));
  }
}

async function lockEditor() {
  try {
    await api(`/api/walls/${encodeURIComponent(state.wallId)}/session/editor`, { method: "DELETE" });
    await loadSession();
    state.mode = "wall";
    state.inspectorTarget = "access";
    render({ preserveCanvas: true });
    showToast(t("messages.editorLocked"));
  } catch (error) {
    console.error(error);
    showToast(errorMessage(error, t("messages.saveFailed")));
  }
}

async function testSelectedTile(event) {
  const tileId = state.selectedTileId;
  const tile = state.wall?.tiles.find(item => item.id === tileId);
  if (!tile) return;
  const button = event.currentTarget;
  button.disabled = true;
  state.testResults.set(tileId, { running: true, message: t("messages.testRunning") });
  const resultElement = document.getElementById("tile-test-result");
  if (resultElement) {
    resultElement.className = "test-result is-running";
    resultElement.textContent = t("messages.testRunning");
  }

  const showResult = result => {
    const currentResultElement = document.getElementById("tile-test-result");
    if (currentResultElement) {
      currentResultElement.className = `test-result ${result.success ? "is-success" : "is-error"}`;
      currentResultElement.textContent = sourceTestMessage(result);
    }
  };

  try {
    const result = await api(`/api/walls/${encodeURIComponent(state.wallId)}/tiles/${encodeURIComponent(tileId)}/test`, {
      method: "POST",
      body: JSON.stringify({ tile: getTileTestPayload(tile) })
    });
    state.testResults.set(tileId, { ...result, running: false });
    showResult(result);
    if (result.success)
      showToast(t("messages.testPassed"));
  } catch (error) {
    console.error(error);
    const result = { success: false, message: t("messages.testFailed") };
    state.testResults.set(tileId, result);
    showResult(result);
    showToast(errorMessage(error, t("messages.testFailed")));
  } finally {
    document.getElementById("tile-test")?.removeAttribute("disabled");
  }
}

function getTileTestPayload(tile) {
  const draft = state.tileDrafts.get(tile.id) || {};
  const value = name => Object.prototype.hasOwnProperty.call(draft, name) ? draft[name] : tile[name];
  const host = String(value("host") || "").trim();
  const rawPort = value("port");
  const parsedPort = rawPort === "" || rawPort == null ? tile.port : Number(rawPort);
  const password = String(value("password") || "");
  const payload = {
    ...tile,
    enabled: Boolean(host),
    protocol: String(value("protocol") || "rtsp"),
    host,
    port: Number.isFinite(parsedPort) ? parsedPort : tile.port,
    path: String(value("path") || ""),
    username: String(value("username") || ""),
    transport: String(value("transport") || "auto"),
    codec: String(value("codec") || "auto")
  };
  if (password) payload.password = password;
  return payload;
}

async function addVideo() {
  const index = state.wall?.tiles.length || 0;
  const defaultX = (index % 2) * 50;
  const defaultY = (Math.floor(index / 2) % 2) * 50;
  const tile = {
    title: `${t("labels.video")} ${index + 1}`,
    enabled: false,
    showTitle: true,
    protocol: "rtsp",
    host: "",
    port: 554,
    path: "",
    username: "",
    transport: "auto",
    codec: "auto",
    column: index % 2,
    row: Math.floor(index / 2) % 2,
    columnSpan: 1,
    rowSpan: 1,
    sizeUnit: "percent",
    x: defaultX,
    y: defaultY,
    width: 50,
    height: 50,
    aspect: "respectSource",
    borderWidth: 2,
    borderColor: ["#e7a346", "#5dbe91", "#7997e3", "#dc8068"][index % 4],
    titleBackgroundColor: ["#e7a346", "#5dbe91", "#7997e3", "#dc8068"][index % 4],
    titleBackground: "borderColor"
  };

  try {
    const added = await api(`/api/walls/${encodeURIComponent(state.wallId)}/tiles`, {
      method: "POST",
      body: JSON.stringify(tile)
    });
    state.selectedTileId = added.id;
    state.inspectorTarget = "tile";
    await loadWall({ renderPage: false, refresh: false });
    state.mode = "edit";
    render();
    await refreshStatuses();
    showToast(t("messages.added"));
  } catch (error) {
    console.error(error);
    showToast(t("messages.saveFailed"));
  }
}

async function deleteSelectedTile() {
  if (!state.selectedTileId || !window.confirm(t("messages.deleteConfirm"))) return;
  try {
    await api(`/api/walls/${encodeURIComponent(state.wallId)}/tiles/${encodeURIComponent(state.selectedTileId)}`, {
      method: "DELETE"
    });
    state.selectedTileId = null;
    await loadWall({ renderPage: false, refresh: false });
    state.mode = "edit";
    render();
    await refreshStatuses();
    showToast(t("messages.deleted"));
  } catch (error) {
    console.error(error);
    showToast(t("messages.saveFailed"));
  }
}

async function applyLayout(layout) {
  const preset = layoutPresets[layout];
  if (!preset || !state.wall) return;
  const nextWall = {
    ...state.wall,
    tiles: state.wall.tiles.map(tile => ({ ...tile }))
  };
  nextWall.layoutPreset = layout;
  if (!preset.freeform) {
    nextWall.gridColumns = preset.columns;
    nextWall.gridRows = preset.rows;
    if (preset.positions) {
      preset.positions.forEach((position, index) => {
        if (!nextWall.tiles[index]) return;
        Object.assign(nextWall.tiles[index], position);
      });
    } else {
      nextWall.tiles.forEach(tile => normalizeClientPlacement(tile, preset.columns, preset.rows));
    }
  }

  try {
    state.wall = await api(`/api/walls/${encodeURIComponent(state.wallId)}`, {
      method: "PUT",
      body: JSON.stringify(nextWall)
    });
    state.mode = "edit";
    render({ preserveCanvas: true });
    await refreshStatuses();
    showToast(t("messages.saved"));
  } catch (error) {
    console.error(error);
    showToast(t("messages.saveFailed"));
  }
}

async function saveLayoutPreset() {
  if (!state.wall) return;
  const name = window.prompt(t("messages.presetNamePrompt"), t("messages.presetNameDefault"));
  if (!name?.trim()) return;
  try {
    await api(`/api/walls/${encodeURIComponent(state.wallId)}/presets`, {
      method: "POST",
      body: JSON.stringify({ name: name.trim() })
    });
    await loadWall({ renderPage: false, refresh: false });
    state.mode = "edit";
    render({ preserveCanvas: true });
    await refreshStatuses();
    showToast(t("messages.presetSaved"));
  } catch (error) {
    console.error(error);
    showToast(t("messages.saveFailed"));
  }
}

async function applySavedLayout(presetId) {
  try {
    state.wall = await api(`/api/walls/${encodeURIComponent(state.wallId)}/presets/${encodeURIComponent(presetId)}/apply`, {
      method: "POST",
      body: "{}"
    });
    state.mode = "edit";
    render({ preserveCanvas: true });
    await refreshStatuses();
    showToast(t("messages.presetApplied"));
  } catch (error) {
    console.error(error);
    showToast(t("messages.saveFailed"));
  }
}

async function deleteSavedLayout(presetId) {
  if (!window.confirm(t("messages.presetDeleteConfirm"))) return;
  try {
    await api(`/api/walls/${encodeURIComponent(state.wallId)}/presets/${encodeURIComponent(presetId)}`, {
      method: "DELETE"
    });
    await loadWall({ renderPage: false, refresh: false });
    state.mode = "edit";
    render({ preserveCanvas: true });
    await refreshStatuses();
    showToast(t("messages.presetDeleted"));
  } catch (error) {
    console.error(error);
    showToast(t("messages.saveFailed"));
  }
}

async function exportSavedLayouts() {
  try {
    const response = await fetch(`/api/walls/${encodeURIComponent(state.wallId)}/presets/export`);
    if (!response.ok) throw await createApiError(response);
    const blob = await response.blob();
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `${state.wallId}-layouts.json`;
    link.click();
    URL.revokeObjectURL(url);
    showToast(t("messages.presetExported"));
  } catch (error) {
    console.error(error);
    showToast(errorMessage(error, t("messages.saveFailed")));
  }
}

async function importSavedLayouts(event) {
  const input = event.currentTarget;
  const file = input.files?.[0];
  input.value = "";
  if (!file) return;

  try {
    const payload = JSON.parse(await file.text());
    const presets = Array.isArray(payload) ? payload : payload.presets;
    if (!Array.isArray(presets) || presets.length === 0)
      throw new Error(t("messages.presetImportEmpty"));

    const conflictMode = document.getElementById("preset-conflict-mode")?.value || "rename";
    const wall = await api(`/api/walls/${encodeURIComponent(state.wallId)}/presets/import`, {
      method: "POST",
      body: JSON.stringify({ schemaVersion: 1, conflictMode, presets })
    });
    state.wall = wall;
    state.mode = "edit";
    render();
    showToast(t("messages.presetImported", { count: presets.length }));
  } catch (error) {
    console.error(error);
    showToast(errorMessage(error, t("messages.presetImportFailed")));
  }
}

function normalizeClientPlacement(tile, columns, rows) {
  tile.column = clamp(Number(tile.column) || 0, 0, columns - 1);
  tile.row = clamp(Number(tile.row) || 0, 0, rows - 1);
  tile.columnSpan = clamp(Number(tile.columnSpan) || 1, 1, columns - tile.column);
  tile.rowSpan = clamp(Number(tile.rowSpan) || 1, 1, rows - tile.row);
}

const layoutPresets = {
  "1x1": {
    columns: 1, rows: 1, visibleCount: 1, labelKey: "layouts.one", tooltipKey: "tooltips.layoutOne", icon: "layout-one",
    positions: [{ column: 0, row: 0, columnSpan: 1, rowSpan: 1 }]
  },
  "2x2": { columns: 2, rows: 2, visibleCount: 4, labelKey: "layouts.two", tooltipKey: "tooltips.layoutTwo", icon: "layout-two", positions: [
    { column: 0, row: 0, columnSpan: 1, rowSpan: 1 },
    { column: 1, row: 0, columnSpan: 1, rowSpan: 1 },
    { column: 0, row: 1, columnSpan: 1, rowSpan: 1 },
    { column: 1, row: 1, columnSpan: 1, rowSpan: 1 }
  ] },
  "3x3": { columns: 3, rows: 3, visibleCount: 9, labelKey: "layouts.three", tooltipKey: "tooltips.layoutThree", icon: "layout-three", positions: Array.from({ length: 9 }, (_, index) => ({
    column: index % 3, row: Math.floor(index / 3), columnSpan: 1, rowSpan: 1
  })) },
  focus: { columns: 3, rows: 3, visibleCount: 4, labelKey: "layouts.focus", tooltipKey: "tooltips.layoutFocus", icon: "layout-focus", positions: [
    { column: 0, row: 0, columnSpan: 2, rowSpan: 2 },
    { column: 2, row: 0, columnSpan: 1, rowSpan: 1 },
    { column: 2, row: 1, columnSpan: 1, rowSpan: 1 },
    { column: 2, row: 2, columnSpan: 1, rowSpan: 1 }
  ] },
  twoPlusOne: { columns: 2, rows: 2, visibleCount: 3, labelKey: "layouts.twoPlusOne", tooltipKey: "tooltips.layoutTwoPlusOne", icon: "layout-two-plus-one", positions: [
    { column: 0, row: 0, columnSpan: 2, rowSpan: 1 },
    { column: 0, row: 1, columnSpan: 1, rowSpan: 1 },
    { column: 1, row: 1, columnSpan: 1, rowSpan: 1 }
  ] },
  custom: { columns: 12, rows: 12, visibleCount: null, labelKey: "layouts.custom", tooltipKey: "tooltips.layoutCustom", icon: "layout-custom" },
  freeform: { freeform: true, visibleCount: null, labelKey: "layouts.freeform", tooltipKey: "tooltips.layoutFreeform", icon: "resize" }
};

const layoutPresetOrder = ["1x1", "2x2", "3x3", "focus", "twoPlusOne", "custom", "freeform"];

function renderLayoutPresets() {
  const container = document.getElementById("layout-presets");
  if (!container) return;

  container.innerHTML = layoutPresetOrder.map(layout => {
    const preset = layoutPresets[layout];
    return `<button class="icon-button" type="button" data-layout="${layout}"
      data-label-key="${preset.labelKey}" data-tooltip-key="${preset.tooltipKey}" aria-pressed="false">
      <span data-icon="${preset.icon}" aria-hidden="true"></span>
    </button>`;
  }).join("");
  renderIcons(container);
}

const themePresetOrder = ["dark", "light", "graphite", "ocean", "forest", "amber", "signal", "highContrast"];

const themePresets = {
  dark: {
    backgroundColor: "#0d1114", foregroundColor: "#edf3f4", panelColor: "#151a1f", accentColor: "#e7a346",
    panelStrongColor: "#232c33", lineColor: "#303b43", mutedColor: "#9aa9ae", focusColor: "#f4bd68", colorScheme: "dark"
  },
  light: {
    backgroundColor: "#f2f5f6", foregroundColor: "#172026", panelColor: "#ffffff", accentColor: "#b86b18",
    panelStrongColor: "#e7ecee", lineColor: "#c6d0d4", mutedColor: "#5a6870", focusColor: "#8a4a08", colorScheme: "light"
  },
  graphite: {
    backgroundColor: "#20252a", foregroundColor: "#f3f5f5", panelColor: "#2b3238", accentColor: "#8db5d6",
    panelStrongColor: "#353e45", lineColor: "#46515a", mutedColor: "#aab4ba", focusColor: "#b8d4eb", colorScheme: "dark"
  },
  ocean: {
    backgroundColor: "#081d2c", foregroundColor: "#e4f5ff", panelColor: "#0f3046", accentColor: "#48bdd5",
    panelStrongColor: "#16435d", lineColor: "#27556a", mutedColor: "#91b3c0", focusColor: "#86e4f2", colorScheme: "dark"
  },
  forest: {
    backgroundColor: "#0c2119", foregroundColor: "#e7f5ed", panelColor: "#153a2b", accentColor: "#7bc98e",
    panelStrongColor: "#1e4b38", lineColor: "#2e6048", mutedColor: "#9ab9a7", focusColor: "#a8e0b5", colorScheme: "dark"
  },
  amber: {
    backgroundColor: "#24170b", foregroundColor: "#fff0cf", panelColor: "#3a260f", accentColor: "#f0af48",
    panelStrongColor: "#4b3216", lineColor: "#63441f", mutedColor: "#c0a57c", focusColor: "#ffd27e", colorScheme: "dark"
  },
  signal: {
    backgroundColor: "#241718", foregroundColor: "#fff1ef", panelColor: "#3a2528", accentColor: "#e27a6d",
    panelStrongColor: "#4b3033", lineColor: "#654246", mutedColor: "#c3a3a5", focusColor: "#ffb0a5", colorScheme: "dark"
  },
  highContrast: {
    backgroundColor: "#000000", foregroundColor: "#ffffff", panelColor: "#111111", accentColor: "#ffee00",
    panelStrongColor: "#1c1c1c", lineColor: "#ffffff", mutedColor: "#d9d9d9", focusColor: "#ffee00", colorScheme: "dark"
  }
};

function getVisibleTiles(wall = getWallRenderValues()) {
  const preset = layoutPresets[wall.layoutPreset] || layoutPresets["2x2"];
  const limit = preset.visibleCount ?? wall.tiles.length;
  return wall.tiles.slice(0, limit);
}

function setMode(mode) {
  const nextMode = mode === "edit" ? "edit" : "wall";
  if (state.mode === "edit" && nextMode !== "edit") discardEditorDrafts();
  state.mode = nextMode;
  if (state.mode === "edit" && state.session.editorProtectionEnabled && !state.session.editor)
    state.inspectorTarget = "access";
  render({ preserveCanvas: true });
}

function updateTileSelection() {
  document.querySelectorAll(".stream-tile").forEach(tile => {
    tile.setAttribute("aria-selected", String(tile.dataset.tileId === state.selectedTileId));
  });
}

async function refreshStatuses() {
  if (!state.wall) return;
  try {
    const statuses = await api(`/api/walls/${encodeURIComponent(state.wallId)}/status`);
    state.statusByTile = new Map(statuses.map(status => [status.tileId, status]));
    document.querySelectorAll(".stream-tile").forEach(tileElement => {
      const status = state.statusByTile.get(tileElement.dataset.tileId);
      const localError = state.players.get(tileElement.dataset.tileId)?.localErrorDetail;
      if (localError) {
        setVisualTileStatus(tileElement, "error", localError, status?.detectedCodec);
      } else if (status) {
        setVisualTileStatus(tileElement, status.state, statusLabel(status.state), status.detectedCodec);
      }
    });
  } catch (error) {
    console.debug("status refresh failed", error);
  }
}

function handleVisibilityChange() {
  if (document.hidden) suspendPlayers();
  else resumePlayers();
}

function suspendPlayers() {
  for (const player of state.players.values()) player.suspend();
}

function resumePlayers() {
  if (document.hidden) return;
  for (const player of state.players.values()) player.resume();
}

function setVisualTileStatus(tileElement, stateName, detail, detectedCodec = "") {
  const statusElement = tileElement.querySelector(".tile-status");
  if (!statusElement) return;
  statusElement.dataset.state = String(stateName || "idle").toLowerCase();
  statusElement.querySelector("[data-status-text]").textContent = detail || t("status.waiting");
  statusElement.querySelector("[data-detected-codec]").textContent = detectedCodec || "";
}

function stopPlayers() {
  for (const player of state.players.values()) player.stop();
  state.players.clear();
}

class StreamPlayer {
  static mediaHealthTimeoutMs = 7000;

  constructor(host, tile, onStatus) {
    this.host = host;
    this.tile = tile;
    this.onStatus = onStatus;
    this.socket = null;
    this.socketMediaKind = null;
    this.decoder = null;
    this.decoderPromise = null;
    this.pendingEncodedFrames = [];
    this.reader = null;
    this.video = null;
    this.canvas = null;
    this.canvasContext = null;
    this.hls = null;
    this.capabilities = null;
    this.modes = [];
    this.modeIndex = 0;
    this.activeMode = null;
    this.transportHealthy = false;
    this.healthTimer = 0;
    this.image = null;
    this.objectUrl = null;
    this.stopped = false;
    this.suspended = false;
    this.localErrorDetail = "";
    this.reportedState = null;
    this.reportedDetail = null;
    this.reconnectTimer = 0;
    this.reconnectDelay = 500;
    this.connect();
  }

  async connect() {
    if (this.stopped || this.suspended) return;
    this.clearReconnect();
    this.disposeTransport();
    if (navigator.onLine === false) {
      this.report("reconnecting", t("status.reconnecting"));
      this.scheduleReconnect();
      return;
    }

    this.report("connecting", t("status.connecting"));
    let capabilities = null;
    try {
      const response = await fetch(`/api/streams/${encodeURIComponent(state.wallId)}/${encodeURIComponent(this.tile.id)}/capabilities`, {
        cache: "no-store",
        credentials: "same-origin"
      });
      if (response.ok) capabilities = await response.json();
    } catch {
      // The direct transport remains the compatibility baseline if the
      // optional capability endpoint or gateway is unavailable.
    }
    if (this.stopped || this.suspended) return;

    this.capabilities = capabilities;
    this.modes = this.createModes(capabilities);
    this.modeIndex = 0;
    this.activateMode();
  }

  createModes(capabilities) {
    const modes = [];
    const add = (type, url, label) => {
      if (!url || modes.some(mode => mode.type === type && mode.url === url)) return;
      modes.push({ type, url, label });
    };
    const direct = capabilities?.direct || {};
    const gateway = capabilities?.gateway || {};

    if (this.tile.codec === "mjpeg") {
      add("mjpeg", direct.mjpeg, "MJPEG");
      add("webrtc", gateway.h264WebRtc, "H.264");
      add("hls", gateway.h264Hls, "HLS");
    } else {
      add("webrtc", gateway.webRtc, "WebRTC");
      add("socket", direct.webSocket, "WebCodecs");
      add("webrtc", gateway.h264WebRtc, "H.264 fallback");
      add("hls", gateway.hls, "HLS");
      add("hls", gateway.h264Hls, "H.264 HLS");
    }

    if (modes.length === 0) {
      add(this.tile.codec === "mjpeg" ? "mjpeg" : "socket",
        this.tile.codec === "mjpeg" ? direct.mjpeg : direct.webSocket,
        this.tile.codec === "mjpeg" ? "MJPEG" : "WebCodecs");
    }
    return modes;
  }

  activateMode() {
    if (this.stopped || this.suspended) return;
    const mode = this.modes[this.modeIndex];
    if (!mode) {
      this.report("reconnecting", t("status.reconnecting"));
      this.scheduleReconnect();
      return;
    }

    this.activeMode = mode;
    this.transportHealthy = false;
    this.armHealthTimeout(mode);
    try {
      if (mode.type === "mjpeg") this.connectMjpegImage(mode.url);
      else if (mode.type === "socket") this.connectSocket(mode.url);
      else if (mode.type === "webrtc") this.connectWebRtc(mode.url);
      else if (mode.type === "hls") this.connectHls(mode.url);
    } catch (error) {
      this.failActiveMode(error);
    }
  }

  failActiveMode(error) {
    if (this.stopped || this.suspended) return;
    const failedMode = this.activeMode;
    this.disposeTransport();
    if (this.modeIndex + 1 < this.modes.length) {
      this.modeIndex++;
      this.report("reconnecting", failedMode?.label || t("status.reconnecting"));
      this.activateMode();
      return;
    }

    this.report("reconnecting", error?.message || t("status.reconnecting"));
    this.scheduleReconnect();
  }

  markHealthy() {
    this.reconnectDelay = 500;
    this.clearHealthTimeout();
  }

  armHealthTimeout(mode) {
    this.clearHealthTimeout();
    this.healthTimer = window.setTimeout(() => {
      this.healthTimer = 0;
      if (this.activeMode === mode && !this.transportHealthy &&
          !this.stopped && !this.suspended) {
        this.failActiveMode(new Error(t("status.noSignal")));
      }
    }, StreamPlayer.mediaHealthTimeoutMs);
  }

  clearHealthTimeout() {
    if (!this.healthTimer) return;
    window.clearTimeout(this.healthTimer);
    this.healthTimer = 0;
  }

  scheduleReconnect() {
    if (this.stopped || this.suspended || this.reconnectTimer) return;
    const delay = this.reconnectDelay;
    this.reconnectDelay = Math.min(15000, Math.ceil(this.reconnectDelay * 1.8));
    this.reconnectTimer = window.setTimeout(() => {
      this.reconnectTimer = 0;
      this.connect();
    }, delay);
  }

  clearReconnect() {
    if (!this.reconnectTimer) return;
    window.clearTimeout(this.reconnectTimer);
    this.reconnectTimer = 0;
  }

  connectMjpegImage(url) {
    const image = document.createElement("img");
    image.alt = this.tile.title || t("labels.video");
    image.addEventListener("load", () => {
      if (this.image !== image) return;
      this.transportHealthy = true;
      this.markHealthy();
      this.report("streaming", t("status.streaming"));
    });
    image.addEventListener("error", () => {
      if (this.image !== image || this.stopped || this.suspended) return;
      this.failActiveMode(new Error(t("status.noSignal")));
    });
    image.src = url + (url.includes("?") ? "&" : "?") + "attempt=" + Date.now();
    this.image = image;
    this.host.appendChild(image);
    this.report("connecting", t("status.connecting"));
  }

  connectSocket(url) {
    if (!("WebSocket" in window)) {
      this.failActiveMode(new Error(t("status.unsupported")));
      return;
    }
    const socketUrl = url.startsWith("ws:") || url.startsWith("wss:")
      ? url
      : (window.location.protocol === "https:" ? "wss" : "ws") + "://" + window.location.host + url;
    const socket = new WebSocket(socketUrl);
    socket.binaryType = "arraybuffer";
    this.socket = socket;
    socket.addEventListener("open", () => {
      if (this.socket === socket) this.report("connecting", t("status.connecting"));
    });
    socket.addEventListener("message", event => {
      if (this.socket === socket) this.handleSocketMessage(event);
    });
    socket.addEventListener("error", () => {
      if (this.socket !== socket || this.stopped || this.suspended) return;
      if (!this.transportHealthy) this.failActiveMode(new Error(t("status.noSignal")));
      else {
        this.report("reconnecting", t("status.reconnecting"));
        this.scheduleReconnect();
      }
    });
    socket.addEventListener("close", () => {
      if (this.socket !== socket) return;
      this.socket = null;
      if (!this.stopped && !this.suspended) {
        if (!this.transportHealthy) this.failActiveMode(new Error(t("status.noSignal")));
        else {
          this.report("reconnecting", t("status.reconnecting"));
          this.scheduleReconnect();
        }
      }
    });
  }

  createVideoElement() {
    const video = document.createElement("video");
    video.autoplay = true;
    video.muted = true;
    video.playsInline = true;
    video.controls = false;
    this.video = video;
    this.host.appendChild(video);
    return video;
  }

  connectWebRtc(url) {
    if (typeof window.MediaMTXWebRTCReader !== "function") {
      this.failActiveMode(new Error(t("status.webRtcUnavailable")));
      return;
    }

    const video = this.createVideoElement();
    let reader = null;
    reader = new window.MediaMTXWebRTCReader({
      url,
      onError: error => {
        if (this.reader !== reader || this.stopped || this.suspended) return;
        if (!this.transportHealthy) this.failActiveMode(new Error(String(error)));
        else this.report("reconnecting", t("status.reconnecting"));
      },
      onTrack: event => {
        if (this.reader !== reader || this.stopped || this.suspended) return;
        const stream = event.streams?.[0];
        if (!stream) return;
        video.srcObject = stream;
        video.play().catch(() => {});
        this.transportHealthy = true;
        this.markHealthy();
        this.report("streaming", t("status.streaming"));
      }
    });
    this.reader = reader;
    this.report("connecting", t("status.connecting"));
  }

  connectHls(url) {
    const video = this.createVideoElement();
    video.addEventListener("playing", () => {
      if (this.video !== video) return;
      this.transportHealthy = true;
      this.markHealthy();
      this.report("streaming", t("status.streaming"));
    });
    video.addEventListener("error", () => {
      if (this.video !== video || this.stopped || this.suspended) return;
      this.failActiveMode(new Error(t("status.noSignal")));
    });

    const HlsCtor = window.Hls;
    if (HlsCtor && typeof HlsCtor.isSupported === "function" && HlsCtor.isSupported()) {
      const hls = new HlsCtor({
        enableWorker: true,
        lowLatencyMode: true
      });
      this.hls = hls;
      hls.on(HlsCtor.Events.ERROR, (_, data) => {
        if (this.hls !== hls || this.stopped || this.suspended || !data?.fatal) return;
        this.failActiveMode(new Error(data.details || t("status.noSignal")));
      });
      hls.on(HlsCtor.Events.MEDIA_ATTACHED, () => {
        if (this.hls === hls) hls.loadSource(url);
      });
      hls.attachMedia(video);
      this.report("connecting", t("status.connecting"));
      return;
    }

    if (video.canPlayType("application/vnd.apple.mpegurl")) {
      video.src = url;
      video.play().catch(() => {});
      this.report("connecting", t("status.connecting"));
      return;
    }

    this.failActiveMode(new Error(t("status.hlsUnavailable")));
  }

  handleSocketMessage(event) {
    if (typeof event.data === "string") {
      let message;
      try {
        message = JSON.parse(event.data);
      } catch {
        this.report("error", t("status.noSignal"));
        return;
      }
      if (message.kind === "h264") {
        this.socketMediaKind = "h264";
        this.configureDecoder(message.codec || "avc1.42E01E", "h264");
      }
      if (message.kind === "h265") {
        this.socketMediaKind = "h265";
        this.configureDecoder(message.codec || "hvc1.1.6.L120.B0", "h265");
      }
      if (message.kind === "mjpeg") this.prepareMjpegSocket();
      if (message.kind === "unsupported") this.report("error", message.reason || t("status.unsupported"));
      return;
    }

    if (this.decoder) {
      this.decodeEncodedVideo(event.data);
      return;
    }
    if (this.decoderPromise) {
      if (this.pendingEncodedFrames.length < 8) this.pendingEncodedFrames.push(event.data);
      return;
    }
    if (this.socketMediaKind === "h264" || this.socketMediaKind === "h265") {
      return;
    }
    this.renderMjpegSocket(event.data);
  }

  configureDecoder(codec, kind) {
    if (!("VideoDecoder" in window)) {
      this.failActiveMode(new Error(kind === "h265" ? t("status.h265Unavailable") : t("status.webCodecsUnavailable")));
      return;
    }
    if (this.decoder || this.decoderPromise) return;

    const socket = this.socket;
    const config = { codec, optimizeForLatency: true };
    if (kind === "h264") config.avc = { format: "annexb" };

    const promise = this.createDecoder(config, kind, socket)
      .catch(error => {
        this.pendingEncodedFrames = [];
        if (this.socket === socket && !this.stopped && !this.suspended) {
          this.failActiveMode(new Error(error.message || (kind === "h265"
            ? t("status.h265Unavailable")
            : t("status.webCodecsUnavailable"))));
        }
      })
      .finally(() => {
        if (this.decoderPromise === promise) this.decoderPromise = null;
      });
    this.decoderPromise = promise;
  }

  async createDecoder(config, kind, socket) {
    if (typeof VideoDecoder.isConfigSupported !== "function")
      throw new Error(kind === "h265" ? t("status.h265Unavailable") : t("status.webCodecsUnavailable"));

    const support = await VideoDecoder.isConfigSupported(config);
    if (!support.supported)
      throw new Error(kind === "h265" ? t("status.h265Unavailable") : t("status.webCodecsUnavailable"));
    if (this.socket !== socket || this.stopped || this.suspended) return;

    const decoder = new VideoDecoder({
        output: frame => {
          let canvas = this.canvas;
          if (!canvas || !canvas.isConnected) {
            canvas = this.host.querySelector("canvas");
          }
          if (!canvas) {
            canvas = document.createElement("canvas");
            this.host.appendChild(canvas);
          }
          this.canvas = canvas;
          const width = frame.displayWidth || frame.codedWidth;
          const height = frame.displayHeight || frame.codedHeight;
          if (canvas.width !== width || canvas.height !== height || !this.canvasContext) {
            canvas.width = width;
            canvas.height = height;
            this.canvasContext = canvas.getContext("2d", {
              alpha: false,
              desynchronized: true
            });
          }
          this.canvasContext?.drawImage(frame, 0, 0, width, height);
          frame.close();
          this.transportHealthy = true;
          this.markHealthy();
          this.report("streaming", t("status.streaming"));
        },
        error: error => {
          if (!this.stopped && !this.suspended) {
            if (!this.transportHealthy) this.failActiveMode(error);
            else this.report("error", error.message || t("status.noSignal"));
          }
        }
      });
    this.decoder = decoder;
    decoder.configure(config);
    const pending = this.pendingEncodedFrames.splice(0);
    for (const buffer of pending) this.decodeEncodedVideo(buffer);
  }

  decodeEncodedVideo(buffer) {
    if (!this.decoder || buffer.byteLength < 9) return;
    try {
      const view = new DataView(buffer);
      const keyFrame = (view.getUint8(0) & 1) !== 0;
      const timestamp = Number(view.getBigInt64(1, true));
      const data = new Uint8Array(buffer, 9);
      this.decoder.decode(new EncodedVideoChunk({
        type: keyFrame ? "key" : "delta",
        timestamp,
        data
      }));
    } catch (error) {
      this.report("error", error.message || t("status.noSignal"));
    }
  }

  prepareMjpegSocket() {
    if (!this.image) {
      this.image = document.createElement("img");
      this.image.alt = this.tile.title || t("labels.video");
      this.host.appendChild(this.image);
    }
  }

  renderMjpegSocket(data) {
    this.prepareMjpegSocket();
    const nextUrl = URL.createObjectURL(new Blob([data], { type: "image/jpeg" }));
    const previousUrl = this.objectUrl;
    this.objectUrl = nextUrl;
    this.image.onload = () => {
      if (previousUrl) URL.revokeObjectURL(previousUrl);
      this.transportHealthy = true;
      this.markHealthy();
      this.report("streaming", t("status.streaming"));
    };
    this.image.src = nextUrl;
  }

  report(stateName, detail) {
    if (stateName === "error") this.localErrorDetail = detail || t("status.error");
    else this.localErrorDetail = "";
    if (this.reportedState === stateName && this.reportedDetail === detail) return;
    this.reportedState = stateName;
    this.reportedDetail = detail;
    this.onStatus({ state: stateName, detail });
  }

  suspend() {
    if (this.stopped || this.suspended) return;
    this.suspended = true;
    this.clearReconnect();
    this.disposeTransport();
  }

  resume() {
    if (this.stopped || !this.suspended) return;
    this.suspended = false;
    this.retryNow();
  }

  retryNow() {
    if (this.stopped || this.suspended) return;
    if (this.socket?.readyState === WebSocket.OPEN || this.transportHealthy || this.reconnectTimer) return;
    this.clearReconnect();
    this.reconnectDelay = 500;
    this.connect();
  }

  disposeTransport() {
    this.clearHealthTimeout();
    const reader = this.reader;
    this.reader = null;
    if (reader) reader.close();

    const hls = this.hls;
    this.hls = null;
    if (hls) hls.destroy();

    const socket = this.socket;
    this.socket = null;
    this.socketMediaKind = null;
    if (socket && socket.readyState <= WebSocket.OPEN) socket.close();

    const decoder = this.decoder;
    this.decoder = null;
    this.decoderPromise = null;
    this.pendingEncodedFrames = [];
    this.canvas = null;
    this.canvasContext = null;
    if (decoder && decoder.state !== "closed") decoder.close();

    const image = this.image;
    this.image = null;
    image?.remove();
    const video = this.video;
    this.video = null;
    if (video) {
      video.pause();
      video.srcObject = null;
      video.removeAttribute("src");
      video.load();
      video.remove();
    }
    if (this.objectUrl) {
      URL.revokeObjectURL(this.objectUrl);
      this.objectUrl = null;
    }
    this.activeMode = null;
    this.transportHealthy = false;
  }

  stop() {
    this.stopped = true;
    this.suspended = true;
    this.clearReconnect();
    this.disposeTransport();
  }
}

function applyLabels(root) {
  root.querySelectorAll("[data-i18n]").forEach(element => {
    element.textContent = t(element.dataset.i18n);
  });
  root.querySelectorAll("[data-i18n-aria-label]").forEach(element => {
    element.setAttribute("aria-label", t(element.dataset.i18nAriaLabel));
  });
  root.querySelectorAll("[data-label-key]").forEach(element => {
    const label = t(element.dataset.labelKey);
    element.setAttribute("aria-label", label);
    if (element.dataset.tooltipKey) element.dataset.tooltip = t(element.dataset.tooltipKey);
  });
  root.querySelectorAll("[data-placeholder-key]").forEach(element => {
    element.setAttribute("placeholder", t(element.dataset.placeholderKey));
  });
  renderIcons(root);
  wireTouchHints(root);
}

function renderIcons(root) {
  root.querySelectorAll("[data-icon]").forEach(element => {
    const name = element.dataset.icon;
    element.innerHTML = icon(name);
  });
}

function icon(name) {
  const pathName = iconAliases[name] || name;
  const className = iconClassName(name);
  return `<svg class="icon-svg icon-${className}" viewBox="0 0 24 24" aria-hidden="true">${iconPaths[pathName] || iconPaths.circle}</svg>`;
}

function iconClassName(name) {
  return String(name || "circle")
    .replace(/([a-z])([A-Z])/g, "$1-$2")
    .replace(/[^a-zA-Z0-9-]/g, "-")
    .toLowerCase();
}

function wireTouchHints(root) {
  root.querySelectorAll("[data-tooltip]").forEach(element => {
    if (element.dataset.touchHintWired === "true") return;
    element.dataset.touchHintWired = "true";

    let timer = 0;
    let startX = 0;
    let startY = 0;

    const cancel = () => {
      window.clearTimeout(timer);
      timer = 0;
    };

    element.addEventListener("pointerdown", event => {
      if (event.pointerType !== "touch" && event.pointerType !== "pen") return;
      startX = event.clientX;
      startY = event.clientY;
      cancel();
      timer = window.setTimeout(() => {
        timer = 0;
        showToast(element.dataset.tooltip, 1800);
      }, 550);
    });
    element.addEventListener("pointermove", event => {
      if (!timer || Math.hypot(event.clientX - startX, event.clientY - startY) > 8) cancel();
    });
    element.addEventListener("pointerup", cancel);
    element.addEventListener("pointercancel", cancel);
    element.addEventListener("pointerleave", cancel);
  });
}

function selectOptions(options, selected) {
  return options.map(([value, key]) => `<option value="${escapeAttribute(value)}" ${value === selected ? "selected" : ""}>${escapeHtml(t(key))}</option>`).join("");
}

function layoutLabel(layout) {
  const preset = layoutPresets[layout] || layoutPresets["2x2"];
  return t(preset.labelKey);
}

function optionLabel(value) {
  const key = value === "mjpeg" ? "options.mjpeg" : value === "h264" ? "options.h264" : value === "h265" ? "options.h265" : value === "rtsp" ? "options.rtsp" : value === "rtp" ? "options.rtp" : value === "udp" ? "options.udp" : value;
  return t(key);
}

function statusLabel(value) {
  const key = String(value || "idle").toLowerCase();
  return t(`status.${key}`);
}

function t(key, values = {}) {
  const value = key.split(".").reduce((current, segment) => current?.[segment], state.messages);
  if (typeof value !== "string" && isDevelopmentHost() && !missingLocaleKeys.has(`${state.locale}:${key}`)) {
    missingLocaleKeys.add(`${state.locale}:${key}`);
    console.warn(`Missing locale key: ${state.locale}.${key}`);
  }
  let result = typeof value === "string" ? value : key;
  Object.entries(values).forEach(([name, replacement]) => {
    result = result.replaceAll(`{{${name}}}`, String(replacement));
  });
  return result;
}

function isDevelopmentHost() {
  return window.location.hostname === "localhost" ||
    window.location.hostname === "127.0.0.1" ||
    window.location.hostname === "[::1]";
}

async function api(path, options = {}) {
  const request = { ...options, headers: { "Content-Type": "application/json", ...(options.headers || {}) } };
  const response = await fetch(path, request);
  if (!response.ok) {
    const error = await createApiError(response);
    if (response.status === 401 && state.auth.enabled && !path.startsWith("/api/auth/"))
      showAccessGate(error.message);
    throw error;
  }
  if (response.status === 204) return null;
  return response.json();
}

async function createApiError(response) {
  const raw = await response.text();
  let payload = null;
  try {
    payload = raw ? JSON.parse(raw) : null;
  } catch {
    // Keep the original response text when the server did not return JSON.
  }
  const error = new Error(payload?.error || raw || `HTTP ${response.status}`);
  error.details = Array.isArray(payload?.errors) ? payload.errors : [];
  if (Array.isArray(payload?.details)) error.details.push(...payload.details);
  error.issues = Array.isArray(payload?.issues)
    ? payload.issues.filter(issue => typeof issue?.code === "string")
    : [];
  error.code = typeof payload?.code === "string" ? payload.code : "";
  error.status = response.status;
  return error;
}

function errorMessage(error, fallback) {
  const details = Array.isArray(error?.details) ? error.details : [];
  const issueDetails = Array.isArray(error?.issues)
    ? error.issues.map(localizeValidationIssue).filter(Boolean)
    : [];
  const localizedKey = error?.code ? `errors.${error.code}` : "";
  const localized = localizedKey ? t(localizedKey) : "";
  const heading = localized && localized !== localizedKey
    ? localized
    : error?.message || fallback;
  const extraDetails = (issueDetails.length > 0 ? issueDetails : details)
    .filter(detail => detail !== heading);
  return extraDetails.length > 0 ? `${heading} ${extraDetails.join(" ")}` : heading;
}

function localizeValidationIssue(issue) {
  if (!issue || typeof issue !== "object") return "";
  const values = { ...(issue.values || {}) };
  const tile = typeof values.tile === "string" ? values.tile : "";
  delete values.tile;
  const key = issue.code ? `errors.${issue.code}` : "";
  const localized = key ? t(key, values) : "";
  const message = localized && localized !== key ? localized : String(issue.message || "");
  return tile && message ? `${tile}: ${message}` : message;
}

function sourceTestMessage(result) {
  const issues = Array.isArray(result?.issues) ? result.issues : [];
  if (issues.length > 0) {
    const localized = issues.map(localizeValidationIssue).filter(Boolean);
    if (localized.length > 0) return localized.join(" ");
  }
  return result?.message || t("messages.testFailed");
}

function getWallId() {
  const parts = window.location.pathname.split("/").filter(Boolean);
  return parts[1] || "default";
}

function safeColor(value, fallback) {
  return /^#[0-9a-f]{3,8}$/i.test(String(value || "")) ? value : fallback;
}

function getActiveThemePreset(wall) {
  return themePresetOrder.find(key => ["backgroundColor", "foregroundColor", "panelColor", "accentColor"].every(name =>
    safeColor(wall?.[name], "").toLowerCase() === themePresets[key][name].toLowerCase())) || null;
}

function getThemeSurface(wall) {
  const backgroundColor = safeColor(wall?.backgroundColor, themePresets.dark.backgroundColor);
  const foregroundColor = safeColor(wall?.foregroundColor, themePresets.dark.foregroundColor);
  const panelColor = safeColor(wall?.panelColor, themePresets.dark.panelColor);
  const accentColor = safeColor(wall?.accentColor, themePresets.dark.accentColor);
  const preset = getActiveThemePreset(wall);
  const colorScheme = preset?.colorScheme || (getColorLuminance(backgroundColor) > 0.55 ? "light" : "dark");
  const fallback = colorScheme === "light" ? themePresets.light : themePresets.dark;

  return {
    backgroundColor,
    foregroundColor,
    panelColor,
    accentColor,
    panelStrongColor: preset?.panelStrongColor || fallback.panelStrongColor,
    lineColor: preset?.lineColor || fallback.lineColor,
    mutedColor: preset?.mutedColor || fallback.mutedColor,
    focusColor: preset?.focusColor || fallback.focusColor,
    colorScheme
  };
}

function getColorLuminance(value) {
  let hex = safeColor(value, "#0d1114").slice(1);
  if (hex.length === 3) hex = hex.split("").map(character => character + character).join("");
  hex = hex.slice(0, 6);
  const channels = [0, 2, 4].map(offset => Number.parseInt(hex.slice(offset, offset + 2), 16) / 255);
  const linear = channels.map(channel => channel <= 0.03928
    ? channel / 12.92
    : Math.pow((channel + 0.055) / 1.055, 2.4));
  return 0.2126 * linear[0] + 0.7152 * linear[1] + 0.0722 * linear[2];
}

function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>"']/g, character => ({
    "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;"
  }[character]));
}

function escapeAttribute(value) {
  return escapeHtml(value).replace(/`/g, "&#96;");
}

function showToast(message, duration = 2600) {
  const toast = document.getElementById("toast");
  toast.textContent = message;
  toast.hidden = false;
  window.clearTimeout(state.toastTimer);
  state.toastTimer = window.setTimeout(() => { toast.hidden = true; }, duration);
}
