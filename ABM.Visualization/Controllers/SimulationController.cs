using Microsoft.AspNetCore.Mvc;
using ABM.Visualization.Models;

namespace ABM.Visualization.Controllers
{
    [ApiController]
    [Route("simulation")]
    public class SimulationController : ControllerBase
    {
        [HttpGet]
        public SimulationState Get()
        {
            return new SimulationState
            {
                Agents =
                {
                    new AgentDTO { Id = 1, X = 2, Y = 2, State = "Susceptible" },
                    new AgentDTO { Id = 2, X = 7, Y = 5, State = "Infected" },
                    new AgentDTO { Id = 3, X = 10, Y = 10, State = "Recovered" }
                }
            };
        }
    }
}