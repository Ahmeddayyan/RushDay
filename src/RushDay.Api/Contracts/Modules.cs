namespace RushDay.Api.Contracts;

public sealed record ModuleCatalogueItem(string Code, string Title, int Credits, string Semester, int Capacity);

public sealed record ModuleDetail(
    string Code,
    string Title,
    int Credits,
    string Semester,
    int Capacity,
    int Enrolled,
    int PlacesRemaining);
