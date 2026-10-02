using System;
using System.Collections.Generic;
using System.Linq;

namespace ABM.Core
{
    // Flocking agent (Craig Reynolds' Boids). Keeps a continuous-space
    // position/velocity internally since Agent.Position only stores
    // integers, then rounds back to Position after each move.
    public class BoidAgent : Agent
    {
        public double X { get; private set; }
        public double Y { get; private set; }
        public double VX { get; private set; }
        public double VY { get; private set; }

        private readonly int gridWidth;
        private readonly int gridHeight;
        private readonly GridTopology topology;

        private readonly double perceptionRadius;
        private readonly double separationWeight;
        private readonly double alignmentWeight;
        private readonly double cohesionWeight;
        private readonly double maxSpeed;

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

            double angle = (id * 137 % 360) * Math.PI / 180.0;
            VX = Math.Cos(angle) * maxSpeed * 0.5;
            VY = Math.Sin(angle) * maxSpeed * 0.5;
        }

        public override void Step(IReadOnlyList<Agent> neighbours, Random rng, IEnvironmentManager environment)
        {
            var nearby = neighbours
                .OfType<BoidAgent>()
                .Where(b => Distance(b) <= perceptionRadius)
                .ToList();

            if (nearby.Count > 0)
            {
                var (sepX, sepY) = Separation(nearby);
                var (aliX, aliY) = Alignment(nearby);
                var (cohX, cohY) = Cohesion(nearby);

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

            Position = ((int)Math.Round(X), (int)Math.Round(Y));
        }

        private double Distance(BoidAgent other)
        {
            var dx = other.X - X;
            var dy = other.Y - Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private (double X, double Y) Separation(List<BoidAgent> nearby)
        {
            double x = 0, y = 0;
            foreach (var other in nearby)
            {
                var dx = X - other.X;
                var dy = Y - other.Y;
                var distance = Math.Sqrt(dx * dx + dy * dy);
                if (distance < 0.0001) continue;

                x += dx / distance;
                y += dy / distance;
            }
            return (x, y);
        }

        private (double X, double Y) Alignment(List<BoidAgent> nearby)
        {
            double avgVX = nearby.Average(b => b.VX);
            double avgVY = nearby.Average(b => b.VY);
            return (avgVX - VX, avgVY - VY);
        }

        private (double X, double Y) Cohesion(List<BoidAgent> nearby)
        {
            double avgX = nearby.Average(b => b.X);
            double avgY = nearby.Average(b => b.Y);
            return (avgX - X, avgY - Y);
        }

        private void ClampSpeed()
        {
            var speed = Math.Sqrt(VX * VX + VY * VY);
            if (speed > maxSpeed)
            {
                VX = VX / speed * maxSpeed;
                VY = VY / speed * maxSpeed;
            }
        }

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
