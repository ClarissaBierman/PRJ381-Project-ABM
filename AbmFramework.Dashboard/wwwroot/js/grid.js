const canvas = document.getElementById("grid");
const ctx = canvas.getContext("2d");
const legend = document.getElementById("legend");

const targetSize = 480;
let gridWidth = 20;
let gridHeight = 20;
let cellSize = targetSize / 20;

const stateStyles = {
    "Susceptible": { color: "#3366cc", label: "Susceptible" },
    "Infected": { color: "#dc3545", label: "Infected" },
    "Recovered": { color: "#28a745", label: "Recovered" },
    "Group0-Happy": { color: "#3366cc", label: "Group A, happy" },
    "Group0-Unhappy": { color: "#a9c1f5", label: "Group A, unhappy" },
    "Group1-Happy": { color: "#dc3545", label: "Group B, happy" },
    "Group1-Unhappy": { color: "#f5b5bc", label: "Group B, unhappy" },
    "BoidAgent": { color: "#3366cc", label: "Boid" },
    "Searching": { color: "#8d6e63", label: "Searching for food" },
    "ReturningWithFood": { color: "#28a745", label: "Carrying food home" },
    "Sheep": { color: "#f1c40f", label: "Sheep" },
    "Wolf": { color: "#34495e", label: "Wolves" }
};

// Patch (grid cell) colours, like NetLogo's patches. Shown as square swatches in the legend.
const patchStyles = {
    "Grass": { color: "#8dbf78", label: "Grass" },
    "BareGround": { color: "#b08d6a", label: "Eaten grass" },
    "Nest": { color: "#7b1fa2", label: "Nest" },
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

function clearGrid() {
    seenStates = [];
    lastAgents = [];
    lastGrid = null;
    foodReference = 1;
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
            fillCell(p.x, p.y, patchStyles.Nest.color);
            drawn.add("Nest");
        }
    });

    return drawn;
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

    const radius = Math.max(2, cellSize * 0.3);
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

        ctx.fillStyle = style.color;
        ctx.beginPath();
        ctx.arc(a.x * cellSize + cellSize / 2, a.y * cellSize + cellSize / 2, radius, 0, Math.PI * 2);
        ctx.fill();
    });

    drawEnergyLabels(lastAgents);

    if (legendChanged) {
        renderLegend();
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
            const shape = state in patchStyles ? "swatch square" : "swatch";
            return `<span class="legend-item"><span class="${shape}" style="background:${style.color}"></span>${style.label}</span>`;
        })
        .join("");
}

setGridSize(gridWidth, gridHeight);
