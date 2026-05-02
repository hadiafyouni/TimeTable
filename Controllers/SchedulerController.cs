using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;

namespace Schedule
{
    [ApiController]
    [Route("api/[controller]")]
    public class SchedulerController : ControllerBase
    {
        private readonly CurriculumRepository _repository;

        public SchedulerController(CurriculumRepository repository)
        {
            _repository = repository;
        }

        [HttpPost("generate")]
        public IActionResult Generate()
        {
            try
            {
                var slots = _repository.GetTimeSlots();
                var requirements = _repository.GetClassRequirements();

                if (!slots.Any() || !requirements.Any())
                    return BadRequest("Database is empty. Please seed the required data first.");

                var engine = new TimetableEngine(slots);
                var result = engine.GenerateSchedule(requirements);

                Guid versionId = Guid.NewGuid();
                _repository.SaveScheduleToDatabase(result, versionId);

                return Ok(new { Message = "Success", Version = versionId, Data = result });
            }
            catch (Exception ex)
            {
                return BadRequest(new { Error = ex.Message });
            }
        }
    }
}