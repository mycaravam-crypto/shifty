using ShiftPlanner.Domain.Scheduling;
using Xunit;

namespace ShiftPlanner.Tests.Domain;

// issue #97: Schedule.Status/PublishedAt/PublishedBy used to be plain auto-properties with
// public setters — the Draft/Published/Archived state machine (issue #68) was enforced only by
// SchedulesController's own checks, not by the type itself. Publish/Archive now own that
// invariant directly; these tests exercise the domain method in isolation, without a
// controller/DB in the loop.
public class ScheduleTests
{
    private static Schedule NewDraftSchedule() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Test Schedule",
        StartDate = new DateOnly(2026, 8, 1),
        EndDate = new DateOnly(2026, 8, 31),
    };

    [Fact]
    public void Publish_FromDraft_SucceedsAndSetsBothFieldsTogether()
    {
        var schedule = NewDraftSchedule();
        var at = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);

        schedule.Publish("manager-1", at);

        Assert.Equal(ScheduleStatus.Published, schedule.Status);
        Assert.Equal(at, schedule.PublishedAt);
        Assert.Equal("manager-1", schedule.PublishedBy);
    }

    [Fact]
    public void Archive_FromPublished_Succeeds()
    {
        var schedule = NewDraftSchedule();
        schedule.Publish("manager-1", DateTimeOffset.UtcNow);

        schedule.Archive();

        Assert.Equal(ScheduleStatus.Archived, schedule.Status);
    }

    [Fact]
    public void Publish_FromDraft_ThenArchive_DirectlySkippingPublished_Throws()
    {
        var schedule = NewDraftSchedule();

        Assert.Throws<InvalidOperationException>(() => schedule.Archive());
        // Status is unchanged by the failed attempt.
        Assert.Equal(ScheduleStatus.Draft, schedule.Status);
    }

    [Fact]
    public void Publish_WhenAlreadyPublished_Throws()
    {
        var schedule = NewDraftSchedule();
        var firstPublishedAt = DateTimeOffset.UtcNow;
        schedule.Publish("manager-1", firstPublishedAt);

        Assert.Throws<InvalidOperationException>(() => schedule.Publish("manager-2", DateTimeOffset.UtcNow.AddMinutes(5)));
        // The failed re-publish attempt does not clobber the original values.
        Assert.Equal(firstPublishedAt, schedule.PublishedAt);
        Assert.Equal("manager-1", schedule.PublishedBy);
    }

    [Fact]
    public void Archive_WhenAlreadyArchived_Throws()
    {
        var schedule = NewDraftSchedule();
        schedule.Publish("manager-1", DateTimeOffset.UtcNow);
        schedule.Archive();

        Assert.Throws<InvalidOperationException>(() => schedule.Archive());
        Assert.Equal(ScheduleStatus.Archived, schedule.Status);
    }

    [Fact]
    public void Publish_WhenArchived_Throws()
    {
        var schedule = NewDraftSchedule();
        schedule.Publish("manager-1", DateTimeOffset.UtcNow);
        schedule.Archive();

        Assert.Throws<InvalidOperationException>(() => schedule.Publish("manager-2", DateTimeOffset.UtcNow));
        Assert.Equal(ScheduleStatus.Archived, schedule.Status);
    }
}
