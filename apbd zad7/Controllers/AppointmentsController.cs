using apbd_zad7.Models;
using apbd_zad7.Services;
using Microsoft.AspNetCore.Mvc;

namespace apbd_zad7.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AppointmentsController : ControllerBase
{
    private readonly IAppointmentsService _appointmentsService;

    public AppointmentsController(IAppointmentsService appointmentsService)
    {
        _appointmentsService = appointmentsService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AppointmentListDto>>> GetAppointments([FromQuery] string? status, [FromQuery] string? patientLastName)
    {
        return Ok(await _appointmentsService.GetAppointmentsAsync(status, patientLastName));
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<AppointmentDetailsDto>> GetAppointmentDetails(int id)
    {
        var appointment = await _appointmentsService.GetAppointmentByIdAsync(id);
        if (appointment is null)
            return NotFound(new ErrorResponseDto
            {
                Message = $"Appointment with id {id} not found"
            });

        return Ok(appointment);
    }

    [HttpPost]
    public async Task<ActionResult> CreateAppointment([FromBody] CreateAppointmentRequestDto appointment)
    {
        try
        {
            var newId = await _appointmentsService.CreateAppointmentAsync(appointment);
            return CreatedAtAction(
                nameof(GetAppointmentDetails),
                new { id = newId },
                new { id = newId }
            );
        }
        catch (ArgumentException e)
        {
            return BadRequest(new ErrorResponseDto
            {
                Message = e.Message
            });
        }
        catch (Exception e)
        {
            return Conflict(new ErrorResponseDto
            {
                Message = e.Message
            });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateAppointment(int id, [FromBody] UpdateAppointmentRequestDto appointment)
    {
        try
        {
            await _appointmentsService.UpdateAppointmentAsync(id, appointment);
            return NoContent();
        }
        catch (KeyNotFoundException e)
        {
            return NotFound(new ErrorResponseDto
            {
                Message = e.Message
            });
        }
        catch (ArgumentException e)
        {
            return BadRequest(new ErrorResponseDto
            {
                Message = e.Message
            });
        }
        catch (Exception e)
        {
            return Conflict(new ErrorResponseDto
            {
                Message = e.Message
            });
        }
    }
    
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteAppointment(int id)
    {
        try
        {
            await _appointmentsService.DeleteAppointmentAsync(id);
            return NoContent();
        }
        catch (KeyNotFoundException e)
        {
            return NotFound(new ErrorResponseDto
            {
                Message = e.Message
            });
        }
        catch (Exception e)
        {
            return Conflict(new ErrorResponseDto
            {
                Message = e.Message
            });
        }
    }
    
}