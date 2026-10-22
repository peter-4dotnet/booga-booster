using FourDotnet.BoogaBooster.Core.Cqrs;
using FourDotnet.BoogaBooster.DigitalTwin.Abstractions.DataTransferObjects;

namespace FourDotnet.BoogaBooster.DigitalTwin.Features.GetRideTelemetry;

/// <summary>Reads the ride's current telemetry snapshot.</summary>
public sealed record GetRideTelemetryQuery : Query<RideTelemetry>;
