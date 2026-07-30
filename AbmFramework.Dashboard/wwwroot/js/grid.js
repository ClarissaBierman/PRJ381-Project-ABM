const canvas = document.getElementById("grid");
const ctx = canvas.getContext("2d");
const cellSize = 30;
const gridWidth = 20;
const gridHeight = 20;

canvas.width = gridWidth * cellSize;
canvas.height = gridHeight * cellSize;

function drawGrid() {
    ctx.clearRect(0, 0, canvas.width, canvas.height);

    for (let y = 0; y < gridHeight; y++) {
        for (let x = 0; x < gridWidth; x++) {
            ctx.strokeRect(x * cellSize, y * cellSize, cellSize, cellSize);
        }
    }
}

function drawAgents(agents) {
    agents.forEach(a => {
        if (a.state === "Susceptible") ctx.fillStyle = "blue";
        if (a.state === "Infected") ctx.fillStyle = "red";
        if (a.state === "Recovered") ctx.fillStyle = "green";

        ctx.beginPath();
        ctx.arc(a.x * cellSize + cellSize / 2, a.y * cellSize + cellSize / 2, 8, 0, Math.PI * 2);
        ctx.fill();
    });
}

drawGrid();

connection.on("ReceiveGrid", (agents) => {
    drawGrid();
    drawAgents(agents);
});