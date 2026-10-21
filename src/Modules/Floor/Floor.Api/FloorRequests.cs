using Floor.Domain;

namespace Floor.Api;

public sealed record CreatedResponse(Guid Id);

public sealed record CreateDiningAreaRequest(Guid LocationId, string? Name);

public sealed record NameRequest(string? Name);

public sealed record AddTableRequest(string? Label, int Capacity);

public sealed record CapacityRequest(int Capacity);

public sealed record TableLabelRequest(string? Label);

public sealed record TableLayoutRequest(int X, int Y, int Width, int Height, TableShape Shape, int Rotation = 0);

/// <summary><paramref name="ReservationId"/> : la réservation honorée ; absente pour un client sans réservation.</summary>
public sealed record OpenTableSessionRequest(Guid TableId, int GuestCount, Guid? ReservationId = null);

public sealed record GuestCountRequest(int GuestCount);

public sealed record MoveTableSessionRequest(Guid TableId);

public sealed record MergeTableSessionsRequest(Guid IntoTableSessionId);

public sealed record QrCodesRequest(Guid LocationId);

/// <param name="Phone">Facultatif (format international) : pour prévenir le groupe quand sa table est prête.</param>
/// <param name="QuotedMinutes">Attente annoncée au client, en minutes.</param>
public sealed record AddWaitingPartyRequest(Guid LocationId, string? Name, int PartySize, string? Phone = null, string? Note = null, int? QuotedMinutes = null)
{
    public override string ToString() => "AddWaitingPartyRequest (***)";
}

public sealed record SeatWaitingPartyRequest(Guid TableId);
