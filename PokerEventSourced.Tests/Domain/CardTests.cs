using System.Text.Json;
using PokerEventSourced.Domain.Cards;

namespace PokerEventSourced.Tests.Domain;

public class CardTests
{
    [Theory]
    [InlineData("Ah", Rank.Ace, Suit.Hearts)]
    [InlineData("Tc", Rank.Ten, Suit.Clubs)]
    [InlineData("2d", Rank.Two, Suit.Diamonds)]
    [InlineData("ks", Rank.King, Suit.Spades)]
    public void Parses_rank_and_suit(string text, Rank rank, Suit suit)
    {
        Assert.Equal(new Card(rank, suit), Card.Parse(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("1h")]
    [InlineData("Ax")]
    [InlineData("Ahh")]
    public void Rejects_non_cards(string text)
    {
        Assert.False(Card.TryParse(text, out _));
    }

    [Fact]
    public void Formats_as_two_characters()
    {
        Assert.Equal("Qd", new Card(Rank.Queen, Suit.Diamonds).ToString());
    }

    [Fact]
    public void Parses_concatenated_cards()
    {
        var cards = Card.ParseMany("Jc3d5c");

        Assert.Equal("Jc3d5c", Card.Format(cards!));
    }

    [Theory]
    [InlineData("????")]
    [InlineData("A?Kd")]
    public void Unknown_cards_are_null(string text)
    {
        Assert.Null(Card.ParseMany(text));
    }

    [Theory]
    [InlineData("Jc3")]
    [InlineData("JcXd")]
    public void Rejects_malformed_card_lists(string text)
    {
        Assert.Throws<FormatException>(() => Card.ParseMany(text));
    }

    [Fact]
    public void Serializes_to_json_as_a_string()
    {
        var cards = Card.ParseMany("Ac2d")!;

        var json = JsonSerializer.Serialize(cards);

        Assert.Equal("""["Ac","2d"]""", json);
        Assert.Equal(cards, JsonSerializer.Deserialize<List<Card>>(json));
    }
}
