// =============================================================================
// history.js  (dashboard page)
//
// The "Past runs" window: lists the live runs and comparisons saved in the
// database, and lets you look at a run's chart again, continue a run, reopen
// a comparison, download Excel, CSV or JSON, or delete one. Uses the Compare
// panel's styles (.cmp) so both look the same in light and dark mode.
//
// It talks to the server over plain HTTP (fetch) using the /api/history
// endpoints in Controllers/HistoryController.cs.
//
// The whole file is wrapped in (function () { ... })() so its variables stay
// private and don't clash with index.html's globals. It builds its own HTML
// when the page loads and hooks up the "Past runs" button (#historyButton).
//
// Things it borrows from index.html if they exist: monitorLabel and
// metricColor (names and colours for chart lines), parameterDefinitions (to
// list a run's settings) and continueSavedRun (the Continue button).
// From compare.js: window.abmCompare.showSaved (to reopen a comparison).
// =============================================================================

(function () {
    "use strict";

    // Everything this window remembers between clicks.
    const state = {
        tab: "runs",      // "runs" or "compares"
        model: "",        // model filter ("" = all models)
        chart: null,      // the Chart.js chart in the detail view, if one is open
        info: null,       // /api/history/info: is saving on, where is the database
        runs: {}          // the listed runs by id, for the Continue buttons
    };

    // Handy references to the window's elements, filled in by build().
    let el = {};

    // Escapes text before putting it into HTML, so a name like "<b>" can't
    // break the page (or inject anything).
    const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) =>
        ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));

    // A date as "30 Oct 2026, 14:15" in the user's own format.
    function when(iso) {
        return new Date(iso).toLocaleString(undefined, {
            day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit"
        });
    }

    // A readable name for a chart line: "HappyAgents" -> "Happy Agents".
    function label(key) {
        if (typeof monitorLabel === "function") return monitorLabel(key);
        return key.replace(/([a-z])([A-Z])/g, "$1 $2");
    }

    // The line colour: the same colour the dashboard uses, or one from a palette.
    function colour(key, i) {
        const fromPage = typeof metricColor === "function" ? metricColor(key) : null;
        const palette = ["#2e6be6", "#e8590c", "#2f9e5b", "#9c36b5", "#c92a2a", "#0b7285"];
        return fromPage || palette[i % palette.length];
    }

    // fetch() + error handling: throws an Error with the server's message
    // (shown in the red box) if the request failed. 204 (No Content) -> null.
    async function getJson(url, options) {
        const response = await fetch(url, options);
        if (!response.ok) {
            const text = await response.text();
            throw new Error(text || `The server answered ${response.status}.`);
        }
        return response.status === 204 ? null : response.json();
    }

    // Starts a file download by clicking a temporary <a download> link.
    function download(url) {
        const a = document.createElement("a");
        a.href = url;
        a.download = "";
        document.body.appendChild(a);
        a.click();
        a.remove();
    }

    // ------------------------------------------------------------------ build

    // Creates the window's HTML (hidden until opened) and its event handlers.
    function build() {
        const overlay = document.createElement("div");
        overlay.className = "cmp cmp-overlay hist";
        overlay.setAttribute("role", "dialog");
        overlay.setAttribute("aria-modal", "true");
        overlay.setAttribute("aria-label", "Past runs");
        overlay.innerHTML = `
            <div class="cmp-sheet">
                <div class="cmp-head">
                    <div class="cmp-grow">
                        <h2>Past runs</h2>
                        <p>Every run and comparison is saved to the database. Open one to see its results again.</p>
                    </div>
                    <button type="button" class="cmp-close" id="histClose" aria-label="Close">&times;</button>
                </div>
                <div class="cmp-body">
                    <div class="cmp-card hist-bar">
                        <div class="hist-tabs" role="tablist">
                            <button type="button" role="tab" data-tab="runs" class="active">Live runs</button>
                            <button type="button" role="tab" data-tab="compares">Compares</button>
                        </div>
                        <label class="cmp-field"><span>Model</span><select id="histModel"></select></label>
                        <button type="button" id="histRefresh">Refresh</button>
                    </div>

                    <div class="cmp-error" id="histError" role="alert" hidden></div>

                    <div class="cmp-card" id="histListCard">
                        <div id="histList"></div>
                    </div>

                    <div id="histDetail" hidden></div>

                    <p class="hist-path" id="histPath"></p>
                </div>
            </div>`;
        document.body.appendChild(overlay);

        el = {
            overlay,
            close: overlay.querySelector("#histClose"),
            model: overlay.querySelector("#histModel"),
            refresh: overlay.querySelector("#histRefresh"),
            error: overlay.querySelector("#histError"),
            listCard: overlay.querySelector("#histListCard"),
            list: overlay.querySelector("#histList"),
            detail: overlay.querySelector("#histDetail"),
            path: overlay.querySelector("#histPath"),
            tabs: overlay.querySelectorAll(".hist-tabs button")
        };

        el.close.addEventListener("click", close);
        overlay.addEventListener("mousedown", (e) => { if (e.target === overlay) close(); });
        document.addEventListener("keydown", (e) => { if (e.key === "Escape" && el.overlay.classList.contains("open")) close(); });
        el.refresh.addEventListener("click", () => loadList());
        el.model.addEventListener("change", () => { state.model = el.model.value; loadList(); });
        el.tabs.forEach((tab) => tab.addEventListener("click", () => {
            state.tab = tab.dataset.tab;
            el.tabs.forEach((t) => t.classList.toggle("active", t === tab));
            loadList();
        }));
        el.list.addEventListener("click", onListClick);
    }

    // Hooks up the dashboard's "Past runs" button.
    function addButton() {
        const button = document.getElementById("historyButton");
        if (button) button.addEventListener("click", open);
    }

    // ------------------------------------------------------------ open/close

    // Shows the window and loads the list.
    async function open() {
        el.overlay.classList.add("open");
        document.body.style.overflow = "hidden";
        fillModels();
        if (!state.info) {
            try {
                state.info = await getJson("api/history/info");
                el.path.textContent = state.info.enabled ? `Saved in ${state.info.databasePath}` : "";
            } catch { }
        }
        await loadList();
    }

    // Hides the window.
    function close() {
        el.overlay.classList.remove("open");
        document.body.style.overflow = "";
        destroyChart();
    }

    // Copies the dashboard's model list into the filter drop-down.
    function fillModels() {
        const source = document.getElementById("modelSelect");
        const options = source ? [...source.options].map((o) => `<option value="${esc(o.value)}">${esc(o.textContent)}</option>`) : [];
        el.model.innerHTML = `<option value="">All models</option>` + options.join("");
        el.model.value = state.model;
    }

    // ------------------------------------------------------------------ lists

    // Loads and shows the list for the current tab and model filter.
    async function loadList() {
        hideError();
        hideDetail();
        el.list.innerHTML = `<p class="hist-empty">Loading...</p>`;
        const query = state.model ? `?model=${encodeURIComponent(state.model)}` : "";
        try {
            if (state.tab === "runs") renderRuns(await getJson(`api/history/runs${query}`));
            else renderCompares(await getJson(`api/history/compares${query}`));
        } catch (err) {
            el.list.innerHTML = "";
            showError(err.message);
        }
    }

    // Builds the buttons cell of a table row. Each button is
    // [action name, text, extra HTML attributes].
    function actions(buttons) {
        return `<td class="hist-actions">${buttons.map(([act, text, extra]) =>
            `<button type="button" data-act="${act}"${extra || ""}>${text}</button>`).join("")}</td>`;
    }

    // Shows the Live runs table.
    function renderRuns(runs) {
        state.runs = Object.fromEntries(runs.map((r) => [r.id, r]));
        if (runs.length === 0) {
            el.list.innerHTML = `<p class="hist-empty">No saved runs yet. Press Setup and Run on the dashboard and the run is saved here by itself.</p>`;
            return;
        }

        const rows = runs.map((r) => `
            <tr data-id="${esc(r.id)}">
                <td>${esc(when(r.startedAt))}</td>
                <td>${esc(r.displayName)}</td>
                <td>${r.gridWidth} x ${r.gridHeight}</td>
                <td>${r.tickCount} / ${r.tickLimit}</td>
                <td>${r.seed ?? "-"}</td>
                <td><span class="hist-status ${esc(r.status.replace(/\s+/g, "-").toLowerCase())}">${esc(r.status)}</span></td>
                ${actions([
                    ["view", "View"],
                    ["continue", "Continue", (r.canContinue ? "" : " disabled") + ` title="${esc(r.continueHint)}"`],
                    ["xlsx", "Excel"], ["csv", "CSV"], ["json", "JSON"],
                    ["delete", "Delete", r.isCurrent ? ' disabled title="This run is still on screen"' : ""]])}
            </tr>`).join("");

        el.list.innerHTML = `
            <div class="cmp-scroll hist-scroll"><table>
                <thead><tr><th>Date</th><th>Model</th><th>Grid</th><th>Ticks</th><th>Seed</th><th>Status</th><th></th></tr></thead>
                <tbody>${rows}</tbody>
            </table></div>`;
    }

    // Shows the Compares table (A and B of one comparison are one row).
    function renderCompares(groups) {
        if (groups.length === 0) {
            el.list.innerHTML = `<p class="hist-empty">No saved comparisons yet. Every comparison you run with Compare is saved here.</p>`;
            return;
        }

        const rows = groups.map((g) => `
            <tr data-id="${esc(g.groupId)}">
                <td>${esc(when(g.createdAt))}</td>
                <td>${esc(g.displayName)}</td>
                <td>${g.labels.length > 1 ? "A and B" : "One"}</td>
                <td>${g.runs}${g.labels.length > 1 ? " each" : ""}</td>
                <td>${g.gridWidth ?? "-"} x ${g.gridHeight ?? "-"}</td>
                <td>${g.tickLimit}</td>
                <td>${g.baseSeed}</td>
                ${actions([["open", "Open"], ["cjson", "JSON"], ["cdelete", "Delete"]])}
            </tr>`).join("");

        el.list.innerHTML = `
            <div class="cmp-scroll hist-scroll"><table>
                <thead><tr><th>Date</th><th>Model</th><th>Settings</th><th>Runs</th><th>Grid</th><th>Ticks</th><th>Base seed</th><th></th></tr></thead>
                <tbody>${rows}</tbody>
            </table></div>`;
    }

    // One click handler for every button in the list (event delegation):
    // reads the row's id and the button's data-act and does that action.
    async function onListClick(e) {
        const button = e.target.closest("button[data-act]");
        if (!button || button.disabled) return;
        const id = button.closest("tr").dataset.id;
        const act = button.dataset.act;

        hideError();
        try {
            if (act === "view") await showRun(id);
            else if (act === "continue") await continueRun(id);
            else if (act === "xlsx") download(`api/history/runs/${id}/ticks.xlsx`);
            else if (act === "csv") download(`api/history/runs/${id}/ticks.csv`);
            else if (act === "json") download(`api/history/runs/${id}/export.json`);
            else if (act === "cjson") download(`api/history/compares/${id}/export.json`);
            else if (act === "open") await openCompare(id);
            else if (act === "delete" || act === "cdelete") await confirmDelete(button, act, id);
        } catch (err) {
            showError(err.message);
        }
    }

    // First click asks, second click deletes. Moving away resets it.
    async function confirmDelete(button, act, id) {
        if (!button.classList.contains("sure")) {
            button.classList.add("sure");
            button.textContent = "Sure?";
            button.addEventListener("mouseleave", () => {
                button.classList.remove("sure");
                button.textContent = "Delete";
            }, { once: true });
            return;
        }

        const url = act === "delete" ? `api/history/runs/${id}` : `api/history/compares/${id}`;
        await getJson(url, { method: "DELETE" });
        await loadList();
    }

    // ---------------------------------------------------------------- details

    // The View button: shows one run's facts, settings and chart.
    async function showRun(id) {
        const run = await getJson(`api/history/runs/${id}`);
        destroyChart();

        const keys = run.ticks.length ? Object.keys(run.ticks[0].values) : [];
        const settings = settingsList(run);

        el.detail.innerHTML = `
            <div class="cmp-card">
                <div class="cmp-chart-top">
                    <h3>${esc(run.displayName)}<small>${esc(when(run.startedAt))}</small></h3>
                    <button type="button" data-act="continue" class="primary">Continue</button>
                    <button type="button" data-act="xlsx">Download Excel</button>
                    <button type="button" data-act="csv">Download CSV</button>
                    <button type="button" data-act="json">Download JSON</button>
                    <button type="button" data-act="back">Back to the list</button>
                </div>
                <div class="hist-facts">
                    <span><b>Status</b> ${esc(run.status)}</span>
                    <span><b>Ticks</b> ${run.tickCount}</span>
                    <span><b>Grid</b> ${run.gridWidth ?? "-"} x ${run.gridHeight ?? "-"}</span>
                    <span><b>Edges</b> ${run.topology === "Toroidal" ? "Wrap around" : "Walls"}</span>
                    <span><b>Scheduling</b> ${esc(run.scheduler === "Sequential" ? "Same order" : "Random order")}</span>
                    <span><b>Seed</b> ${run.seed ?? "None"}</span>
                </div>
                ${settings ? `<div class="hist-settings">${settings}</div>` : ""}
                ${run.ticks.length
                    ? `<div class="cmp-chart-wrap"><canvas id="histChart"></canvas></div>`
                    : `<p class="hist-empty">This run has no ticks saved yet.</p>`}
            </div>`;

        const listed = state.runs[id];
        const go = el.detail.querySelector('[data-act="continue"]');
        go.disabled = !(listed && listed.canContinue);
        go.title = listed ? listed.continueHint : "";
        go.addEventListener("click", () => continueRun(id).catch((err) => showError(err.message)));
        el.detail.querySelector('[data-act="xlsx"]').addEventListener("click", () => download(`api/history/runs/${id}/ticks.xlsx`));
        el.detail.querySelector('[data-act="csv"]').addEventListener("click", () => download(`api/history/runs/${id}/ticks.csv`));
        el.detail.querySelector('[data-act="json"]').addEventListener("click", () => download(`api/history/runs/${id}/export.json`));
        el.detail.querySelector('[data-act="back"]').addEventListener("click", hideDetail);

        el.listCard.hidden = true;
        el.detail.hidden = false;

        if (run.ticks.length && typeof Chart !== "undefined") {
            const dark = document.documentElement.dataset.theme === "dark";
            const ink = dark ? "#c9d1e0" : "#444";
            const grid = dark ? "rgba(255,255,255,0.08)" : "rgba(0,0,0,0.08)";
            state.chart = new Chart(el.detail.querySelector("#histChart"), {
                type: "line",
                data: {
                    datasets: keys.map((key, i) => ({
                        label: label(key),
                        data: run.ticks.map((t) => ({ x: t.tick, y: t.values[key] })),
                        borderColor: colour(key, i),
                        backgroundColor: colour(key, i),
                        borderWidth: 2,
                        pointRadius: 0,
                        tension: 0.15
                    }))
                },
                options: {
                    animation: false,
                    maintainAspectRatio: false,
                    parsing: false,
                    interaction: { mode: "index", intersect: false },
                    plugins: { legend: { labels: { color: ink } } },
                    scales: {
                        x: { type: "linear", title: { display: true, text: "Tick", color: ink }, ticks: { color: ink, precision: 0 }, grid: { color: grid } },
                        y: { beginAtZero: true, ticks: { color: ink }, grid: { color: grid } }
                    }
                }
            });
        }
    }

    // The sliders that were set for the run, as "Initial sheep 80".
    function settingsList(run) {
        const defs = typeof parameterDefinitions !== "undefined" ? parameterDefinitions[run.model] : null;
        if (!defs || !run.configuration) return "";
        return defs.map(([key, text, , , , percent]) => {
            const name = key[0].toLowerCase() + key.slice(1);
            const value = run.configuration[name];
            if (value === undefined || value === null) return "";
            const shown = percent ? `${Math.round(value * 100)}%` : (Number.isInteger(value) ? value : Number(value).toFixed(2));
            return `<span>${esc(text)} <b>${esc(shown)}</b></span>`;
        }).join("");
    }

    // The Continue button: closes this window and asks the dashboard to
    // replay the run up to where it stopped (see LiveSimulation.cs).
    async function continueRun(id) {
        const run = state.runs[id];
        if (!run || !run.canContinue) return;
        if (typeof continueSavedRun !== "function") throw new Error("The dashboard is not ready yet.");
        close();
        await continueSavedRun(run);
    }

    // The Open button: shows a saved comparison in the Compare panel again.
    async function openCompare(groupId) {
        if (!window.abmCompare) throw new Error("The Compare panel is not loaded.");
        const saved = await getJson(`api/history/compares/${groupId}`);
        close();
        await window.abmCompare.showSaved(saved);
    }

    // Back from the detail view to the list.
    function hideDetail() {
        destroyChart();
        el.detail.hidden = true;
        el.detail.innerHTML = "";
        el.listCard.hidden = false;
    }

    // Chart.js charts must be destroyed before their canvas is reused.
    function destroyChart() {
        if (state.chart) { state.chart.destroy(); state.chart = null; }
    }

    // ------------------------------------------------------------------ errors

    // Shows a message in the red error box.
    function showError(message) {
        el.error.textContent = message;
        el.error.hidden = false;
    }

    function hideError() {
        el.error.hidden = true;
        el.error.textContent = "";
    }

    // Runs once the page's HTML is ready.
    function init() {
        build();
        addButton();
    }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init);
    else init();
})();
