using RushDay.Infrastructure.Queries;

namespace RushDay.UnitTests.Grades;

/// <summary>
/// The <c>MarksStatus</c> rule of 02-api.md section 7 (review S6 E1): while a module is in draft every grade row counts
/// as entered; once it has left draft only Submitted or Published grades do, so a Draft on an active enrolment (a
/// student withdrawn before the submit and enrolled again) is missing and the module is not publishable.
/// </summary>
public sealed class MarksStatusTests
{
    [Fact]
    public void In_draft_every_grade_row_is_entered()
    {
        var status = MarksStatusQuery.From(total: 3, graded: 2, live: 0, scheduled: 0, submitted: 0, submittedAt: null, liveAt: null, scheduledAt: null, drafts: 2);

        Assert.Equal((MarksState.Draft, 2, 1, 3), (status.Status, status.Entered, status.Missing, status.Total));
        Assert.False(status.IsPublishable);
        Assert.True(status.IsEditable);
    }

    [Fact]
    public void A_draft_on_a_submitted_module_is_missing_and_blocks_publication()
    {
        var status = MarksStatusQuery.From(total: 2, graded: 2, live: 0, scheduled: 0, submitted: 1, submittedAt: DateTimeOffset.UnixEpoch, liveAt: null, scheduledAt: null, drafts: 1);

        Assert.Equal((MarksState.Submitted, 1, 1, 2), (status.Status, status.Entered, status.Missing, status.Total));
        Assert.False(status.IsPublishable);
        Assert.False(status.IsEditable);
    }

    [Fact]
    public void A_fully_submitted_module_is_publishable()
    {
        var status = MarksStatusQuery.From(total: 2, graded: 2, live: 0, scheduled: 0, submitted: 2, submittedAt: DateTimeOffset.UnixEpoch, liveAt: null, scheduledAt: null, drafts: 0);

        Assert.Equal((MarksState.Submitted, 2, 0), (status.Status, status.Entered, status.Missing));
        Assert.True(status.IsPublishable);
    }

    [Fact]
    public void A_draft_beside_published_marks_is_missing_too()
    {
        var at = DateTimeOffset.UnixEpoch;
        var live = MarksStatusQuery.From(total: 3, graded: 3, live: 2, scheduled: 0, submitted: 0, submittedAt: at, liveAt: at, scheduledAt: null, drafts: 1);
        var scheduled = MarksStatusQuery.From(total: 3, graded: 2, live: 0, scheduled: 2, submitted: 0, submittedAt: at, liveAt: null, scheduledAt: at, drafts: 0);

        Assert.Equal((MarksState.Published, 2, 1), (live.Status, live.Entered, live.Missing));
        Assert.Equal((MarksState.Scheduled, 2, 1), (scheduled.Status, scheduled.Entered, scheduled.Missing));
    }

    [Fact]
    public void No_enrolment_and_no_grade_is_no_students()
    {
        Assert.Same(MarksStatusData.NoStudents, MarksStatusQuery.From(0, 0, 0, 0, 0, null, null, null));
    }
}
