using Xunit;

namespace ABM.Core.Tests;

public class BoidAgentTests
{
    private const int Size = 20;

    // A boid's starting heading is derived from its id (id * 137 degrees), at
    // half of maxSpeed: id 0 heads along +X, id 90 heads along +Y.
    private static BoidAgent Boid(
        int id,
        (int X, int Y) position,
        GridTopology topology = GridTopology.Toroidal,
        double perceptionRadius = 3.0,
        double separation = 0.0,
        double alignment = 0.0,
        double cohesion = 0.0,
        double maxSpeed = 1.0) =>
        new(id, position, Size, Size, topology, perceptionRadius, separation, alignment, cohesion, maxSpeed);

    private static Grid2D Grid(GridTopology topology = GridTopology.Toroidal) => new(Size, Size, topology);

    private static double Speed(BoidAgent b) => Math.Sqrt(b.VX * b.VX + b.VY * b.VY);

    private static double Distance(BoidAgent a, BoidAgent b) =>
        Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    [Fact]
    public void Cohesion_PullsNearbyBoidsTogether()
    {
        var a = Boid(0, (5, 5), cohesion: 1.0);
        var b = Boid(1, (7, 5), cohesion: 1.0);
        var grid = Grid();
        var rng = new Random(1);

        a.Step(new Agent[] { b }, rng, grid);
        b.Step(new Agent[] { a }, rng, grid);

        Assert.True(Distance(a, b) < 2.0, $"Expected boids to move closer than 2, got {Distance(a, b):F2}");
    }

    [Fact]
    public void Separation_PushesCrowdedBoidsApart()
    {
        var a = Boid(0, (5, 5), separation: 1.0);
        var b = Boid(1, (5, 6), separation: 1.0);
        var grid = Grid();
        var rng = new Random(1);

        a.Step(new Agent[] { b }, rng, grid);
        b.Step(new Agent[] { a }, rng, grid);

        Assert.True(Distance(a, b) > 1.0, $"Expected boids to move further apart than 1, got {Distance(a, b):F2}");
    }

    [Fact]
    public void Alignment_TurnsVelocityTowardsNeighboursHeading()
    {
        var boid = Boid(0, (5, 5), alignment: 1.0);      // heading +X
        var neighbour = Boid(90, (6, 5), alignment: 1.0); // heading +Y

        boid.Step(new Agent[] { neighbour }, new Random(1), Grid());

        // Alignment adds (neighbour velocity - own velocity), so the boid
        // takes on the neighbour's heading, give or take the random nudge.
        Assert.InRange(boid.VX, -0.1, 0.1);
        Assert.InRange(boid.VY, 0.4, 0.6);
    }

    [Fact]
    public void Neighbours_OutsidePerceptionRadius_AreIgnored()
    {
        var boid = Boid(0, (5, 5), perceptionRadius: 2.0, cohesion: 1.0);
        var farAway = Boid(1, (10, 5), cohesion: 1.0);
        var (vx, vy) = (boid.VX, boid.VY);

        boid.Step(new Agent[] { farAway }, new Random(1), Grid());

        // Only the random nudge (at most 0.05 per axis) should change velocity.
        Assert.InRange(boid.VX - vx, -0.05, 0.05);
        Assert.InRange(boid.VY - vy, -0.05, 0.05);
    }

    [Fact]
    public void Speed_IsClampedToMaxSpeed()
    {
        var boid = Boid(0, (10, 10), separation: 5.0, alignment: 5.0, cohesion: 5.0, maxSpeed: 0.8);
        var crowd = Enumerable.Range(1, 6)
            .Select(i => (Agent)Boid(i * 37, (10 + i % 3 - 1, 10 + i / 3 - 1)))
            .ToArray();
        var rng = new Random(1);

        for (int i = 0; i < 20; i++)
        {
            boid.Step(crowd, rng, Grid());
            Assert.True(Speed(boid) <= 0.8 + 1e-9, $"Speed {Speed(boid)} exceeded maxSpeed on step {i}");
        }
    }

    [Fact]
    public void Toroidal_BoidWrapsAroundAndStaysOnTheGrid()
    {
        var boid = Boid(0, (Size - 1, 5)); // heading +X, off the right edge
        var grid = Grid(GridTopology.Toroidal);
        var rng = new Random(1);
        bool wrapped = false;

        for (int i = 0; i < 200; i++)
        {
            boid.Step(Array.Empty<Agent>(), rng, grid);

            Assert.InRange(boid.X, 0, Size);
            Assert.InRange(boid.Y, 0, Size);
            Assert.InRange(boid.Position.X, 0, Size - 1);
            Assert.InRange(boid.Position.Y, 0, Size - 1);
            if (boid.X < Size / 2.0) wrapped = true;
        }

        Assert.True(wrapped, "Expected the boid to wrap past the right edge to the left side.");
    }

    [Fact]
    public void Bounded_BoidBouncesOffEdgesAndStaysOnTheGrid()
    {
        var boid = Boid(0, (Size - 1, 5), GridTopology.Bounded); // heading +X, into the right wall
        var grid = Grid(GridTopology.Bounded);
        var rng = new Random(1);

        boid.Step(Array.Empty<Agent>(), rng, grid);
        Assert.True(boid.VX < 0, "Expected the boid's X velocity to reverse at the right wall.");

        for (int i = 0; i < 200; i++)
        {
            boid.Step(Array.Empty<Agent>(), rng, grid);

            Assert.InRange(boid.X, 0, Size - 1);
            Assert.InRange(boid.Y, 0, Size - 1);
            Assert.InRange(boid.Position.X, 0, Size - 1);
            Assert.InRange(boid.Position.Y, 0, Size - 1);
        }
    }
}
