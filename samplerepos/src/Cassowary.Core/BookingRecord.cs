namespace Cassowary.Core;

// Fictional domain type — the estate's only payload is identity + version.
public sealed record BookingRecord(string BookingId, string FlightNumber);
