using SportMeet.Application.Imports;

namespace SportMeet.Import.Tests;

public class GeocodePolicyTests
{
    [Fact]
    public void An_exact_building_with_one_query_is_trusted()
        => Assert.False(GeocodePolicy.NeedsConfirm("ROOFTOP", partialMatch: false, venueOnly: false, queriesApartMetres: null));

    [Fact]
    public void Two_queries_that_agree_are_trusted()
        => Assert.False(GeocodePolicy.NeedsConfirm("ROOFTOP", false, false, queriesApartMetres: 120));

    [Theory]
    [InlineData("GEOMETRIC_CENTER")]
    [InlineData("APPROXIMATE")]
    [InlineData("RANGE_INTERPOLATED")]
    public void Anything_less_precise_than_a_building_needs_a_look(string type)
        => Assert.True(GeocodePolicy.NeedsConfirm(type, false, false, null));

    [Fact]
    public void A_partial_match_needs_a_look()
        => Assert.True(GeocodePolicy.NeedsConfirm("ROOFTOP", partialMatch: true, false, null));

    [Fact]
    public void A_venue_name_alone_is_never_trusted()
        => Assert.True(GeocodePolicy.NeedsConfirm("ROOFTOP", false, venueOnly: true, null));

    [Fact]
    public void Queries_more_than_300m_apart_need_a_look()
        => Assert.True(GeocodePolicy.NeedsConfirm("ROOFTOP", false, false, queriesApartMetres: 301));

    [Fact]
    public void Distance_is_roughly_right()
    {
        // Flinders Street Station to Melbourne Museum is about 2.4 km.
        var metres = GeocodePolicy.DistanceMetres(-37.8183, 144.9671, -37.8033, 144.9717);
        Assert.InRange(metres, 1_500, 2_000);
        Assert.Equal(0, GeocodePolicy.DistanceMetres(-37.8, 144.9, -37.8, 144.9), 3);
    }
}
