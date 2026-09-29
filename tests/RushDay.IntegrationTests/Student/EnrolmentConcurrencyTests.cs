using System.Globalization;
using System.Net;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RushDay.Domain.Enrolments;
using RushDay.Domain.Modules;
using Xunit.Abstractions;

namespace RushDay.IntegrationTests.Student;

/// <summary>
/// The enrolment race that oversold CS3099 by 124 places in v0 (04-performance-and-ops.md section 2.3): 200 students fire
/// at a 30-place module at once and exactly 30 get in. The module is created here (<c>ZZ3001</c>, spring, capacity 30,
/// 15 credits, department ZZ), so the test is independent of <c>EnrolmentTests</c>' use of CS3099.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class EnrolmentConcurrencyTests(RushDayApiFactory factory, ITestOutputHelper output)
{
    private const int Capacity = 30;

    [Fact]
    public async Task Two_hundred_simultaneous_enrolments_fill_thirty_places_exactly()
    {
        await factory.CreateModuleAsync("ZZ3001", Semester.Spring, Capacity);
        var clients = await LoginRangeAsync(101, 300);
        try
        {
            // All 200 requests are in flight before any is awaited.
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var requests = clients.Select(client => Task.Run(async () =>
            {
                await start.Task;
                using var response = await client.EnrolAsync("ZZ3001");
                var body = await response.ReadJsonAsync();
                return (response.StatusCode, Type: body.ValueKind == System.Text.Json.JsonValueKind.Object && body.TryGetProperty("type", out var type) ? type.GetString() : null);
            })).ToList();
            start.SetResult();
            var outcomes = await Task.WhenAll(requests);

            var created = outcomes.Count(o => o.StatusCode == HttpStatusCode.Created);
            var full = outcomes.Count(o => o.StatusCode == HttpStatusCode.Conflict && o.Type == "urn:rushday:module-full");
            var summary = string.Join(", ", outcomes.GroupBy(o => $"{(int)o.StatusCode} {o.Type}").Select(g => $"{g.Key} x{g.Count()}"));
            output.WriteLine($"ZZ3001 rush: {summary}");

            Assert.True(created == Capacity && full == 170, $"Expected 30 x 201 and 170 x 409 module-full, got {summary}");

            var (enrolledCount, activeCount) = await CountsViaSqlAsync("ZZ3001");
            output.WriteLine($"ZZ3001: enrolled_count={enrolledCount} active={activeCount}");
            Assert.Equal((Capacity, Capacity), (enrolledCount, activeCount));
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    /// <summary>
    /// Enrolments and withdrawals racing on one module: ten holders withdraw while forty others try to enrol. Whatever the
    /// interleaving, the count never exceeds capacity and always equals the active rows; every place freed is either
    /// still free or taken by exactly one newcomer.
    /// </summary>
    [Fact]
    public async Task Racing_enrolments_and_withdrawals_never_oversell()
    {
        const int places = 10;
        await factory.CreateModuleAsync("ZZ3002", Semester.Spring, places);
        var holders = await LoginRangeAsync(101, 110);
        var newcomers = await LoginRangeAsync(111, 150);
        try
        {
            foreach (var holder in holders)
            {
                using var enrol = await holder.EnrolAsync("ZZ3002");
                Assert.Equal(HttpStatusCode.Created, enrol.StatusCode);
            }

            Assert.Equal((places, places), await CountsViaSqlAsync("ZZ3002"));

            // Withdrawals are spread over ~150 ms while every newcomer keeps trying (a student refreshing the page), so
            // enrolments land before, between and after the withdrawals' commits.
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var withdrawals = holders.Select((client, index) => Task.Run(async () =>
            {
                await start.Task;
                await Task.Delay(index * 15);
                using var response = await client.WithdrawAsync("ZZ3002");
                return response.StatusCode;
            })).ToList();
            var attempts = 0;
            var enrolments = newcomers.Select(client => Task.Run(async () =>
            {
                await start.Task;
                for (var attempt = 0; attempt < 60; attempt++)
                {
                    Interlocked.Increment(ref attempts);
                    using var response = await client.EnrolAsync("ZZ3002");
                    if (response.StatusCode != HttpStatusCode.Conflict)
                    {
                        return response.StatusCode;
                    }

                    await Task.Delay(10);
                }

                return HttpStatusCode.Conflict;
            })).ToList();
            start.SetResult();

            var withdrawn = (await Task.WhenAll(withdrawals)).Count(s => s == HttpStatusCode.NoContent);
            var enrolmentCodes = await Task.WhenAll(enrolments);
            var accepted = enrolmentCodes.Count(s => s == HttpStatusCode.Created);
            var refused = enrolmentCodes.Count(s => s == HttpStatusCode.Conflict);

            var (enrolledCount, activeCount) = await CountsViaSqlAsync("ZZ3002");
            output.WriteLine($"ZZ3002 race: withdrawn={withdrawn} accepted={accepted} refused={refused} attempts={attempts} enrolled_count={enrolledCount} active={activeCount}");

            Assert.Equal(places, withdrawn);
            Assert.Equal(newcomers.Count, accepted + refused);
            Assert.InRange(accepted, 1, places);
            Assert.Equal(places - withdrawn + accepted, activeCount);
            Assert.Equal(activeCount, enrolledCount);
            Assert.InRange(enrolledCount, 0, places);
        }
        finally
        {
            foreach (var client in holders.Concat(newcomers))
            {
                client.Dispose();
            }
        }
    }

    /// <summary>
    /// Double clicks: ten students each fire three simultaneous requests at a five-place module. The student row lock
    /// and the unique (student, module) index give each student at most one row and one place.
    /// </summary>
    [Fact]
    public async Task Simultaneous_duplicates_from_one_student_take_one_place()
    {
        const int places = 5;
        await factory.CreateModuleAsync("ZZ3003", Semester.Spring, places);
        var clients = await LoginRangeAsync(151, 160);
        try
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var requests = clients.SelectMany((client, index) => Enumerable.Range(0, 3).Select(_ => Task.Run(async () =>
            {
                await start.Task;
                using var response = await client.EnrolAsync("ZZ3003");
                return (Student: index, response.StatusCode);
            }))).ToList();
            start.SetResult();
            var outcomes = await Task.WhenAll(requests);

            var winners = outcomes.Where(o => o.StatusCode == HttpStatusCode.Created).Select(o => o.Student).ToList();
            output.WriteLine($"ZZ3003 duplicates: 201 x{winners.Count} from {winners.Distinct().Count()} students, 409 x{outcomes.Count(o => o.StatusCode == HttpStatusCode.Conflict)}");

            Assert.Equal(places, winners.Count);
            Assert.Equal(places, winners.Distinct().Count());
            Assert.All(outcomes, o => Assert.True(o.StatusCode is HttpStatusCode.Created or HttpStatusCode.Conflict, o.StatusCode.ToString()));
            Assert.Equal((places, places), await CountsViaSqlAsync("ZZ3003"));
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    /// <summary>
    /// The claiming statement on its own, with no fast path in front of it: fifty transactions hold their claim open
    /// together and exactly <c>capacity</c> of them get a row back (READ COMMITTED re-evaluates the <c>WHERE</c> on the
    /// committed row, 04-performance-and-ops.md section 2.1).
    /// </summary>
    [Fact]
    public async Task The_claiming_statement_alone_never_exceeds_capacity()
    {
        var moduleId = await factory.CreateModuleAsync("ZZ3004", Semester.Spring, Capacity);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var builder = new NpgsqlConnectionStringBuilder(factory.ConnectionString) { MaxPoolSize = 60, Pooling = true, ApplicationName = "rushday-claim-probe" };
        await using var source = NpgsqlDataSource.Create(builder.ConnectionString);

        var claims = Enumerable.Range(0, 50).Select(_ => Task.Run(async () =>
        {
            await using var connection = await source.OpenConnectionAsync();
            await using var transaction = await connection.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted);
            await start.Task;
            await using var command = new NpgsqlCommand(RushDay.Infrastructure.Enrolments.EnrolmentService.ClaimPlaceSql, connection, transaction);
            command.Parameters.AddWithValue("module", moduleId);
            command.Parameters.AddWithValue("now", DateTimeOffset.UtcNow);
            bool claimed;
            await using (var reader = await command.ExecuteReaderAsync())
            {
                claimed = await reader.ReadAsync();
            }

            // Hold the row lock a little, so the others queue behind it and re-check on commit.
            await using (var pause = new NpgsqlCommand("SELECT pg_sleep(0.02)", connection, transaction))
            {
                await pause.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            return claimed;
        })).ToList();
        start.SetResult();
        var results = await Task.WhenAll(claims);

        output.WriteLine($"ZZ3004 raw claims: {results.Count(c => c)} of {results.Length}");
        Assert.Equal(Capacity, results.Count(c => c));
        Assert.Equal(Capacity, (await CountsViaSqlAsync("ZZ3004")).EnrolledCount);
    }

    /// <summary>Signs in <c>S{from}</c>..<c>S{to}</c> (demo sessions), a few at a time.</summary>
    private async Task<List<HttpClient>> LoginRangeAsync(int from, int to)
    {
        var numbers = Enumerable.Range(from, to - from + 1).Select(n => "S" + n.ToString("D6", CultureInfo.InvariantCulture)).ToList();
        var clients = new HttpClient[numbers.Count];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, numbers.Count),
            new ParallelOptions { MaxDegreeOfParallelism = 8 },
            async (i, _) => clients[i] = await factory.LoginStudentAsync(numbers[i]));
        return [.. clients];
    }

    /// <summary>The assertion of 04-performance-and-ops.md section 2.3, in SQL on a connection of its own.</summary>
    private async Task<(int EnrolledCount, int ActiveCount)> CountsViaSqlAsync(string code)
    {
        await using var connection = new NpgsqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT enrolled_count, (SELECT count(*) FROM enrolments WHERE module_id = m.id AND status = 'Active')::int FROM modules m WHERE code = @code",
            connection);
        command.Parameters.AddWithValue("code", code);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt32(0), reader.GetInt32(1));
    }
}
