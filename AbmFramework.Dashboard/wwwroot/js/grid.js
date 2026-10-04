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
    "Grass": { color: "#8dbf78", label: "Grass" },
    "Sheep": { color: "#f1c40f", label: "Sheep" },
    "Wolf": { color: "#34495e", label: "Wolves" }
};

const fallbackPalette = ["#ffc107", "#9b59b6", "#17a2b8", "#e67e22", "#6c757d"];
let fallbackIndex = 0;
let seenStates = [];

function styleFor(state) {
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
    legend.innerHTML = "";
    drawGridLines();
}

function drawGridLines() {
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.strokeStyle = "#d0d0d0";

    for (let y = 0; y < gridHeight; y++) {
        for (let x = 0; x < gridWidth; x++) {
            ctx.strokeRect(x * cellSize, y * cellSize, cellSize, cellSize);
        }
    }
}

function drawAgents(agents, grassPatches = []) {
    drawGridLines();

    const radius = Math.max(2, cellSize * 0.3);
    let legendChanged = false;

    grassPatches.forEach(patch => {
        const style = styleFor("Grass");
        if (!seenStates.includes("Grass")) {
            seenStates.push("Grass");
            legendChanged = true;
        }

        ctx.fillStyle = style.color;
        ctx.fillRect(patch.x * cellSize + 1, patch.y * cellSize + 1, Math.max(0, cellSize - 1), Math.max(0, cellSize - 1));
    });

    agents.forEach(a => {
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

    if (legendChanged) {
        renderLegend();
    }
}

function renderLegend() {
    legend.innerHTML = seenStates
        .map(state => {
            const style = styleFor(state);
            const shape = state === "Grass" ? "swatch square" : "swatch";
            return `<span class="legend-item"><span class="${shape}" style="background:${style.color}"></span>${style.label}</span>`;
        })
        .join("");
}

setGridSize(gridWidth, gridHeight);
