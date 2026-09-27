using System.Text.Json;
using PokerEventSourced.Import;
using PokerEventSourced.Import.Phh;
using PokerEventSourced.Store;
using PokerEventSourced.Tests.TestSupport;

namespace PokerEventSourced.Tests.Store;

public class PolecatSmokeTests
{
    public sealed record SmokeStarted(string Label);

    public sealed record SmokeHappened(int Number);

    [Fact]
    public async Task Appends_and_reads_back_a_string_keyed_stream()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var database = await TestDatabase.CreateAsync();
        await using var store = PokerStore.Create(database.ConnectionString);

        const string streamKey = "phh:Test:1";
        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream(streamKey, new SmokeStarted("hello"), new SmokeHappened(42));
            await session.SaveChangesAsync(ct);
        }

        await using (var query = store.QuerySession())
        {
            var events = await query.Events.FetchStreamAsync(streamKey, token: ct);

            Assert.Collection(
                events,
                e => Assert.Equal(new SmokeStarted("hello"), e.Data),
                e => Assert.Equal(new SmokeHappened(42), e.Data));
        }
    }

    [Fact]
    public async Task A_parsed_hand_round_trips_through_the_store()
    {
        var ct = TestContext.Current.CancellationToken;
        const string file = "phh/dwan-ivey-2009.phh";
        var hand = Assert.IsType<ParsedHand>(Assert.Single(new PhhParser().Parse(RepoPaths.ReadSample(file), file)));
        await using var database = await TestDatabase.CreateAsync();
        await using var store = PokerStore.Create(database.ConnectionString);

        await using (var session = store.LightweightSession())
        {
            session.Events.StartStream(hand.StreamKey, hand.Events);
            await session.SaveChangesAsync(ct);
        }

        await using var query = store.QuerySession();
        var stored = (await query.Events.FetchStreamAsync(hand.StreamKey, token: ct)).Select(e => e.Data).ToList();

        Assert.Equal(hand.Events.Count, stored.Count);
        for (var i = 0; i < stored.Count; i++)
        {
            // Records with card lists compare lists by reference, so compare their JSON instead.
            Assert.Equal(hand.Events[i].GetType(), stored[i].GetType());
            Assert.Equal(JsonSerializer.Serialize(hand.Events[i], hand.Events[i].GetType()), JsonSerializer.Serialize(stored[i], stored[i].GetType()));
        }
    }
}
