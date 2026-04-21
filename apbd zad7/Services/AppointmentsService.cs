using System.Data;
using apbd_zad7.Models;
using Microsoft.Data.SqlClient;

namespace apbd_zad7.Services;

public interface IAppointmentsService
{
    Task<IEnumerable<AppointmentListDto>> GetAppointmentsAsync(string? status, string? patientLastName);
    Task<AppointmentDetailsDto?> GetAppointmentByIdAsync(int id);
    Task<int> CreateAppointmentAsync(CreateAppointmentRequestDto appointment);
    Task UpdateAppointmentAsync(int id, UpdateAppointmentRequestDto appointment);
    Task DeleteAppointmentAsync(int id);
}

public class AppointmentsService : IAppointmentsService
{
    private readonly IConfiguration _configuration;

    public AppointmentsService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private string GetConnectionString()
    {
        return _configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Missing connection string");
    }

    public async Task<IEnumerable<AppointmentListDto>> GetAppointmentsAsync(string? status, string? patientLastName)
    {
        var result = new List<AppointmentListDto>();

        await using var connection = new SqlConnection(GetConnectionString());
        await using var command = new SqlCommand(@"
            SELECT
                a.IdAppointment,
                a.AppointmentDate,
                a.Status,
                a.Reason,
                p.FirstName + N' ' + p.LastName AS PatientFullName,
                p.Email AS PatientEmail
            FROM dbo.Appointments a
            JOIN dbo.Patients p ON p.IdPatient = a.IdPatient
            WHERE (@Status IS NULL OR a.Status = @Status)
              AND (@PatientLastName IS NULL OR p.LastName = @PatientLastName)
            ORDER BY a.AppointmentDate;
        ", connection);
        
        command.Parameters.Add("@Status", SqlDbType.NVarChar, 30).Value =
            string.IsNullOrEmpty(status) ? DBNull.Value : status;
        command.Parameters.Add("@PatientLastName", SqlDbType.NVarChar, 30).Value =
            string.IsNullOrEmpty(patientLastName) ? DBNull.Value : patientLastName;
        
        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();

        while(await reader.ReadAsync())
        {
            result.Add(new AppointmentListDto
            {
                IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
                AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
                Status = reader.GetString(reader.GetOrdinal("Status")),
                Reason = reader.GetString(reader.GetOrdinal("Reason")),
                PatientFullName = reader.GetString(reader.GetOrdinal("PatientFullName")),
                PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail")),
            });
        }

        return result;
    }

    public async Task<AppointmentDetailsDto?> GetAppointmentByIdAsync(int id)
    {
        await using var connection = new SqlConnection(GetConnectionString());
        await using var command = new SqlCommand(@"
            SELECT
                a.IdAppointment,
                a.AppointmentDate,
                a.Status,
                a.Reason,
                a.InternalNotes,
                a.CreatedAt,
                p.IdPatient,
                p.FirstName AS PatientFirstName,
                p.LastName AS PatientLastName,
                p.Email AS PatientEmail,
                p.PhoneNumber AS PatientPhoneNumber,
                d.IdDoctor,
                d.FirstName AS DoctorFirstName,
                d.LastName AS DoctorLastName,
                d.LicenseNumber AS DoctorLicenseNumber,
                s.Name AS DoctorSpecialization
            FROM Appointments a
            JOIN Patients p ON p.IdPatient = a.IdPatient
            JOIN Doctors d On d.IdDoctor = a.IdDoctor
            JOIN Specializations s ON s.IdSpecialization = d.IdSpecialization
            WHERE a.IdAppointment = @IdAppointment;
        ", connection);

        command.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = id;

        await connection.OpenAsync();
        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return new AppointmentDetailsDto
        {
            IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
            AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
            Status = reader.GetString(reader.GetOrdinal("Status")),
            Reason = reader.GetString(reader.GetOrdinal("Reason")),
            InternalNotes = reader.IsDBNull(reader.GetOrdinal("InternalNotes")) ? null : reader.GetString(reader.GetOrdinal("InternalNotes")),
            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
            IdPatient = reader.GetInt32(reader.GetOrdinal("IdPatient")),
            PatientFirstName = reader.GetString(reader.GetOrdinal("PatientFirstName")),
            PatientLastName = reader.GetString(reader.GetOrdinal("PatientLastName")),
            PatientEmail = reader.GetString(reader.GetOrdinal("PatientEmail")),
            PatientPhoneNumber = reader.GetString(reader.GetOrdinal("PatientPhoneNumber")),
            IdDoctor = reader.GetInt32(reader.GetOrdinal("IdDoctor")),
            DoctorFirstName = reader.GetString(reader.GetOrdinal("DoctorFirstName")),
            DoctorLastName = reader.GetString(reader.GetOrdinal("DoctorLastName")),
            DoctorLicenseNumber = reader.GetString(reader.GetOrdinal("DoctorLicenseNumber")),
            DoctorSpecialization = reader.GetString(reader.GetOrdinal("DoctorSpecialization")),
        };
    }

    public async Task<int> CreateAppointmentAsync(CreateAppointmentRequestDto appointment)
    {
        if (appointment.IdPatient <= 0)
            throw new ArgumentException("IdPatient must be greater than 0");
        if (appointment.IdDoctor <= 0)
            throw new ArgumentException("IdDoctor must be greater than 0");
        if (appointment.AppointmentDate <= DateTime.Now)
            throw new ArgumentException("Appointment date cannot be in the past");
        if (string.IsNullOrWhiteSpace(appointment.Reason))
            throw new ArgumentException("Reason is required");
        if (appointment.Reason.Length > 250)
            throw new ArgumentException("Reason must be at most 250 chars");
        
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync();
        
        if (!await DoesPatientExistAsync(connection, appointment.IdPatient))
            throw new ArgumentException("Patient does not exist");

        if (!await DoesDoctorExistAsync(connection, appointment.IdDoctor))
            throw new ArgumentException("Doctor does not exists");

        if (await HasDoctorConflictAsync(connection, appointment.IdDoctor, appointment.AppointmentDate, null))
            throw new InvalidOperationException("Doctor already has another appointment at the same time");

        await using var command = new SqlCommand(@"
            INSERT INTO Appointments (IdPatient, IdDoctor, AppointmentDate, Status, Reason, InternalNotes, CreatedAt)
            OUTPUT INSERTED.IdAppointment VALUES (@IdPatient, @IdDoctor, @AppointmentDate, @Status, @Reason, @InternalNotes, SYSUTCDATETIME())
        ", connection);

        command.Parameters.Add("@IdPatient", SqlDbType.Int).Value = appointment.IdPatient;
        command.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = appointment.IdDoctor;
        command.Parameters.Add("@AppointmentDate", SqlDbType.DateTime2).Value = appointment.AppointmentDate;
        command.Parameters.Add("@Status", SqlDbType.NVarChar, 30).Value = "scheduled";
        command.Parameters.Add("@Reason", SqlDbType.NVarChar, 250).Value = appointment.Reason;
        command.Parameters.Add("@InternalNotes", SqlDbType.NVarChar, 500).Value = DBNull.Value;

        var newId = await command.ExecuteScalarAsync();
        return Convert.ToInt32(newId);
    }

    public async Task UpdateAppointmentAsync(int id, UpdateAppointmentRequestDto appointment)
    {
        if (appointment.IdPatient <= 0)
            throw new ArgumentException("IdPatient must be greater than 0");
        if (appointment.IdDoctor <= 0)
            throw new ArgumentException("IdDoctor must be greater than 0");
        if (appointment.AppointmentDate <= DateTime.Now)
            throw new ArgumentException("Appointment date cannot be in the past");
        if (string.IsNullOrWhiteSpace(appointment.Reason))
            throw new ArgumentException("Reason is required");
        if (appointment.Reason.Length > 250)
            throw new ArgumentException("Reason must be at most 250 chars");
        if (string.IsNullOrWhiteSpace(appointment.Status))
            throw new ArgumentException("Status is required");
        if (appointment.Status is not ("Scheduled" or "Completed" or "Cancelled"))
            throw new ArgumentException("Status must be one of: Scheduled, Completed, Cancelled");
        if (appointment.InternalNotes is { Length: > 500 })
            throw new ArgumentException("InternalNotes must be at most 500 chars");
        
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync();

        var existing = await GetAppointmentBasicsAsync(connection, id);
        if (existing is null)
            throw new KeyNotFoundException("Appointment not found");
        
        if (!await DoesPatientExistAsync(connection, appointment.IdPatient))
            throw new ArgumentException("Patient does not exist");

        if (!await DoesDoctorExistAsync(connection, appointment.IdDoctor))
            throw new ArgumentException("Doctor does not exists");

        if (existing.Status == "Completed" && existing.AppointmentDate != appointment.AppointmentDate)
            throw new InvalidOperationException("Cannot change appointment date for a completed appointment");

        if (await HasDoctorConflictAsync(connection, appointment.IdDoctor, appointment.AppointmentDate, id))
            throw new InvalidOperationException("Doctor already has another appointment at the same time");

        await using var command = new SqlCommand(@"
            UPDATE Appointments
            SET
                IdPatient = @IdPatient,
                IdDoctor = @IdDoctor,
                AppointmentDate = @AppointmentDate,
                Status = @Status,
                Reason = @Reason,
                InternalNotes = @InternalNotes
            WHERE IdAppointment = @IdAppointment;
        ", connection);

        command.Parameters.Add("IdPatient", SqlDbType.Int).Value = appointment.IdPatient;
        command.Parameters.Add("IdDoctor", SqlDbType.Int).Value = appointment.IdDoctor;
        command.Parameters.Add("AppointmentDate", SqlDbType.DateTime2).Value = appointment.AppointmentDate;
        command.Parameters.Add("Status", SqlDbType.NVarChar, 30).Value = appointment.Status;
        command.Parameters.Add("Reason", SqlDbType.NVarChar, 250).Value = appointment.Reason;
        command.Parameters.Add("InternalNotes", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(appointment.InternalNotes) ? DBNull.Value : appointment.InternalNotes;
        command.Parameters.Add("IdAppointment", SqlDbType.Int).Value = id;

        var rowsAffected = await command.ExecuteNonQueryAsync();
        if  (rowsAffected == 0)
            throw new KeyNotFoundException("Appointment not found");
    }

    public async Task DeleteAppointmentAsync(int id)
    {
        await using var connection = new SqlConnection(GetConnectionString());
        await connection.OpenAsync();

        var existing = await GetAppointmentBasicsAsync(connection, id);
        if (existing is null)
            throw new KeyNotFoundException("Appointment not found");
        
        if (existing.Status == "Completed")
            throw new InvalidOperationException("Appointment is completed");
        
        await using var command = new SqlCommand(@"
            DELETE FROM Appointments WHERE IdAppointment = @IdAppointment;
        ", connection);
        
        command.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = id;
        
        var rowsAffected = await command.ExecuteNonQueryAsync();
        if  (rowsAffected == 0)
            throw new KeyNotFoundException("Appointment not found");
    }
    
    private static async Task<bool> DoesPatientExistAsync(SqlConnection connection, int id)
    {
        await using var command = new SqlCommand(@"SELECT COUNT(1) FROM Patients WHERE IdPatient = @IdPatient AND IsActive = 1", connection);

        command.Parameters.Add("@IdPatient", SqlDbType.Int).Value = id;

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }

    private static async Task<bool> DoesDoctorExistAsync(SqlConnection connection, int id)
    {
        await using var command = new SqlCommand(@"SELECT COUNT(1) FROM Doctors WHERE IdDoctor = @IdDoctor AND IsActive = 1", connection);

        command.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = id;

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }

    private static async Task<bool> HasDoctorConflictAsync(SqlConnection connection, int id, DateTime date, int? idAppointment)
    {
        await using var command = new SqlCommand(@"SELECT COUNT(1) FROM Appointments WHERE IdDoctor = @IdDoctor AND AppointmentDate = @Date AND (@ToExclude IS NULL OR IdAppointment <> @ToExclude);", connection);

        command.Parameters.Add("@IdDoctor", SqlDbType.Int).Value = id;
        command.Parameters.Add("@Date", SqlDbType.DateTime2).Value = date;
        command.Parameters.Add("@ToExclude", SqlDbType.Int).Value = idAppointment.HasValue ? idAppointment.Value : DBNull.Value;

        var result = await command.ExecuteScalarAsync();
        return Convert.ToInt32(result) > 0;
    }

    private static async Task<AppointmentBasics?> GetAppointmentBasicsAsync(SqlConnection connection, int id)
    {
        await using var command = new SqlCommand(@"
            SELECT IdAppointment, AppointmentDate, Status FROM Appointments WHERE IdAppointment = @IdAppointment
        ", connection);

        command.Parameters.Add("@IdAppointment", SqlDbType.Int).Value = id;
        
        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return null;

        return new AppointmentBasics
        {
            IdAppointment = reader.GetInt32(reader.GetOrdinal("IdAppointment")),
            AppointmentDate = reader.GetDateTime(reader.GetOrdinal("AppointmentDate")),
            Status = reader.GetString(reader.GetOrdinal("Status"))
        };
    }
}