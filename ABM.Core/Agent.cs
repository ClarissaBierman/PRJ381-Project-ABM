using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ABM.Core
{
    public abstract class Agent
    {
        public int Id { get; }
        public (int X, int Y) Position { get; set; }

        protected Agent(int id, (int X, int Y) position)
        {
            Id = id;
            Position = position;
        }

        public abstract void Step(IReadOnlyList<Agent> neighbours, Random rng);
    }
}
