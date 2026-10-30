// =============================================================================
// grid.js  (dashboard page)
//
// Draws the simulation grid on the <canvas id="grid"> and keeps the legend
// under it up to date. Loaded by index.html before its own script, so every
// function and variable here is global and used from index.html.
//
// Main entry points (called from index.html):
//   setGridSize(w, h)   new run: resize the canvas and clear it
//   drawGrid(grid)      draw one "ReceiveGrid" message (agents + patches)
//   redrawGrid()        draw the last grid again (after a display switch)
//   fitGridTo(px)       the panel was resized
//   setSelection(ids)   highlight agents picked in the Agent inspector
//   cellFromEvent(e) / agentsAtCell(x, y)   turn a mouse click into agents
//
// How a frame is drawn: clear -> patches (grass, pheromone, food, nest)
// -> grid lines -> agents (icons) -> energy labels -> selection rings.
//
// ADD A MODEL: give each of its DisplayState values an entry in stateStyles
// (colour, legend label and icon). Unknown states still work, they get a
// colour from fallbackPalette and are drawn as dots.
// =============================================================================

const canvas = document.getElementById("grid");
const ctx = canvas.getContext("2d");
const legend = document.getElementById("legend");

let targetSize = 480;               // how big the grid should be on screen, in CSS pixels
let gridWidth = 20;                // number of columns in the current run
let gridHeight = 20;               // number of rows
let cellSize = targetSize / 20;    // size of one cell in CSS pixels

// The canvas is drawn at the screen's real pixel density (devicePixelRatio), so the
// grid and icons stay sharp on high-DPI and scaled Windows displays.
function sizeCanvas() {
    const dpr = window.devicePixelRatio || 1;
    const w = gridWidth * cellSize, h = gridHeight * cellSize;
    canvas.width = Math.round(w * dpr);
    canvas.height = Math.round(h * dpr);
    canvas.style.width = w + "px";
    canvas.style.height = h + "px";
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.imageSmoothingQuality = "high";
}

// How each agent state (the agent's DisplayState from the server) is drawn:
// colour, legend text, icon shape (see iconShapes) and optional scale.
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
    "Sheep": { color: "#ffffff", label: "Sheep", icon: "sheep", scale: 1.5 },
    "Wolf": { color: "#1b1b1b", label: "Wolves", icon: "wolf", scale: 2 }
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

// Cells smaller than this (in pixels) are too small to fit a readable energy label.
const minLabelCellSize = 9;

// Colours handed out to agent states that have no entry in stateStyles.
const fallbackPalette = ["#ffc107", "#9b59b6", "#17a2b8", "#e67e22", "#6c757d"];
let fallbackIndex = 0;
let seenStates = [];           // states and patch layers drawn so far, in order (for the legend)
let lastAgents = [];           // the agents of the last drawn frame (for clicks and the inspector)
let lastGrid = null;           // the last ReceiveGrid message, so the frame can be redrawn
let foodReference = 1;         // biggest food pile seen this run, so piles fade as they shrink
let selectedAgentId = null;    // the first selected agent, or null
let selectedAgentIds = [];     // every selected agent (Ctrl+click adds more)
const MAX_SELECTED = 6;
// Ring colour for each selected agent, in selection order.
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
    // Sheep and wolf are drawn like NetLogo's Wolf Sheep model: flat side-on shapes,
    // white sheep and black wolves, a bit bigger than a cell (see "scale" above).
    sheep: () => `
        <path d="M10 21 V29 M14 21 V29 M19 21 V29 M23 21 V29" stroke="#6b6b6b" stroke-width="2.6" stroke-linecap="round"/>
        <g fill="#b5b5b5">
            <circle cx="9" cy="15" r="6.8"/><circle cx="15" cy="11.5" r="6.8"/><circle cx="21" cy="12.5" r="6.3"/>
            <circle cx="11" cy="19.5" r="5.8"/><circle cx="18" cy="19.5" r="5.8"/>
        </g>
        <g fill="#ffffff">
            <circle cx="9" cy="15" r="6"/><circle cx="15" cy="11.5" r="6"/><circle cx="21" cy="12.5" r="5.5"/>
            <circle cx="11" cy="19.5" r="5"/><circle cx="18" cy="19.5" r="5"/>
        </g>
        <path d="M24 10.5 C27 9 30.5 11 30.5 14.5 C30.5 17 28.5 18 26.5 17.5 C25 17 24 15 24 13 Z" fill="#6b6b6b"/>
        <ellipse cx="24.5" cy="10.5" rx="2.6" ry="1.4" fill="#6b6b6b"/>`,
    wolf: c => `
        <g>
            <path d="M5 14 C2.5 16 2 20 3 23 C5 20.5 7 18.5 9 17.5 Z" fill="#8c8c8c" stroke="#8c8c8c" stroke-width="1.6" stroke-linejoin="round"/>
            <path d="M9 22 V29.5 M13 22 V29.5 M19.5 22 V29.5 M23.5 22 V29.5" stroke="#8c8c8c" stroke-width="4.4" stroke-linecap="round"/>
            <path d="M7 16 C8 12.5 12 11.5 16 12 C19 12.3 21 12 22.5 11 L25 13 C25.5 17 24.5 21 22 22.5 C17 24 11 24 8.5 22.5 C6.8 21 6.5 18 7 16 Z" fill="#8c8c8c" stroke="#8c8c8c" stroke-width="1.6" stroke-linejoin="round"/>
            <path d="M21.5 11.5 L22.5 4.5 L25 8.5 L27 4.5 L28 9.5 C29.5 10.5 31 12 31.5 13.5 L28.5 15 C27 15.8 25 15.8 23.5 15 Z" fill="#8c8c8c" stroke="#8c8c8c" stroke-width="1.6" stroke-linejoin="round"/>
        </g>
        <path d="M5 14 C2.5 16 2 20 3 23 C5 20.5 7 18.5 9 17.5 Z" fill="${c}"/>
        <path d="M9 22 V29.5 M13 22 V29.5 M19.5 22 V29.5 M23.5 22 V29.5" stroke="${c}" stroke-width="2.8" stroke-linecap="round"/>
        <path d="M7 16 C8 12.5 12 11.5 16 12 C19 12.3 21 12 22.5 11 L25 13 C25.5 17 24.5 21 22 22.5 C17 24 11 24 8.5 22.5 C6.8 21 6.5 18 7 16 Z" fill="${c}"/>
        <path d="M21.5 11.5 L22.5 4.5 L25 8.5 L27 4.5 L28 9.5 C29.5 10.5 31 12 31.5 13.5 L28.5 15 C27 15.8 25 15.8 23.5 15 Z" fill="${c}"/>
        <circle cx="27" cy="11" r="0.9" fill="#f1c40f"/>`
};

// Shared shape for the Schelling faces: a coloured circle with eyes and a mouth.
function face(c, mouth) {
    return `
        <circle cx="16" cy="16" r="14" fill="${c}" stroke="white" stroke-width="1.5"/>
        <circle cx="11" cy="12" r="2" fill="white"/><circle cx="21" cy="12" r="2" fill="white"/>
        <path d="${mouth}" stroke="white" stroke-width="2.5" fill="none" stroke-linecap="round"/>`;
}

// Shared ant shape (body, head, six legs), with "extra" drawn on top (the food it carries).
function ant(c, extra) {
    return `
        <path d="M14 15 L10 7 M14 17 L10 25 M17 15 L17 6 M17 17 L17 26 M19 15 L23 8 M19 17 L23 24" stroke="${c}" stroke-width="1.6" stroke-linecap="round"/>
        <ellipse cx="8" cy="16" rx="6" ry="4.5" fill="${c}"/>
        <ellipse cx="16.5" cy="16" rx="3.5" ry="2.8" fill="${c}"/>
        <circle cx="23" cy="16" r="3.5" fill="${c}"/>
        ${extra}`;
}

// Loaded icon images, one per shape + colour, so each SVG is only built once.
const iconCache = {};

// Returns a loaded image for the style's shape, or null while it's still loading.
// The SVG is turned into an image via a "data:" URL. When it finishes loading
// the grid and legend are redrawn, so icons appear as soon as they are ready.
function iconFor(style) {
    if (!style.icon || !(style.icon in iconShapes)) return null;

    const key = style.icon + "|" + style.color;
    if (!(key in iconCache)) {
        const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="96" height="96" viewBox="0 0 32 32">${iconShapes[style.icon](style.color)}</svg>`;
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

// The style for an agent state or patch layer. A state never seen before gets
// the next fallback colour and is remembered, so it stays the same colour.
function styleFor(state) {
    if (state in patchStyles) return patchStyles[state];
    if (!(state in stateStyles)) {
        stateStyles[state] = { color: fallbackPalette[fallbackIndex % fallbackPalette.length], label: state };
        fallbackIndex++;
    }
    return stateStyles[state];
}

// Called when a new run starts: works out the cell size and clears the canvas.
function setGridSize(width, height) {
    gridWidth = width;
    gridHeight = height;
    cellSize = Math.floor(targetSize / Math.max(width, height));
    sizeCanvas();
    clearGrid();
}

// Ants only send where they are, so the way they face is worked out from where they were last tick.
const turnsToFace = new Set(["ant", "antWithFood"]);
let facing = {};

// Works out which way an ant is facing (in radians) from its last position.
// The dx/dy correction handles an ant wrapping around the grid edge, which
// would otherwise look like a big jump the other way.
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

// Called when the grid panel changes size: recalculates the cell size and redraws.
// Never smaller than 160 px or 2 px per cell.
function fitGridTo(size) {
    const next = Math.max(160, Math.floor(size));
    if (next === targetSize) return;
    targetSize = next;
    cellSize = Math.max(2, Math.floor(targetSize / Math.max(gridWidth, gridHeight)));
    sizeCanvas();
    if (lastGrid) {
        redrawGrid();
    } else {
        ctx.clearRect(0, 0, canvas.width, canvas.height);
        drawGridLines();
    }
}

// Empties the canvas, the legend and all remembered state (between runs).
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

// One path for all the lines is much faster than a rectangle per cell on big grids.
function drawGridLines() {
    const w = gridWidth * cellSize, h = gridHeight * cellSize;
    ctx.strokeStyle = "#d0d0d0";
    ctx.lineWidth = 1;
    ctx.beginPath();
    for (let x = 0; x <= gridWidth; x++) {
        ctx.moveTo(x * cellSize, 0);
        ctx.lineTo(x * cellSize, h);
    }
    for (let y = 0; y <= gridHeight; y++) {
        ctx.moveTo(0, y * cellSize);
        ctx.lineTo(w, y * cellSize);
    }
    ctx.stroke();
}

// Fills one cell with a colour, optionally see-through (alpha 0 to 1).
function fillCell(x, y, color, alpha = 1) {
    ctx.globalAlpha = alpha;
    ctx.fillStyle = color;
    ctx.fillRect(x * cellSize, y * cellSize, cellSize, cellSize);
    ctx.globalAlpha = 1;
}

// Draws the coloured cells: grass, pheromone, eaten grass, food and nests.
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

    // All the eaten-grass cells share one colour, so they are filled in one go.
    if (grid.hasGrass && displayOptions.showGrass) {
        ctx.fillStyle = patchStyles.BareGround.color;
        ctx.beginPath();
        patches.forEach(p => {
            if (p.grass === false) {
                ctx.rect(p.x * cellSize, p.y * cellSize, cellSize, cellSize);
                drawn.add("BareGround");
            }
        });
        ctx.fill();
    }

    const nests = [];
    patches.forEach(p => {
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

// Draws the anthill icon over the nest cell (or a plain cell while the icon loads).
function drawNest(p) {
    const img = iconFor(patchStyles.Nest);
    if (!img) {
        fillCell(p.x, p.y, patchStyles.Nest.color);
        return;
    }
    const size = Math.max(14, cellSize * 2.6);
    ctx.drawImage(img, p.x * cellSize + cellSize / 2 - size / 2, p.y * cellSize + cellSize / 2 - size * 0.6, size, size);
}

// Draws one agent: its icon (rotated for boids and ants), or a dot while
// the icon is still loading.
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

    // Icons fill their own cell. Only when cells get very small are sheep and wolves
    // drawn a little bigger (like NetLogo's turtle "size") so they stay visible.
    const size = style.scale
        ? Math.max(cellSize * 0.95, Math.min(cellSize * style.scale, 14))
        : Math.max(6, cellSize * 0.95);
    const sprite = spriteFor(img, size);
    const angle = typeof a.heading === "number" ? a.heading : a.facing;
    if (typeof angle === "number") {
        ctx.save();
        ctx.translate(cx, cy);
        ctx.rotate(angle);
        ctx.drawImage(sprite, -size / 2, -size / 2, size, size);
        ctx.restore();
    } else {
        ctx.drawImage(sprite, cx - size / 2, cy - size / 2, size, size);
    }
}

// Drawing an SVG image is slow when it happens thousands of times a frame, so each
// icon is turned into a small bitmap once per size and that bitmap is reused.
const spriteCache = new WeakMap();
function spriteFor(img, size) {
    const px = Math.max(1, Math.ceil(size * (window.devicePixelRatio || 1)));
    let sizes = spriteCache.get(img);
    if (!sizes) {
        sizes = new Map();
        spriteCache.set(img, sizes);
    }
    let sprite = sizes.get(px);
    if (!sprite) {
        sprite = document.createElement("canvas");
        sprite.width = px;
        sprite.height = px;
        sprite.getContext("2d").drawImage(img, 0, 0, px, px);
        sizes.set(px, sprite);
    }
    return sprite;
}

// Draws a coloured ring around every selected agent.
function drawSelection() {
    selectedAgentIds.forEach((id, i) => {
        const a = lastAgents.find(agent => agent.id === id);
        if (a) drawRing(a, selectionColor(i));
    });
}

// The ring colour for the n-th selected agent (also used by the inspector cards).
function selectionColor(index) {
    return selectionColors[index % selectionColors.length];
}

// A ring around one agent: a white outline first, then the colour, so it
// shows up on any background.
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

// Writes each sheep's and wolf's energy above it when "Show energy" is on.
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

// Draws one complete frame.
// grid = { agents, patches, hasGrass } as sent by the server's ReceiveGrid.
// Calls onGridDrawn(agents) afterwards if index.html defines it (it updates
// the Agent inspector).
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

// Rebuilds the legend under the grid from seenStates: icons for agents and
// the nest, square swatches for the other patch colours.
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
    const x = (event.clientX - rect.left) * (gridWidth * cellSize / rect.width);
    const y = (event.clientY - rect.top) * (gridHeight * cellSize / rect.height);
    return { x: Math.floor(x / cellSize), y: Math.floor(y / cellSize) };
}

// Every agent drawn on cell (x, y) in the last frame.
function agentsAtCell(x, y) {
    return lastAgents.filter(a => a.x === x && a.y === y);
}

// Sets which agents are selected (at most MAX_SELECTED) and redraws.
function setSelection(ids) {
    selectedAgentIds = [...new Set(ids)].slice(0, MAX_SELECTED);
    selectedAgentId = selectedAgentIds.length ? selectedAgentIds[0] : null;
    redrawGrid();
    if (!lastGrid && typeof onGridDrawn === "function") onGridDrawn(lastAgents);
}

// Selects a single agent, or clears the selection with null.
function selectAgent(id) {
    setSelection(id === null ? [] : [id]);
}

// Draw an empty 20 x 20 grid as soon as the page loads.
setGridSize(gridWidth, gridHeight);
