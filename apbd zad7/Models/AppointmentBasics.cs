namespace apbd_zad7.Models;

public class AppointmentBasics
{
    public int IdAppointment { get; set; }
    public DateTime AppointmentDate { get; set; }
    public string Status { get; set; } = string.Empty;
}