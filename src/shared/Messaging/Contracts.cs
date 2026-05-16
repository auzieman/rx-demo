// /src/shared/Messaging/Contracts.cs
namespace Shared.Messaging;

public record ApprovePrescriptionCommand(string RxId, string ApprovedBy, DateTimeOffset ApprovedAt, string? Notes, string? FaultMode = null);
public record RefillRequestCommand(string RxId, int RefillCount, DateTimeOffset RequestedAt, string? FaultMode = null);
public record PrescriptionChangedEvent(string RxId, string Status, int Version, DateTimeOffset ChangedAt, string? FaultMode = null);
