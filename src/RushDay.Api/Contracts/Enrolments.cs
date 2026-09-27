namespace RushDay.Api.Contracts;

public sealed record EnrolRequest(string ModuleCode);

public sealed record EnrolmentResponse(Guid EnrolmentId, string StudentNumber, string ModuleCode, DateTimeOffset EnrolledAt);
