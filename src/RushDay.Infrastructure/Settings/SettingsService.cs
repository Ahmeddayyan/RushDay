using Microsoft.EntityFrameworkCore;
using RushDay.Domain.Audit;
using RushDay.Domain.Modules;
using RushDay.Domain.Settings;
using RushDay.Infrastructure.Audit;
using RushDay.Infrastructure.Grades;
using RushDay.Infrastructure.Persistence;
using RushDay.Infrastructure.Seeding;

namespace RushDay.Infrastructure.Settings;

/// <summary>The editable <c>academic_settings</c> fields (<c>PUT /api/admin/settings</c>).</summary>
public sealed record SettingsChange(
    string AcademicYear,
    Semester CurrentSemester,
    string InstitutionName,
    string InstitutionShortName,
    string TimeZone,
    string? SupportEmail,
    string? SupportUrl);

/// <summary>The stored row after a read or an update; <see cref="YearChanged"/> tells the caller which caches to drop.</summary>
public sealed record SettingsRecord(
    string AcademicYear,
    Semester CurrentSemester,
    string InstitutionName,
    string InstitutionShortName,
    string TimeZone,
    string? SupportEmail,
    string? SupportUrl,
    DateTimeOffset UpdatedAt,
    bool YearChanged = false);

/// <summary>
/// The singleton <c>academic_settings</c> row (02-api.md section 8.5). An update audits <c>settings.changed</c> with
/// before and after, and when the academic year changes recomputes <c>modules.enrolled_count</c> for the new year in
/// the same transaction (the <c>reconcile_enrolled_count</c> statements, D28): this year's places start from this
/// year's enrolments only. The caller invalidates <c>settings</c>, and on a year change <c>catalogue:all</c> and
/// <c>windows:all</c> as well.
/// </summary>
public sealed class SettingsService(RushDayDbContext db, AuditWriter audit, TimeProvider clock)
{
    /// <summary>Null only before the backfills have created the row.</summary>
    public async Task<SettingsRecord?> GetAsync(CancellationToken cancellationToken = default)
    {
        var row = await db.AcademicSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Id == AcademicSettings.SingletonId, cancellationToken);
        return row is null ? null : Record(row, yearChanged: false);
    }

    public async Task<SettingsRecord?> UpdateAsync(SettingsChange change, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var rows = await db.AcademicSettings
            .FromSql($"SELECT * FROM academic_settings WHERE id = {AcademicSettings.SingletonId} FOR UPDATE")
            .ToListAsync(cancellationToken);
        var settings = rows.SingleOrDefault();
        if (settings is null)
        {
            return null;
        }

        var before = Snapshot(settings);
        var yearChanged = !string.Equals(settings.AcademicYear, change.AcademicYear, StringComparison.Ordinal);

        settings.AcademicYear = change.AcademicYear;
        settings.CurrentSemester = change.CurrentSemester;
        settings.InstitutionName = change.InstitutionName;
        settings.InstitutionShortName = change.InstitutionShortName;
        settings.TimeZone = change.TimeZone;
        settings.SupportEmail = change.SupportEmail;
        settings.SupportUrl = change.SupportUrl;
        settings.UpdatedAt = clock.GetUtcNow();
        settings.UpdatedByUserId = actorUserId;

        audit.Record(db, AuditActions.SettingsChanged, AuditSubjects.Settings, AcademicSettings.SingletonId.ToString(System.Globalization.CultureInfo.InvariantCulture), new { before, after = Snapshot(settings) });
        await db.SaveChangesAsync(cancellationToken);

        if (yearChanged)
        {
            // The statements read the settings row just written (same transaction): the count becomes the new year's.
            await StartupBackfills.ReconcileEnrolledCountAsync(db, cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return Record(settings, yearChanged);
    }

    private static SettingsRecord Record(AcademicSettings s, bool yearChanged) =>
        new(s.AcademicYear, s.CurrentSemester, s.InstitutionName, s.InstitutionShortName, s.TimeZone, s.SupportEmail, s.SupportUrl, s.UpdatedAt, yearChanged);

    private static object Snapshot(AcademicSettings s) => new
    {
        academicYear = s.AcademicYear,
        currentSemester = GradeNames.Of(s.CurrentSemester),
        institutionName = s.InstitutionName,
        institutionShortName = s.InstitutionShortName,
        timeZone = s.TimeZone,
        supportEmail = s.SupportEmail,
        supportUrl = s.SupportUrl,
    };
}
