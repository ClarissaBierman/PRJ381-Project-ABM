// =============================================================================
// BoidAgent.cs  (ABM.Core)
//
// Craig Reynolds' Boids flocking model (1987). Each bird ("boid") looks at the
// boids within its perception radius and follows three rules:
//   1. Separation: steer away from boids that are too close (don't crash).
//   2. Alignment:  steer towards the average direction of nearby boids.
//   3. Cohesion:   steer towards the average position of nearby boids.
// Nobody leads, yet the group moves as a flock.
//
// Boids fly smoothly, so unlike other agents they keep an exact position
// (X, Y as doubles) and a velocity (VX, VY). After each move the exact position
// is rounded to a whole grid cell and stored in Agent.Position, which is what
// the grid, the neighbour lookup and the dashboard use.
// =============================================================================

namespace ABM.Core
{
    /// <summary>
    /// A bird in the Boids flocking model, steered by separation, alignment and cohesion.
    /// </summary>
    public class BoidAgent : Agent
    {
        /// <summary>Exact horizontal position (in cells, can be between cells).</summary>
        public double X { get; private set; }

        /// <summary>Exact vertical position (in cells, can be between cells).</summary>
        public double Y { get; private set; }

        /// <summary>Horizontal speed, in cells per tick.</summary>
        public double VX { get; private set; }

        /// <summary>Vertical speed, in cells per tick.</summary>
        public double VY { get; private set; }

        // Grid size and edge behaviour, needed to wrap or bounce at the edges.
        private readonly int gridWidth;
        private readonly int gridHeight;
        private readonly GridTopology topology;

        // Tuning values, from SimulationConfig (PerceptionRadius, SeparationWeight,
        // AlignmentWeight, CohesionWeight, MaxSpeed).
        private readonly double perceptionRadius;   // how far a boid can "see", in cells
        private readonly double separationWeight;   // strength of rule 1
        private readonly double alignmentWeight;    // strength of rule 2
        private readonly double cohesionWeight;     // strength of rule 3
        private readonly double maxSpeed;           // top speed, in cells per tick

        /// <summary>Creates a boid at a grid cell with a starting direction based on its id.</summary>
        /// <param name="id">Unique agent id. Also used to pick the starting direction.</param>
        /// <param name="position">Starting cell.</param>
        /// <param name="gridWidth">Grid width, for edge handling.</param>
        /// <param name="gridHeight">Grid height, for edge handling.</param>
        /// <param name="topology">Wrap around or bounce off the edges.</param>
        /// <param name="perceptionRadius">How far the boid can see, in cells.</param>
        /// <param name="separationWeight">How strongly it avoids close neighbours.</param>
        /// <param name="alignmentWeight">How strongly it matches their direction.</param>
        /// <param name="cohesionWeight">How strongly it moves towards the group.</param>
        /// <param name="maxSpeed">Top speed in cells per tick.</param>
        public BoidAgent(
            int id,
            (int X, int Y) position,
            int gridWidth,
            int gridHeight,
            GridTopology topology,
            double perceptionRadius = 2.0,
            double separationWeight = 1.5,
            double alignmentWeight = 1.0,
            double cohesionWeight = 1.0,
            double maxSpeed = 1.0)
            : base(id, position)
        {
            X = position.X;
            Y = position.Y;

            this.gridWidth = gridWidth;
            this.gridHeight = gridHeight;
            this.topology = topology;

            this.perceptionRadius = perceptionRadius;
            this.separationWeight = separationWeight;
            this.alignmentWeight = alignmentWeight;
            this.cohesionWeight = cohesionWeight;
            this.maxSpeed = maxSpeed;

            // Spread the starting directions around the circle without using the
            // random generator: 137 degrees apart (close to the "golden angle")
            // gives every boid a different direction. Start at half speed.
            double angle = (id * 137 % 360) * Math.PI / 180.0;
            VX = Math.Cos(angle) * maxSpeed * 0.5;
            VY = Math.Sin(angle) * maxSpeed * 0.5;
        }

        /// <inheritdoc />
        public override void Step(IReadOnlyList<Agent> neighbours, Random rng, IEnvironmentManager environment)
        {
            // The engine gives us the boids in the surrounding cells. Narrow that
            // down to the ones inside the perception radius.
            var nearby = neighbours
                .OfType<BoidAgent>()
                .Where(b => Distance(b) <= perceptionRadius)
                .ToList();

            if (nearby.Count > 0)
            {
                var (sepX, sepY) = Separation(nearby);
                var (aliX, aliY) = Alignment(nearby);
                var (cohX, cohY) = Cohesion(nearby);

                // Each rule nudges the velocity. The weights decide which rule wins.
                VX += sepX * separationWeight + aliX * alignmentWeight + cohX * cohesionWeight;
                VY += sepY * separationWeight + aliY * alignmentWeight + cohY * cohesionWeight;
            }

            // Small random nudge so the flock doesn't settle into a static shape.
            VX += (rng.NextDouble() - 0.5) * 0.1;
            VY += (rng.NextDouble() - 0.5) * 0.1;

            ClampSpeed();

            X += VX;
            Y += VY;

            ApplyTopology();

            int cellX = (int)Math.Round(X);
            int cellY = (int)Math.Round(Y);

            // On a toroidal grid X can be up to just under gridWidth, which
            // rounds to gridWidth (one past the last column), so wrap the
            // rounded cell back onto the grid. On a Bounded grid X never goes
            // past gridWidth - 1, so it can't round off the grid.
            if (topology == GridTopology.Toroidal)
            {
                cellX %= gridWidth;
                cellY %= gridHeight;
            }

            Position = (cellX, cellY);
        }

        // Straight-line distance to another boid, in cells.
        private double Distance(BoidAgent other)
        {
            var dx = other.X - X;
            var dy = other.Y - Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // Rule 1: add up a unit-length push away from every nearby boid.
        private (double X, double Y) Separation(List<BoidAgent> nearby)
        {
            double x = 0, y = 0;
            foreach (var other in nearby)
            {
                var dx = X - other.X;
                var dy = Y - other.Y;
                var distance = Math.Sqrt(dx * dx + dy * dy);

                // Two boids on exactly the same spot have no "away" direction,
                // and dividing by ~0 would blow up, so skip that pair.
                if (distance < 0.0001) continue;

                x += dx / distance;
                y += dy / distance;
            }
            return (x, y);
        }

        // Rule 2: the difference between the group's average velocity and ours.
        private (double X, double Y) Alignment(List<BoidAgent> nearby)
        {
            double avgVX = nearby.Average(b => b.VX);
            double avgVY = nearby.Average(b => b.VY);
            return (avgVX - VX, avgVY - VY);
        }

        // Rule 3: the direction from us to the centre of the nearby group.
        private (double X, double Y) Cohesion(List<BoidAgent> nearby)
        {
            double avgX = nearby.Average(b => b.X);
            double avgY = nearby.Average(b => b.Y);
            return (avgX - X, avgY - Y);
        }

        // Scales the velocity down to maxSpeed if it got faster than that,
        // keeping the direction the same.
        private void ClampSpeed()
        {
            var speed = Math.Sqrt(VX * VX + VY * VY);
            if (speed > maxSpeed)
            {
                VX = VX / speed * maxSpeed;
                VY = VY / speed * maxSpeed;
            }
        }

        // Handles the grid edges: wrap around on a Toroidal grid, or stop at the
        // wall and bounce back (flip the speed) on a Bounded grid.
        private void ApplyTopology()
        {
            if (topology == GridTopology.Toroidal)
            {
                X = ((X % gridWidth) + gridWidth) % gridWidth;
                Y = ((Y % gridHeight) + gridHeight) % gridHeight;
            }
            else
            {
                if (X < 0) { X = 0; VX = Math.Abs(VX); }
                if (X > gridWidth - 1) { X = gridWidth - 1; VX = -Math.Abs(VX); }
                if (Y < 0) { Y = 0; VY = Math.Abs(VY); }
                if (Y > gridHeight - 1) { Y = gridHeight - 1; VY = -Math.Abs(VY); }
            }
        }
    }
}
