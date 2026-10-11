namespace Cassowary.CrewWeb;

using Cassowary.Core;

public static class RosterPage
{
    public static string Render(BookingRecord booking) => $"crew view: {booking.FlightNumber}";
}
