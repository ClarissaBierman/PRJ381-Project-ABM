// Compare runs: the dashboard's version of NetLogo's BehaviorSpace.
//
// The user picks a model, sets the sliders and grid size, and chooses how many
// times to run it. The server runs it that many times in the background (no
// animation, a different random seed for each run), and this panel shows a
// per-run table, the average / lowest / highest / spread of each value, and a
// chart of the average over time with the lowest-to-highest range shaded.
// "Compare two settings" runs a second batch with other settings and shows
// both side by side.
//
// Self-contained on purpose: it adds its own Compare button next to the other
// controls and talks to its own hub (/comparisonHub), so it does not depend on
// the live-run code in index.html. It needs Chart.js and signalR, which
// index.html already loads.
(function () {
    "use strict";

    const COLOURS = { A: "#2e6be6", B: "#e8590c" };
    const MIN_RUNS = 2, MAX_RUNS = 100, MIN_GRID = 10, MAX_GRID = 100;

    // Sliders per model. `key` is the SimulationConfig property name (camelCase)
    // the server expects. `scale: 100` shows a 0-1 probability as a percentage.
    // `density` is not a config value: it is the share of the grid that is
    // filled, converted to an agent count from the grid size when sending.
    const PARAMS = {
        SIR: [
            { key: "agentCount", label: "Number of agents", min: 10, max: 1000, step: 10 },
            { key: "initialInfected", label: "Initially infected", min: 1, max: 100, step: 1 },
            { key: "infectionProbability", label: "Infection chance", min: 0, max: 1, step: 0.01, digits: 2 },
            { key: "recoveryTicks", label: "Recovery time (ticks)", min: 1, max: 200, step: 1 }
        ],
        Schelling: [
            { key: "density", label: "Density (grid filled)", min: 5, max: 100, step: 1, unit: "%" },
            { key: "similarityThreshold", label: "Similarity threshold", min: 0, max: 100, step: 1, unit: "%", scale: 100 },
            { key: "groupARatio", label: "Share in group A", min: 0, max: 100, step: 1, unit: "%", scale: 100 }
        ],
        Boids: [
            { key: "agentCount", label: "Number of boids", min: 5, max: 300, step: 5 },
            { key: "perceptionRadius", label: "Vision radius", min: 0.5, max: 10, step: 0.5, digits: 1 },
            { key: "separationWeight", label: "Separation", min: 0, max: 5, step: 0.1, digits: 1 },
            { key: "alignmentWeight", label: "Alignment", min: 0, max: 5, step: 0.1, digits: 1 },
            { key: "cohesionWeight", label: "Cohesion", min: 0, max: 5, step: 0.1, digits: 1 },
            { key: "maxSpeed", label: "Max speed", min: 0.1, max: 3, step: 0.1, digits: 1 }
        ],
        AntForaging: [
            { key: "agentCount", label: "Number of ants", min: 5, max: 200, step: 5 },
            { key: "foodSources", label: "Food piles", min: 1, max: 10, step: 1 },
            { key: "foodPerSource", label: "Food per pile", min: 10, max: 200, step: 10 },
            { key: "pheromoneDepositAmount", label: "Pheromone dropped", min: 0.5, max: 20, step: 0.5, digits: 1 },
            { key: "pheromoneDecayRate", label: "Pheromone decay rate", min: 0, max: 0.2, step: 0.005, digits: 3 },
            { key: "explorationChance", label: "Exploration chance", min: 0, max: 1, step: 0.01, digits: 2 }
        ],
        WolfSheep: [
            { key: "initialSheep", label: "Initial sheep", min: 0, max: 500, step: 5 },
            { key: "initialWolves", label: "Initial wolves", min: 0, max: 200, step: 5 },
            { key: "grassRegrowthProbability", label: "Grass regrowth chance", min: 0, max: 1, step: 0.01, digits: 2 },
            { key: "grassEnergyGain", label: "Sheep gain from food", min: 0.5, max: 20, step: 0.5, digits: 1 },
            { key: "sheepEnergyGain", label: "Wolf gain from food", min: 0.5, max: 30, step: 0.5, digits: 1 },
            { key: "sheepReproductionProbability", label: "Sheep reproduce", min: 0, max: 50, step: 1, unit: "%", scale: 100 },
            { key: "wolfReproductionProbability", label: "Wolf reproduce", min: 0, max: 50, step: 1, unit: "%", scale: 100 }
        ]
    };

    const state = {
        connection: null,
        models: [],
        model: null,
        defaults: null,       // server defaults for the chosen model
        twoSettings: false,
        running: false,
        results: [],          // [{ label, summary, settings }]
        chart: null,
        chartKey: null
    };

    let el = {};              // cached elements, filled in by build()

    // ---------------------------------------------------------------- helpers

    const esc = (s) => String(s).replace(/[&<>"']/g, (c) =>
        ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));

    const clamp = (v, lo, hi) => Math.min(hi, Math.max(lo, v));

    function fmt(v, digits) {
        if (!Number.isFinite(v)) return "-";
        if (digits !== undefined) return v.toFixed(digits);
        return Number.isInteger(v) ? String(v) : v.toFixed(2);
    }

    function fmtCol(col, v) {
        return fmt(v) + (col.unit ? " " + col.unit : "");
    }

    // SignalR wraps a server HubException message in extra text.
    function cleanError(err) {
        const msg = (err && err.message) ? err.message : String(err);
        const m = msg.match(/HubException:\s*(.*)$/s);
        return (m ? m[1] : msg.replace(/^Error:\s*/, "")).trim();
    }

    // ------------------------------------------------------------------ build

    function build() {
        const overlay = document.createElement("div");
        overlay.className = "cmp cmp-overlay";
        overlay.setAttribute("role", "dialog");
        overlay.setAttribute("aria-modal", "true");
        overlay.setAttribute("aria-label", "Compare runs");
        overlay.innerHTML = `
            <div class="cmp-sheet">
                <div class="cmp-head">
                    <div class="cmp-grow">
                        <h2>Compare runs</h2>
                        <p>Run a model many times with different random seeds and see how much the results vary.</p>
                    </div>
                    <button type="button" class="cmp-close" id="cmpClose" aria-label="Close">&times;</button>
                </div>
                <div class="cmp-body">
                    <div class="cmp-card">
                        <div class="cmp-topbar">
                            <label class="cmp-field"><span>Model</span><select id="cmpModel" class="cmp-wide"></select></label>
                            <label class="cmp-field"><span>Number of runs (${MIN_RUNS}-${MAX_RUNS})<span class="info" tabindex="0" data-tip="How many times to run the model. Each run gets a different random seed, so you see how much the results change by chance. More runs give a more reliable average but take longer.">i</span></span>
                                <input type="number" id="cmpRuns" min="${MIN_RUNS}" max="${MAX_RUNS}" step="1" value="20"></label>
                            <label class="cmp-field"><span>Base seed (optional)<span class="info" tabindex="0" data-tip="Leave blank for different results every time. Type a number to get exactly the same set of runs again. Each run gets its own seed made from this one.">i</span></span>
                                <input type="number" id="cmpSeed" step="1" placeholder="random"></label>
                            <label class="cmp-toggle"><input type="checkbox" id="cmpTwo"> Compare two settings side by side<span class="info" tabindex="0" data-tip="Set up two versions of the settings (A and B), run both the same number of times and see how the results differ.">i</span></label>
                        </div>
                    </div>

                    <div class="cmp-sets" id="cmpSets"></div>

                    <div class="cmp-error" id="cmpError" role="alert" hidden></div>

                    <div class="cmp-actions">
                        <button type="button" class="primary" id="cmpRun">Run comparison</button>
                        <button type="button" id="cmpCancel" hidden>Cancel</button>
                        <button type="button" id="cmpCsv" hidden>Download summary CSV</button>
                    </div>

                    <div class="cmp-card" id="cmpProgressCard" hidden>
                        <div class="cmp-progress-text"><span id="cmpProgressLabel"></span><strong id="cmpProgressPct"></strong></div>
                        <div class="cmp-bar" id="cmpBar"><div></div></div>
                    </div>

                    <div id="cmpResults"></div>
                </div>
            </div>`;
        document.body.appendChild(overlay);

        el = {
            overlay,
            close: overlay.querySelector("#cmpClose"),
            model: overlay.querySelector("#cmpModel"),
            runs: overlay.querySelector("#cmpRuns"),
            seed: overlay.querySelector("#cmpSeed"),
            two: overlay.querySelector("#cmpTwo"),
            sets: overlay.querySelector("#cmpSets"),
            error: overlay.querySelector("#cmpError"),
            run: overlay.querySelector("#cmpRun"),
            cancel: overlay.querySelector("#cmpCancel"),
            csv: overlay.querySelector("#cmpCsv"),
            progressCard: overlay.querySelector("#cmpProgressCard"),
            progressLabel: overlay.querySelector("#cmpProgressLabel"),
            progressPct: overlay.querySelector("#cmpProgressPct"),
            bar: overlay.querySelector("#cmpBar"),
            results: overlay.querySelector("#cmpResults")
        };

        el.close.addEventListener("click", closePanel);
        overlay.addEventListener("mousedown", (e) => { if (e.target === overlay) closePanel(); });
        document.addEventListener("keydown", (e) => { if (e.key === "Escape" && el.overlay.classList.contains("open")) closePanel(); });

        el.model.addEventListener("change", () => loadModel(el.model.value));
        el.two.addEventListener("change", () => { state.twoSettings = el.two.checked; renderSets(); });
        el.run.addEventListener("click", runComparison);
        el.cancel.addEventListener("click", () => connection().then((c) => c.invoke("CancelComparison")).catch(() => { }));
        el.csv.addEventListener("click", downloadCsv);
    }

    function addButton() {
        const button = document.createElement("button");
        button.type = "button";
        button.id = "compareButton";
        button.textContent = "Compare";
        button.title = "Run a model many times and compare the results";
        button.addEventListener("click", openPanel);

        const host = document.querySelector(".controls");
        const anchor = document.getElementById("downloadButton");
        if (host && anchor && anchor.parentElement === host) anchor.after(button);
        else if (host) host.appendChild(button);
        else document.querySelector("header")?.appendChild(button);
    }

    // ------------------------------------------------------------ connection

    function connection() {
        if (!state.connection) {
            const c = new signalR.HubConnectionBuilder()
                .withUrl("/comparisonHub")
                .withAutomaticReconnect()
                .build();
            c.serverTimeoutInMilliseconds = 120000;
            c.on("ComparisonProgress", onProgress);
            state.connection = c;
            state.starting = c.start();
        }
        return state.starting.then(() => state.connection);
    }

    // ------------------------------------------------------------- open/close

    async function openPanel() {
        el.overlay.classList.add("open");
        document.body.style.overflow = "hidden";
        if (state.models.length === 0) {
            try {
                const c = await connection();
                state.models = await c.invoke("GetModels");
                el.model.innerHTML = state.models
                    .map((m) => `<option value="${esc(m.model)}">${esc(m.displayName)}</option>`).join("");
                await loadModel(el.model.value);
            } catch (err) {
                showError("Could not reach the server: " + cleanError(err));
            }
        }
    }

    function closePanel() {
        el.overlay.classList.remove("open");
        document.body.style.overflow = "";
    }

    // ---------------------------------------------------------------- settings

    async function loadModel(model) {
        hideError();
        state.model = model;
        const c = await connection();
        state.defaults = await c.invoke("GetDefaults", model);
        // A different model means different settings, so start fresh.
        state.results = [];
        el.results.innerHTML = "";
        el.progressCard.hidden = true;
        el.csv.hidden = true;
        destroyChart();
        el.sets.innerHTML = "";     // the old model's values do not carry over
        renderSets();
    }

    function renderSets() {
        const labels = state.twoSettings ? ["A", "B"] : ["A"];

        // Keep what is already on screen: A keeps its values and B starts as
        // a copy of A, so only the setting being compared needs changing.
        const previousA = readSet("A");

        el.sets.className = "cmp-sets" + (state.twoSettings ? " two" : "");
        el.sets.innerHTML = "";

        labels.forEach((label) => el.sets.appendChild(buildSet(label, previousA)));

        updateDifferences();
    }

    function buildSet(label, start) {
        const d = state.defaults;
        const lower = label.toLowerCase();
        const wrap = document.createElement("div");
        wrap.className = `cmp-card cmp-set ${lower}`;
        wrap.dataset.set = label;

        const model = state.models.find((m) => m.model === state.model);
        const title = state.twoSettings ? `Setting ${label}` : "Settings";

        const params = (PARAMS[state.model] || []).map((p) => {
            let value;
            if (start && start[p.key] !== undefined) value = start[p.key];
            else if (p.key === "density") value = Math.round(d.agentCount / (d.gridWidth * d.gridHeight) * 100);
            else value = d[p.key] * (p.scale || 1);

            value = Math.round(value * 1000) / 1000;
            const max = Math.max(p.max, value);   // never hide a default outside the usual range
            return `
                <div class="cmp-slider" data-key="${p.key}">
                    <label for="cmp-${label}-${p.key}">${esc(p.label)}</label>
                    <span class="val"><span class="num">${fmt(value, p.digits)}</span>${p.unit ? " " + p.unit : ""}</span>
                    <input type="range" id="cmp-${label}-${p.key}" data-key="${p.key}"
                           min="${p.min}" max="${max}" step="${p.step}" value="${value}">
                </div>`;
        }).join("");

        const g = (key, fallback) => (start && start[key] !== undefined) ? start[key] : fallback;

        wrap.innerHTML = `
            <div class="cmp-set-title">
                ${state.twoSettings ? `<span class="cmp-badge ${lower}">${label}</span>` : ""}
                <h3>${title}<small>${esc(model ? model.displayName : "")}</small></h3>
            </div>
            <div class="cmp-grid-row">
                <label class="cmp-field"><span>Grid width</span>
                    <input type="number" data-key="gridWidth" min="${MIN_GRID}" max="${MAX_GRID}" value="${g("gridWidth", d.gridWidth)}"></label>
                <span class="cmp-by">&times;</span>
                <label class="cmp-field"><span>Grid height</span>
                    <input type="number" data-key="gridHeight" min="${MIN_GRID}" max="${MAX_GRID}" value="${g("gridHeight", d.gridHeight)}"></label>
                <label class="cmp-field"><span>Run length (ticks)</span>
                    <input type="number" data-key="tickLimit" min="1" max="5000" value="${g("tickLimit", d.tickLimit)}"></label>
                <label class="cmp-field"><span>Scheduling<span class="info" tabindex="0" data-tip="The order agents take their turn each tick. Random shuffles them every tick. Sequential uses the same order every tick.">i</span></span>
                    <select data-key="scheduler">
                        <option value="Random">Random</option>
                        <option value="Sequential">Sequential</option>
                    </select></label>
                <label class="cmp-toggle"><input type="checkbox" data-key="wrap"> Wrap around edges<span class="info" tabindex="0" data-tip="When on, an agent walking off one edge comes back on the other side. When off, the edges are walls.">i</span></label>
            </div>
            <div class="cmp-sliders">${params}</div>`;

        wrap.querySelector('[data-key="scheduler"]').value = g("scheduler", d.scheduler);
        wrap.querySelector('[data-key="wrap"]').checked = start && start.wrap !== undefined
            ? start.wrap : d.topology === "Toroidal";

        // Live value next to each slider.
        wrap.querySelectorAll('input[type="range"]').forEach((input) => {
            const spec = (PARAMS[state.model] || []).find((p) => p.key === input.dataset.key);
            input.addEventListener("input", () => {
                input.parentElement.querySelector(".num").textContent = fmt(parseFloat(input.value), spec.digits);
                updateDifferences();
            });
        });
        wrap.querySelectorAll("input:not([type=range]), select").forEach((i) => i.addEventListener("input", updateDifferences));

        return wrap;
    }

    // Reads one settings card into { key: displayedValue }.
    function readSet(label) {
        const card = el.sets.querySelector(`[data-set="${label}"]`);
        if (!card) return null;
        const out = {};
        card.querySelectorAll("[data-key]").forEach((i) => {
            if (!i.matches("input, select")) return;
            if (i.type === "checkbox") out[i.dataset.key] = i.checked;
            else if (i.tagName === "SELECT") out[i.dataset.key] = i.value;
            else out[i.dataset.key] = parseFloat(i.value);
        });
        return out;
    }

    // Turns what is on screen into the settings the server expects.
    function toServerSettings(values) {
        const settings = {
            gridWidth: Math.round(values.gridWidth),
            gridHeight: Math.round(values.gridHeight),
            tickLimit: Math.round(values.tickLimit),
            scheduler: values.scheduler,
            topology: values.wrap ? "Toroidal" : "Bounded"
        };

        (PARAMS[state.model] || []).forEach((p) => {
            const v = values[p.key];
            if (p.key === "density") {
                settings.agentCount = Math.round(v / 100 * settings.gridWidth * settings.gridHeight);
            } else {
                settings[p.key] = v / (p.scale || 1);
            }
        });
        return settings;
    }

    // In "two settings" mode, shade the sliders in B that differ from A.
    function updateDifferences() {
        el.sets.querySelectorAll(".cmp-slider.differs").forEach((s) => s.classList.remove("differs"));
        if (!state.twoSettings) return;
        const a = readSet("A"), b = readSet("B");
        if (!a || !b) return;
        el.sets.querySelectorAll('[data-set="B"] .cmp-slider').forEach((row) => {
            const key = row.dataset.key;
            if (a[key] !== b[key]) row.classList.add("differs");
        });
    }

    // -------------------------------------------------------------------- run

    function validateInputs() {
        const runs = parseInt(el.runs.value, 10);
        if (!Number.isInteger(runs) || runs < MIN_RUNS || runs > MAX_RUNS) {
            return `Number of runs must be a whole number from ${MIN_RUNS} to ${MAX_RUNS}.`;
        }
        if (el.seed.value.trim() !== "" && !Number.isInteger(Number(el.seed.value))) {
            return "The base seed must be a whole number, or left empty.";
        }
        for (const label of state.twoSettings ? ["A", "B"] : ["A"]) {
            const v = readSet(label);
            const where = state.twoSettings ? ` (setting ${label})` : "";
            for (const dim of ["gridWidth", "gridHeight"]) {
                if (!Number.isInteger(v[dim]) || v[dim] < MIN_GRID || v[dim] > MAX_GRID) {
                    return `Grid width and height must each be a whole number from ${MIN_GRID} to ${MAX_GRID}${where}.`;
                }
            }
            if (!Number.isInteger(v.tickLimit) || v.tickLimit < 1) {
                return `Run length must be a whole number of ticks${where}.`;
            }
            const agents = el.model.value === "WolfSheep"
                ? (v.initialSheep || 0) + (v.initialWolves || 0)
                : (v.agentCount || 0);
            if (agents > v.gridWidth * v.gridHeight) {
                return `Too many agents for a ${v.gridWidth} x ${v.gridHeight} grid${where}. It only has ${v.gridWidth * v.gridHeight} cells but you asked for ${agents} agents. Make the grid bigger or use fewer agents.`;
            }
        }
        return null;
    }

    async function runComparison() {
        if (state.running) return;
        hideError();

        const problem = validateInputs();
        if (problem) return showError(problem);

        const runs = parseInt(el.runs.value, 10);
        const seed = el.seed.value.trim() === "" ? null : parseInt(el.seed.value, 10);
        const labels = state.twoSettings ? ["A", "B"] : ["A"];

        // Same base seed for both settings, so a difference between A and B
        // comes from the settings and not from luck with the seeds.
        const baseSeed = seed ?? Math.floor(Math.random() * 2147483647);

        state.running = true;
        state.results = [];
        state.runs = runs;
        state.sets = labels.length;
        state.lastCompleted = 0;
        state.lastLabel = "";
        el.run.disabled = true;
        el.cancel.hidden = false;
        el.csv.hidden = true;
        el.results.innerHTML = "";
        destroyChart();
        setProgress(0, labels[0], 0);
        el.progressCard.hidden = false;
        el.bar.classList.remove("done");

        try {
            const c = await connection();
            for (let i = 0; i < labels.length; i++) {
                const label = labels[i];
                state.currentIndex = i;
                const settings = toServerSettings(readSet(label));
                const summary = await c.invoke("RunComparison", {
                    model: state.model,
                    runs,
                    seed: baseSeed,
                    label,
                    settings
                });
                state.results.push({ label, summary, settings: readSet(label) });
            }

            el.bar.classList.add("done");
            el.progressLabel.textContent = `Finished ${runs} run${runs === 1 ? "" : "s"}` +
                (labels.length > 1 ? " for each setting" : "");
            el.progressPct.textContent = "100%";
            el.bar.firstElementChild.style.width = "100%";
            renderResults();
            el.csv.hidden = false;
        } catch (err) {
            el.progressCard.hidden = true;
            showError(cleanError(err));
        } finally {
            state.running = false;
            el.run.disabled = false;
            el.cancel.hidden = true;
        }
    }

    function onProgress(p) {
        if (!state.running) return;
        // Updates can arrive out of order; the bar only moves forward.
        if (p.completed < (state.lastCompleted || 0) && p.label === state.lastLabel) return;
        state.lastCompleted = p.completed;
        state.lastLabel = p.label;
        setProgress(p.completed, p.label, state.currentIndex || 0);
    }

    function setProgress(completed, label, setIndex) {
        const runs = state.runs || 1;
        const overall = ((setIndex * runs) + completed) / (runs * (state.sets || 1));
        const where = state.twoSettings ? `Setting ${label}: ` : "";
        const shown = Math.min(runs, completed + 1);
        el.progressLabel.textContent = completed >= runs
            ? `${where}finishing up...`
            : `${where}Run ${shown} of ${runs}`;
        el.progressPct.textContent = Math.round(overall * 100) + "%";
        el.bar.firstElementChild.style.width = (overall * 100) + "%";
    }

    // ---------------------------------------------------------------- results

    function renderResults() {
        const two = state.results.length > 1;
        const first = state.results[0].summary;
        const modelName = (state.models.find((m) => m.model === state.model) || {}).displayName || state.model;

        let html = `
            <div class="cmp-card">
                <div class="cmp-results-head">
                    <h3>${esc(modelName)}: ${first.runCount} runs${two ? " per setting" : ""}</h3>
                    <span class="cmp-chart-note">Base seed ${first.baseSeed} &middot; ${first.tickLimit} ticks &middot; type the seed into the seed box to repeat this exactly</span>
                </div>
            </div>`;

        // Headline cards: average +/- spread for the first few values.
        html += `<div class="cmp-card"><h3>Averages across all runs<span class="info" tabindex="0" data-tip="The average final value over all the runs. The number after &plusmn; is the spread (standard deviation), and the range is the lowest to the highest run.">i</span></h3><div class="cmp-kpis">`;
        const kpiCols = first.columns.slice(0, 4);
        kpiCols.forEach((col, ci) => {
            state.results.forEach((r) => {
                const s = r.summary.stats[ci];
                html += `<div class="cmp-kpi ${r.label.toLowerCase()}">
                    <div class="k-label">${esc(col.label)}${two ? " &middot; " + r.label : ""}</div>
                    <div class="k-value">${esc(fmtCol(col, s.mean))}</div>
                    <div class="k-sub">&plusmn; ${fmt(s.stdDev)} &middot; ${fmt(s.min)} to ${fmt(s.max)}</div>
                </div>`;
            });
        });
        html += `</div></div>`;

        // Chart
        const seriesKeys = first.series.map((s) => s.key);
        const chartOptions = seriesKeys.map((k) => {
            const col = first.columns.find((c) => c.key === k);
            return `<option value="${esc(k)}">${esc(col.seriesLabel || col.label)}</option>`;
        }).join("");
        html += `
            <div class="cmp-card">
                <div class="cmp-chart-top">
                    <h3>Average over time<span class="info" tabindex="0" data-tip="The line is the average of all runs at each tick. The shaded band goes from the lowest run to the highest run, so a wide band means the runs turned out very differently.">i</span></h3>
                    <label class="cmp-field"><span>Show</span><select id="cmpChartKey">${chartOptions}</select></label>
                </div>
                <div class="cmp-chart-wrap"><canvas id="cmpChart"></canvas></div>
                <p class="cmp-chart-note">The line is the average of all runs at each tick. The shaded band runs from the lowest to the highest run.</p>
            </div>`;

        // Per-run tables + statistics
        html += `<div class="cmp-tables${two ? " two" : ""}">`;
        state.results.forEach((r) => { html += runTableHtml(r, two); });
        html += `</div>`;

        // Difference between the two settings
        if (two) html += differenceHtml();

        el.results.innerHTML = html;

        const keySelect = el.results.querySelector("#cmpChartKey");
        keySelect.addEventListener("change", () => drawChart(keySelect.value));
        drawChart(state.chartKey && seriesKeys.includes(state.chartKey) ? state.chartKey : seriesKeys[0]);
        keySelect.value = state.chartKey;
    }

    function runTableHtml(result, two) {
        const { summary, label } = result;
        const cols = summary.columns;
        const lower = label.toLowerCase();

        const head = `<tr><th>Run</th><th>Seed</th>${cols.map((c) => `<th>${esc(c.label)}</th>`).join("")}</tr>`;
        const rows = summary.rows.map((r) =>
            `<tr><td>${r.run}</td><td>${r.seed}</td>${r.values.map((v, i) => `<td>${esc(fmtCol(cols[i], v))}</td>`).join("")}</tr>`).join("");

        const statRow = (name, pick) =>
            `<tr><td>${name}</td>${summary.stats.map((s, i) => `<td>${esc(fmtCol(cols[i], pick(s)))}</td>`).join("")}</tr>`;

        return `
            <div class="cmp-card">
                <div class="cmp-set-title">
                    ${two ? `<span class="cmp-badge ${lower}">${label}</span>` : ""}
                    <h3>${two ? `Setting ${label}: ` : ""}Results for each run<span class="info" tabindex="0" data-tip="One row per run with its seed and its values at the end of the run. Put a seed into the Random seed box on the dashboard, with the same settings, to watch that exact run.">i</span></h3>
                </div>
                <div class="cmp-scroll"><table><thead>${head}</thead><tbody>${rows}</tbody></table></div>
                <div class="cmp-scroll-x"><table class="stats ${lower}">
                    <thead><tr><th>All ${summary.runCount} runs</th>${cols.map((c) => `<th>${esc(c.label)}</th>`).join("")}</tr></thead>
                    <tbody>
                        ${statRow("Average", (s) => s.mean)}
                        ${statRow("Lowest", (s) => s.min)}
                        ${statRow("Highest", (s) => s.max)}
                        ${statRow('Spread (std. dev.)<span class="info" tabindex="0" data-tip="Standard deviation. Roughly how far a typical run ends up from the average. Small means the runs mostly agree, big means chance plays a big part.">i</span>', (s) => s.stdDev)}
                    </tbody>
                </table></div>
            </div>`;
    }

    function differenceHtml() {
        const [a, b] = state.results.map((r) => r.summary);
        const rows = a.columns.map((col, i) => {
            const diff = b.stats[i].mean - a.stats[i].mean;
            const pct = a.stats[i].mean !== 0 ? (diff / Math.abs(a.stats[i].mean)) * 100 : null;
            return `<tr>
                <td>${esc(col.label)}</td>
                <td>${esc(fmtCol(col, a.stats[i].mean))}</td>
                <td>${esc(fmtCol(col, b.stats[i].mean))}</td>
                <td><strong>${diff > 0 ? "+" : ""}${fmt(diff)}${col.unit ? " " + col.unit : ""}</strong></td>
                <td>${pct === null ? "-" : (pct > 0 ? "+" : "") + pct.toFixed(1) + "%"}</td>
            </tr>`;
        }).join("");

        return `
            <div class="cmp-card">
                <h3>Setting B compared with setting A<span class="info" tabindex="0" data-tip="B minus A is how much higher or lower B&#39;s average is than A&#39;s. Change is that difference as a percentage of A.">i</span> <small>average of each value</small></h3>
                <div class="cmp-scroll"><table>
                    <thead><tr><th>Value</th><th>A average</th><th>B average</th><th>B minus A</th><th>Change</th></tr></thead>
                    <tbody>${rows}</tbody>
                </table></div>
            </div>`;
    }

    // ------------------------------------------------------------------ chart

    function destroyChart() {
        if (state.chart) { state.chart.destroy(); state.chart = null; }
    }

    function drawChart(key) {
        state.chartKey = key;
        destroyChart();

        const canvas = document.getElementById("cmpChart");
        if (!canvas) return;

        const datasets = [];
        state.results.forEach((r) => {
            const series = r.summary.series.find((s) => s.key === key);
            if (!series) return;
            const colour = COLOURS[r.label];
            const pts = (arr) => series.ticks.map((t, i) => ({ x: t, y: arr[i] }));
            const name = state.results.length > 1 ? `Setting ${r.label}` : "Average";

            // Lowest (invisible line), then highest filled down to it, then the average on top.
            datasets.push({ label: name + " lowest", data: pts(series.min), borderWidth: 0, pointRadius: 0, fill: false, isBand: true });
            datasets.push({
                label: name + " highest", data: pts(series.max), borderWidth: 0, pointRadius: 0,
                fill: "-1", backgroundColor: colour + "30", isBand: true
            });
            datasets.push({
                label: name, data: pts(series.mean), borderColor: colour, backgroundColor: colour,
                borderWidth: 2.5, pointRadius: 0, tension: 0.15, fill: false
            });
        });

        const col = state.results[0].summary.columns.find((c) => c.key === key);

        state.chart = new Chart(canvas, {
            type: "line",
            data: { datasets },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                animation: false,
                parsing: false,
                interaction: { mode: "nearest", axis: "x", intersect: false },
                scales: {
                    x: { type: "linear", title: { display: true, text: "Tick" }, ticks: { precision: 0 } },
                    y: { title: { display: true, text: (col.seriesLabel || col.label) + (col.unit ? " (" + col.unit + ")" : "") }, beginAtZero: true }
                },
                plugins: {
                    legend: { labels: { filter: (item, data) => !data.datasets[item.datasetIndex].isBand } },
                    tooltip: { filter: (item) => !item.dataset.isBand }
                }
            }
        });
    }

    // -------------------------------------------------------------------- csv

    function csvCell(v) {
        const s = String(v);
        return /[",\r\n]/.test(s) ? '"' + s.replace(/"/g, '""') + '"' : s;
    }

    // Rounds long decimals (0.08333333333333333) to 4 places for the file.
    const csvNum = (v) => (typeof v === "number" && !Number.isInteger(v)) ? Math.round(v * 10000) / 10000 : v;

    const csvLine = (cells) => cells.map((c) => csvCell(csvNum(c))).join(",");

    function buildCsv() {
        const lines = [];
        const two = state.results.length > 1;
        const first = state.results[0].summary;

        lines.push(csvLine(["Compare runs summary"]));
        lines.push(csvLine(["Model", first.model]));
        lines.push(csvLine(["Runs per setting", first.runCount]));
        lines.push(csvLine(["Base seed", first.baseSeed]));
        lines.push(csvLine(["Ticks per run", first.tickLimit]));

        state.results.forEach((r) => {
            const { summary: s, label } = r;
            const prefix = two ? `Setting ${label}` : "";
            lines.push("");
            if (two) lines.push(csvLine([prefix]));
            lines.push(csvLine(["Settings", Object.entries(r.settings).map(([k, v]) => `${k}=${v}`).join("; ")]));

            lines.push("");
            lines.push(csvLine(["Run", "Seed", ...s.columns.map((c) => c.label)]));
            s.rows.forEach((row) => lines.push(csvLine([row.run, row.seed, ...row.values])));

            lines.push("");
            lines.push(csvLine(["Statistic", ...s.columns.map((c) => c.label)]));
            [["Average", "mean"], ["Lowest", "min"], ["Highest", "max"], ["Std deviation", "stdDev"]]
                .forEach(([name, field]) => lines.push(csvLine([name, ...s.stats.map((st) => st[field])])));

            if (s.series.length) {
                lines.push("");
                lines.push(csvLine(["Over time (average, lowest, highest across runs)"]));
                const header = ["Tick"];
                s.series.forEach((se) => {
                    const col = s.columns.find((c) => c.key === se.key);
                    const name = col.seriesLabel || col.label;
                    header.push(`${name} average`, `${name} lowest`, `${name} highest`);
                });
                lines.push(csvLine(header));
                const n = s.series[0].ticks.length;
                for (let i = 0; i < n; i++) {
                    const row = [s.series[0].ticks[i]];
                    s.series.forEach((se) => row.push(se.mean[i], se.min[i], se.max[i]));
                    lines.push(csvLine(row));
                }
            }
        });

        if (two) {
            const [a, b] = state.results.map((r) => r.summary);
            lines.push("");
            lines.push(csvLine(["Setting B compared with setting A (average)"]));
            lines.push(csvLine(["Value", "A average", "B average", "B minus A"]));
            a.columns.forEach((c, i) =>
                lines.push(csvLine([c.label, a.stats[i].mean, b.stats[i].mean, b.stats[i].mean - a.stats[i].mean])));
        }

        return lines.join("\r\n") + "\r\n";
    }

    function downloadCsv() {
        if (state.results.length === 0) return;
        // The BOM makes Excel read the file as UTF-8.
        const blob = new Blob(["\uFEFF" + buildCsv()], { type: "text/csv;charset=utf-8" });
        const stamp = new Date().toISOString().replace(/[-:T]/g, "").slice(0, 14);
        const url = URL.createObjectURL(blob);
        const a = document.createElement("a");
        a.href = url;
        a.download = `compare-${state.model}-${stamp}.csv`;
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }

    // ------------------------------------------------------------------ errors

    function showError(message) {
        el.error.textContent = message;
        el.error.hidden = false;
    }

    function hideError() {
        el.error.hidden = true;
        el.error.textContent = "";
    }

    // ------------------------------------------------------------------- start

    function init() {
        build();
        addButton();
    }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init);
    else init();
})();
