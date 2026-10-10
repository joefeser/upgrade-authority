namespace Cassowary.Booker;

using Cassowary.Core;
using Cassowary.GroundOps;

public static class BookingService
{
    public static string Describe(BookingRecord booking, FuelContract fuel)
        => $"{booking.BookingId} on {booking.FlightNumber} with {fuel.Liters}L at {fuel.Station}";
}
