const canvas = document.getElementById("grid");
const ctx = canvas.getContext("2d");
const legend = document.getElementById("legend");

let targetSize = 480;
let gridWidth = 20;
let gridHeight = 20;
let cellSize = targetSize / 20;

const stateStyles = {
    "Susceptible": { color: "#3366cc", label: "Susceptible", icon: "person" },
    "Infected": { color: "#dc3545", label: "Infected", icon: "person" },
    "Recovered": { color: "#28a745", label: "Recovered", icon: "person" },
    "Group0-Happy": { color: "#3366cc", label: "Group A, happy", icon: "happy" },
    "Group0-Unhappy": { color: "#7fa3e8", label: "Group A, unhappy", icon: "unhappy" },
    "Group1-Happy": { color: "#dc3545", label: "Group B, happy", icon: "happy" },
    "Group1-Unhappy": { color: "#ef8c97", label: "Group B, unhappy", icon: "unhappy" },
    "BoidAgent": { color: "#3366cc", label: "Boid", icon: "boid" },
    "Searching": { color: "#5d4037", label: "Searching for food", icon: "ant" },
    "ReturningWithFood": { color: "#5d4037", label: "Carrying food home", icon: "antWithFood" },
    "Sheep": { color: "#f1c40f", label: "Sheep", icon: "sheep" },
    "Wolf": { color: "#34495e", label: "Wolves", icon: "wolf" }
};

// Patch (grid cell) colours, like NetLogo's patches. Shown as square swatches in the legend.
const patchStyles = {
    "Grass": { color: "#8dbf78", label: "Grass" },
    "BareGround": { color: "#b08d6a", label: "Eaten grass" },
    "Nest": { color: "#a0703c", label: "Nest", icon: "anthill" },
    "Food": { color: "#ff9800", label: "Food" },
    "Pheromone": { color: "#00acc1", label: "Pheromone trail" }
};

// Display switches (like NetLogo's show-energy). The page flips these and calls redrawGrid().
const displayOptions = {
    showGrass: true,
    showFood: true,
    showPheromone: true,
    showEnergy: false
};

// Cells smaller than this are too small to fit a readable energy label.
const minLabelCellSize = 9;

const fallbackPalette = ["#ffc107", "#9b59b6", "#17a2b8", "#e67e22", "#6c757d"];
let fallbackIndex = 0;
let seenStates = [];
let lastAgents = [];
let lastGrid = null;
let foodReference = 1;
let selectedAgentId = null;
let selectedAgentIds = [];
const MAX_SELECTED = 6;
const selectionColors = ["#ff1744", "#2979ff", "#00c853", "#ff9100", "#d500f9", "#00b8d4"];

// Agent shapes, like NetLogo's turtle shapes. Each is an SVG on a 32x32 canvas
// facing right, drawn in the colour of the agent's state.
const iconShapes = {
    anthill: () => `
        <ellipse cx="16" cy="28" rx="15" ry="2.5" fill="#000" opacity="0.18"/>
        <path d="M1.5 28 C4 16, 10 7, 16 7 C22 7, 28 16, 30.5 28 Z" fill="#a0703c" stroke="#6d4c2a" stroke-width="1.5" stroke-linejoin="round"/>
        <path d="M7.5 23 C9 16, 12 11.5, 15.5 10" stroke="#c99a62" stroke-width="2.2" fill="none" stroke-linecap="round"/>
        <ellipse cx="16" cy="11.5" rx="3.6" ry="2.4" fill="#3b2612"/>
        <g fill="#6d4c2a"><circle cx="11" cy="21" r="1.1"/><circle cx="21.5" cy="17" r="1.1"/><circle cx="24" cy="23.5" r="1.1"/><circle cx="14" cy="25" r="1.1"/><circle cx="19" cy="21.5" r="0.9"/></g>`,
    person: c => `
        <circle cx="16" cy="8" r="6" fill="${c}" stroke="white" stroke-width="1.5"/>
        <path d="M6 31 C6 20 10 15.5 16 15.5 C22 15.5 26 20 26 31 Z" fill="${c}" stroke="white" stroke-width="1.5"/>`,
    happy: c => face(c, "M10 19 Q16 25 22 19"),
    unhappy: c => face(c, "M10 23 Q16 17 22 23"),
    boid: c => `
        <path d="M31 16 L4 29 L10 16 L4 3 Z" fill="${c}" stroke="white" stroke-width="1.5" stroke-linejoin="round"/>`,
    ant: c => ant(c, ""),
    antWithFood: c => ant(c, `<circle cx="27" cy="16" r="5" fill="#ff9800" stroke="#5d4037" stroke-width="1.5"/>`),
    sheep: () => `
        <path d="M9 23 V29 M14 23 V29 M19 23 V29 M23 23 V29" stroke="#333" stroke-width="2.2" stroke-linecap="round"/>
        <g fill="#fafafa" stroke="#888" stroke-width="1">
            <circle cx="8" cy="15" r="5"/><circle cx="13" cy="11" r="5.5"/><circle cx="19" cy="11.5" r="5"/>
            <circle cx="10" cy="20" r="5"/><circle cx="17" cy="20" r="5.5"/><circle cx="21" cy="16" r="5"/>
        </g>
        <ellipse cx="26.5" cy="13.5" rx="4" ry="3.5" fill="#333"/>`,
    wolf: c => `
        <path d="M3 13 Q5 18 9 16" stroke="${c}" stroke-width="3" fill="none" stroke-linecap="round"/>
        <path d="M10 22 V29 M14 22 V29 M19 22 V29 M23 22 V29" stroke="${c}" stroke-width="2.4" stroke-linecap="round"/>
        <ellipse cx="16" cy="18" rx="9" ry="5.5" fill="${c}"/>
        <path d="M22 16 L23 6 L26 11 L28 6 L29 12 L31 15 L29.5 18 L24 19 Z" fill="${c}"/>
        <circle cx="27" cy="13" r="1" fill="#f1c40f"/>`
};

function face(c, mouth) {
    return `
        <circle cx="16" cy="16" r="14" fill="${c}" stroke="white" stroke-width="1.5"/>
        <circle cx="11" cy="12" r="2" fill="white"/><circle cx="21" cy="12" r="2" fill="white"/>
        <path d="${mouth}" stroke="white" stroke-width="2.5" fill="none" stroke-linecap="round"/>`;
}

function ant(c, extra) {
    return `
        <path d="M14 15 L10 7 M14 17 L10 25 M17 15 L17 6 M17 17 L17 26 M19 15 L23 8 M19 17 L23 24" stroke="${c}" stroke-width="1.6" stroke-linecap="round"/>
        <ellipse cx="8" cy="16" rx="6" ry="4.5" fill="${c}"/>
        <ellipse cx="16.5" cy="16" rx="3.5" ry="2.8" fill="${c}"/>
        <circle cx="23" cy="16" r="3.5" fill="${c}"/>
        ${extra}`;
}

const iconCache = {};

// Returns a loaded image for the style's shape, or null while it's still loading.
function iconFor(style) {
    if (!style.icon || !(style.icon in iconShapes)) return null;

    const key = style.icon + "|" + style.color;
    if (!(key in iconCache)) {
        const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">${iconShapes[style.icon](style.color)}</svg>`;
        const img = new Image();
        img.src = "data:image/svg+xml;charset=utf-8," + encodeURIComponent(svg);
        iconCache[key] = img;
        img.onload = () => {
            redrawGrid();
            renderLegend();
        };
    }

    const img = iconCache[key];
    return img.complete && img.naturalWidth > 0 ? img : null;
}

function styleFor(state) {
    if (state in patchStyles) return patchStyles[state];
    if (!(state in stateStyles)) {
        stateStyles[state] = { color: fallbackPalette[fallbackIndex % fallbackPalette.length], label: state };
        fallbackIndex++;
    }
    return stateStyles[state];
}

function setGridSize(width, height) {
    gridWidth = width;
    gridHeight = height;
    cellSize = Math.floor(targetSize / Math.max(width, height));
    canvas.width = gridWidth * cellSize;
    canvas.height = gridHeight * cellSize;
    clearGrid();
}

// Ants only send where they are, so the way they face is worked out from where they were last tick.
const turnsToFace = new Set(["ant", "antWithFood"]);
let facing = {};

function facingFor(a) {
    const last = facing[a.id];
    let angle = last ? last.angle : 0;
    if (last && (last.x !== a.x || last.y !== a.y)) {
        let dx = a.x - last.x;
        let dy = a.y - last.y;
        if (Math.abs(dx) > gridWidth / 2) dx -= Math.sign(dx) * gridWidth;
        if (Math.abs(dy) > gridHeight / 2) dy -= Math.sign(dy) * gridHeight;
        angle = Math.atan2(dy, dx);
    }
    facing[a.id] = { x: a.x, y: a.y, angle };
    return angle;
}

function fitGridTo(size) {
    const next = Math.max(160, Math.floor(size));
    if (next === targetSize) return;
    targetSize = next;
    cellSize = Math.max(2, Math.floor(targetSize / Math.max(gridWidth, gridHeight)));
    canvas.width = gridWidth * cellSize;
    canvas.height = gridHeight * cellSize;
    if (lastGrid) {
        redrawGrid();
    } else {
        ctx.clearRect(0, 0, canvas.width, canvas.height);
        drawGridLines();
    }
}

function clearGrid() {
    seenStates = [];
    lastAgents = [];
    lastGrid = null;
    foodReference = 1;
    selectedAgentId = null;
    selectedAgentIds = [];
    facing = {};
    legend.innerHTML = "";
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    drawGridLines();
}

function drawGridLines() {
    ctx.strokeStyle = "#d0d0d0";

    for (let y = 0; y < gridHeight; y++) {
        for (let x = 0; x < gridWidth; x++) {
            ctx.strokeRect(x * cellSize, y * cellSize, cellSize, cellSize);
        }
    }
}

function fillCell(x, y, color, alpha = 1) {
    ctx.globalAlpha = alpha;
    ctx.fillStyle = color;
    ctx.fillRect(x * cellSize, y * cellSize, cellSize, cellSize);
    ctx.globalAlpha = 1;
}

// Returns the patch layers actually drawn this frame, for the legend.
function drawPatches(grid) {
    const drawn = new Set();
    const patches = grid.patches || [];

    if (grid.hasGrass && displayOptions.showGrass) {
        ctx.fillStyle = patchStyles.Grass.color;
        ctx.fillRect(0, 0, canvas.width, canvas.height);
        drawn.add("Grass");
    }

    patches.forEach(p => {
        if (p.food > foodReference) foodReference = p.food;
    });

    // Pheromone first so food and the nest stay visible on top of a trail.
    patches.forEach(p => {
        if (p.pheromone > 0 && displayOptions.showPheromone) {
            // Log scale, so a faint trail still shows and a busy one doesn't saturate instantly.
            const strength = Math.min(0.85, Math.log1p(p.pheromone) / Math.log1p(40));
            fillCell(p.x, p.y, patchStyles.Pheromone.color, Math.max(0.12, strength));
            drawn.add("Pheromone");
        }
    });

    const nests = [];
    patches.forEach(p => {
        if (p.grass === false && grid.hasGrass && displayOptions.showGrass) {
            fillCell(p.x, p.y, patchStyles.BareGround.color);
            drawn.add("BareGround");
        }
        if (p.food > 0 && displayOptions.showFood) {
            // Piles fade as the ants carry the food away.
            fillCell(p.x, p.y, patchStyles.Food.color, 0.3 + 0.7 * (p.food / foodReference));
            drawn.add("Food");
        }
        if (p.nest && displayOptions.showFood) {
            nests.push(p);
            drawn.add("Nest");
        }
    });

    // Drawn last and a bit bigger than a cell, so the anthill sits on top of nearby trails.
    nests.forEach(drawNest);

    return drawn;
}

function drawNest(p) {
    const img = iconFor(patchStyles.Nest);
    if (!img) {
        fillCell(p.x, p.y, patchStyles.Nest.color);
        return;
    }
    const size = Math.max(14, cellSize * 2.6);
    ctx.drawImage(img, p.x * cellSize + cellSize / 2 - size / 2, p.y * cellSize + cellSize / 2 - size * 0.6, size, size);
}

function drawAgent(a, style) {
    const cx = a.x * cellSize + cellSize / 2;
    const cy = a.y * cellSize + cellSize / 2;
    const img = iconFor(style);

    if (!img) {
        ctx.fillStyle = style.color;
        ctx.beginPath();
        ctx.arc(cx, cy, Math.max(2, cellSize * 0.3), 0, Math.PI * 2);
        ctx.fill();
        return;
    }

    const size = Math.max(6, cellSize * 0.95);
    ctx.save();
    ctx.translate(cx, cy);
    const angle = typeof a.heading === "number" ? a.heading : a.facing;
    if (typeof angle === "number") ctx.rotate(angle);
    ctx.drawImage(img, -size / 2, -size / 2, size, size);
    ctx.restore();
}

function drawSelection() {
    selectedAgentIds.forEach((id, i) => {
        const a = lastAgents.find(agent => agent.id === id);
        if (a) drawRing(a, selectionColor(i));
    });
}

function selectionColor(index) {
    return selectionColors[index % selectionColors.length];
}

function drawRing(a, color) {
    const cx = a.x * cellSize + cellSize / 2;
    const cy = a.y * cellSize + cellSize / 2;
    const r = Math.max(5, cellSize * 0.7);

    ctx.lineWidth = 4;
    ctx.strokeStyle = "white";
    ctx.beginPath();
    ctx.arc(cx, cy, r, 0, Math.PI * 2);
    ctx.stroke();

    ctx.lineWidth = 2;
    ctx.strokeStyle = color;
    ctx.beginPath();
    ctx.arc(cx, cy, r, 0, Math.PI * 2);
    ctx.stroke();

    ctx.lineWidth = 1;
}

function drawEnergyLabels(agents) {
    if (!displayOptions.showEnergy || cellSize < minLabelCellSize) return;

    const fontSize = Math.max(8, Math.min(12, Math.floor(cellSize * 0.55)));
    ctx.font = `bold ${fontSize}px Arial, sans-serif`;
    ctx.textAlign = "center";
    ctx.textBaseline = "bottom";
    ctx.lineWidth = 3;
    ctx.strokeStyle = "white";
    ctx.fillStyle = "#111";

    agents.forEach(a => {
        if (a.energy === undefined || a.energy === null) return;
        const text = String(Math.round(a.energy));
        const cx = a.x * cellSize + cellSize / 2;
        const top = a.y * cellSize + 1 + fontSize;
        ctx.strokeText(text, cx, top);
        ctx.fillText(text, cx, top);
    });

    ctx.lineWidth = 1;
}

// grid = { agents, patches, hasGrass } as sent by the server's ReceiveGrid.
function drawGrid(grid) {
    lastGrid = grid;
    lastAgents = grid.agents || [];

    ctx.clearRect(0, 0, canvas.width, canvas.height);
    const patchLayers = drawPatches(grid);
    drawGridLines();

    let legendChanged = false;

    patchLayers.forEach(layer => {
        if (!seenStates.includes(layer)) {
            seenStates.push(layer);
            legendChanged = true;
        }
    });

    lastAgents.forEach(a => {
        const style = styleFor(a.state);
        if (!seenStates.includes(a.state)) {
            seenStates.push(a.state);
            legendChanged = true;
        }

        if (typeof a.heading !== "number" && turnsToFace.has(style.icon)) {
            a.facing = facingFor(a);
        }

        drawAgent(a, style);
    });

    drawEnergyLabels(lastAgents);
    drawSelection();

    if (legendChanged) {
        renderLegend();
    }

    if (typeof onGridDrawn === "function") {
        onGridDrawn(lastAgents);
    }
}

// Redraws the last frame, e.g. after a display switch changes while paused.
function redrawGrid() {
    if (!lastGrid) return;
    // Rebuild the legend so switched-off layers drop out of it.
    seenStates = [];
    drawGrid(lastGrid);
    renderLegend();
}

// Kept for older callers that pass agents and patches separately.
function drawAgents(agents, patches = []) {
    drawGrid({ agents, patches, hasGrass: false });
}

function renderLegend() {
    legend.innerHTML = seenStates
        .map(state => {
            const style = styleFor(state);
            const patchIcon = state in patchStyles && iconFor(styleFor(state));
            if (patchIcon) {
                return `<span class="legend-item"><img class="legend-icon" src="${patchIcon.src}" alt="">${style.label}</span>`;
            }
            if (state in patchStyles) {
                return `<span class="legend-item"><span class="swatch square" style="background:${style.color}"></span>${style.label}</span>`;
            }
            const img = iconFor(style);
            const marker = img
                ? `<img class="legend-icon" src="${img.src}" alt="">`
                : `<span class="swatch" style="background:${style.color}"></span>`;
            return `<span class="legend-item">${marker}${style.label}</span>`;
        })
        .join("");
}

// Converts a mouse event on the canvas into a grid cell, allowing for the canvas being scaled by CSS.
function cellFromEvent(event) {
    const rect = canvas.getBoundingClientRect();
    const x = (event.clientX - rect.left) * (canvas.width / rect.width);
    const y = (event.clientY - rect.top) * (canvas.height / rect.height);
    return { x: Math.floor(x / cellSize), y: Math.floor(y / cellSize) };
}

function agentsAtCell(x, y) {
    return lastAgents.filter(a => a.x === x && a.y === y);
}

function setSelection(ids) {
    selectedAgentIds = [...new Set(ids)].slice(0, MAX_SELECTED);
    selectedAgentId = selectedAgentIds.length ? selectedAgentIds[0] : null;
    redrawGrid();
    if (!lastGrid && typeof onGridDrawn === "function") onGridDrawn(lastAgents);
}

function selectAgent(id) {
    setSelection(id === null ? [] : [id]);
}

function selectedAgent() {
    if (selectedAgentId === null) return null;
    return lastAgents.find(a => a.id === selectedAgentId) || null;
}

setGridSize(gridWidth, gridHeight);
