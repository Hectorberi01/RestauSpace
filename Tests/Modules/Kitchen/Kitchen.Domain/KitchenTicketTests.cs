namespace Kitchen.Domain;

public class KitchenTicketTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly LocationId Location = LocationId.New();
    private static readonly OrderId Order = OrderId.New();
    private static readonly KitchenStationReference Grill = KitchenRefs.Station(Tenant, Location);
    private static readonly KitchenStationReference Cold = KitchenRefs.Station(Tenant, Location);
    private readonly FakeClock _clock = new();

    private static KitchenOrderSnapshot Snapshot(params KitchenStationReference[] stations) =>
        KitchenOrderSnapshot.Create(
            Order,
            Tenant,
            Location,
            stations.Select((station, index) => KitchenOrderLine.Create(OrderItemId.New(), $"Dish {index + 1}", index + 1, station, null, [], null)));

    /// <summary>Ticket en file d'attente avec <paramref name="itemCount"/> articles, événements effacés.</summary>
    private KitchenTicket NewTicket(int itemCount = 2, TicketReadinessPolicy? policy = null)
    {
        var stations = Enumerable.Range(0, itemCount).Select(i => i % 2 == 0 ? Grill : Cold).ToArray();
        var ticket = KitchenTicket.CreateFromOrder(Snapshot(stations), KitchenPriority.Normal, _clock, policy);
        ticket.ClearDomainEvents();
        return ticket;
    }

    private static KitchenTicketItemId ItemId(KitchenTicket ticket, int index) => ticket.Items.ElementAt(index).Id;

    private void PrepareItem(KitchenTicket ticket, int index)
    {
        ticket.MarkItemStarted(ItemId(ticket, index), _clock);
        ticket.MarkItemReady(ItemId(ticket, index), _clock);
    }

    private KitchenTicket ReadyTicket()
    {
        var ticket = NewTicket();
        PrepareItem(ticket, 0);
        PrepareItem(ticket, 1);
        ticket.MarkReady(_clock);
        ticket.ClearDomainEvents();
        return ticket;
    }

    private KitchenTicket CompletedTicket()
    {
        var ticket = ReadyTicket();
        ticket.Complete(_clock);
        ticket.ClearDomainEvents();
        return ticket;
    }

    private KitchenTicket CancelledTicket()
    {
        var ticket = NewTicket();
        ticket.Cancel(_clock);
        ticket.ClearDomainEvents();
        return ticket;
    }

    // ---- CreateFromOrder ----

    [Fact]
    public void CreateFromOrder_builds_a_queued_ticket_with_queued_items_and_raises_KitchenTicketCreated()
    {
        var snapshot = Snapshot(Grill, Cold);

        var ticket = KitchenTicket.CreateFromOrder(snapshot, KitchenPriority.High, _clock);

        Assert.Equal(Tenant, ticket.TenantId);
        Assert.Equal(Location, ticket.LocationId);
        Assert.Equal(Order, ticket.OrderId);
        Assert.Equal(KitchenTicketStatus.Queued, ticket.Status);
        Assert.Equal(KitchenPriority.High, ticket.Priority);
        Assert.Equal(TicketReadinessPolicy.AllItemsReady, ticket.ReadinessPolicy);
        Assert.Equal(_clock.UtcNow, ticket.CreatedAtUtc);
        Assert.Null(ticket.StartedAtUtc);
        Assert.False(ticket.IsClosed);
        Assert.False(ticket.CanBeMarkedReady);

        Assert.Equal(2, ticket.Items.Count);
        var lines = snapshot.Lines.ToList();
        var items = ticket.Items.ToList();
        for (var i = 0; i < lines.Count; i++)
        {
            Assert.Equal(lines[i].OrderItemId, items[i].OrderItemId);
            Assert.Equal(lines[i].Name, items[i].Name);
            Assert.Equal(lines[i].Quantity, items[i].Quantity);
            Assert.Equal(lines[i].Station.Id, items[i].StationId);
            Assert.Equal(KitchenTicketItemStatus.Queued, items[i].Status);
            Assert.False(items[i].IsReady);
        }

        var created = DomainAssert.Raised<KitchenTicketCreated>(ticket);
        Assert.Equal(Tenant, created.TenantId);
        Assert.Equal(ticket.Id, created.TicketId);
        Assert.Equal(Order, created.OrderId);
        Assert.Equal(Location, created.LocationId);
        Assert.Equal(KitchenPriority.High, created.Priority);
        Assert.Equal(2, created.ItemCount);
        Assert.Equal(_clock.UtcNow, created.OccurredAtUtc);
    }

    [Fact]
    public void KitchenTicketCreated_carries_the_routing_of_every_line_in_ticket_order()
    {
        var ticket = KitchenTicket.CreateFromOrder(Snapshot(Grill, Cold, Grill), KitchenPriority.Normal, _clock);

        var created = DomainAssert.Raised<KitchenTicketCreated>(ticket);

        Assert.Equal(
            ticket.Items.Select(i => new KitchenTicketLineRouting(i.Id, i.OrderItemId, i.StationId)),
            created.Lines);
        Assert.Equal([Grill.Id, Cold.Id, Grill.Id], created.Lines.Select(l => l.StationId));
        Assert.Throws<NotSupportedException>(() => ((IList<KitchenTicketLineRouting>)created.Lines).Clear());
    }

    [Fact]
    public void CreateFromOrder_copies_variant_modifiers_and_note_onto_the_items()
    {
        var withDetails = KitchenOrderLine.Create(
            OrderItemId.New(), "Burger", 2, Grill, "Large", ["No onion", "Extra cheese"], "Peanut allergy");
        var plain = KitchenOrderLine.Create(OrderItemId.New(), "Fries", 1, Cold, null, [], null);

        var ticket = KitchenTicket.CreateFromOrder(
            KitchenOrderSnapshot.Create(Order, Tenant, Location, [withDetails, plain]), KitchenPriority.Normal, _clock);

        var burger = ticket.Items.First();
        Assert.Equal("Burger", burger.Name);
        Assert.Equal("Large", burger.VariantName);
        Assert.Equal(["No onion", "Extra cheese"], burger.Modifiers);
        Assert.Equal("Peanut allergy", burger.Note);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)burger.Modifiers).Add("Bacon"));

        var fries = ticket.Items.Last();
        Assert.Null(fries.VariantName);
        Assert.Empty(fries.Modifiers);
        Assert.Null(fries.Note);
    }

    [Fact]
    public void CreateFromOrder_keeps_the_given_readiness_policy()
    {
        var ticket = NewTicket(policy: TicketReadinessPolicy.AtLeast(50));

        Assert.Equal(TicketReadinessPolicy.AtLeast(50), ticket.ReadinessPolicy);
    }

    [Fact]
    public void CreateFromOrder_rejects_a_station_of_another_tenant()
    {
        var foreign = KitchenRefs.Station(TenantId.New(), Location);

        DomainAssert.Throws(
            CommonErrors.TenantMismatch,
            () => KitchenTicket.CreateFromOrder(Snapshot(Grill, foreign), KitchenPriority.Normal, _clock));
    }

    [Fact]
    public void CreateFromOrder_rejects_a_station_of_another_location()
    {
        var elsewhere = KitchenRefs.Station(Tenant, LocationId.New());

        DomainAssert.Throws(
            KitchenTicketErrors.StationOtherLocation,
            () => KitchenTicket.CreateFromOrder(Snapshot(elsewhere), KitchenPriority.Normal, _clock));
    }

    [Fact]
    public void CreateFromOrder_rejects_an_inactive_station()
    {
        var inactive = KitchenRefs.Station(Tenant, Location, isActive: false);

        DomainAssert.Throws(
            KitchenTicketErrors.StationInactive,
            () => KitchenTicket.CreateFromOrder(Snapshot(inactive), KitchenPriority.Normal, _clock));
    }

    [Fact]
    public void CreateFromOrder_rejects_an_unknown_priority() =>
        DomainAssert.Throws(
            KitchenTicketErrors.PriorityUnknown,
            () => KitchenTicket.CreateFromOrder(Snapshot(Grill), (KitchenPriority)0, _clock));

    // ---- Start ----

    [Fact]
    public void Start_moves_a_queued_ticket_to_in_progress_and_raises_KitchenPreparationStarted()
    {
        var ticket = NewTicket();
        _clock.Advance(TimeSpan.FromMinutes(2));

        ticket.Start(_clock);

        Assert.Equal(KitchenTicketStatus.InProgress, ticket.Status);
        Assert.Equal(_clock.UtcNow, ticket.StartedAtUtc);
        Assert.All(ticket.Items, i => Assert.Equal(KitchenTicketItemStatus.Queued, i.Status));
        var started = DomainAssert.Raised<KitchenPreparationStarted>(ticket);
        Assert.Equal(Tenant, started.TenantId);
        Assert.Equal(ticket.Id, started.TicketId);
        Assert.Equal(Order, started.OrderId);
        Assert.Equal(Location, started.LocationId);
        Assert.Equal(_clock.UtcNow, started.OccurredAtUtc);
    }

    [Fact]
    public void Start_rejects_a_ticket_already_in_progress()
    {
        var ticket = NewTicket();
        ticket.Start(_clock);
        ticket.ClearDomainEvents();

        DomainAssert.Throws(KitchenTicketErrors.NotQueued, () => ticket.Start(_clock));

        DomainAssert.NoEvents(ticket);
    }

    [Fact]
    public void Start_rejects_a_ready_ticket()
    {
        var ticket = ReadyTicket();

        DomainAssert.Throws(KitchenTicketErrors.NotQueued, () => ticket.Start(_clock));

        Assert.Equal(KitchenTicketStatus.Ready, ticket.Status);
    }

    // ---- Articles ----

    [Fact]
    public void MarkItemStarted_on_a_queued_ticket_starts_the_item_and_the_ticket()
    {
        var ticket = NewTicket();
        var first = ticket.Items.First();

        ticket.MarkItemStarted(first.Id, _clock);

        Assert.Equal(KitchenTicketItemStatus.Preparing, first.Status);
        Assert.Equal(KitchenTicketItemStatus.Queued, ticket.Items.Last().Status);
        Assert.Equal(KitchenTicketStatus.InProgress, ticket.Status);
        Assert.Equal(_clock.UtcNow, ticket.StartedAtUtc);
        DomainAssert.Raised<KitchenPreparationStarted>(ticket);
        var itemStarted = DomainAssert.Raised<KitchenTicketItemStarted>(ticket);
        Assert.Equal(ticket.Id, itemStarted.TicketId);
        Assert.Equal(first.Id, itemStarted.ItemId);
        Assert.Equal(first.OrderItemId, itemStarted.OrderItemId);
        Assert.Equal(first.StationId, itemStarted.StationId);
        Assert.Equal(Tenant, itemStarted.TenantId);
        Assert.Equal(Location, itemStarted.LocationId);
    }

    [Fact]
    public void KitchenPreparationStarted_is_raised_only_once_per_ticket()
    {
        var ticket = NewTicket();
        var startedAt = _clock.UtcNow;

        ticket.MarkItemStarted(ItemId(ticket, 0), _clock);
        _clock.Advance(TimeSpan.FromMinutes(3));
        ticket.MarkItemStarted(ItemId(ticket, 1), _clock);

        DomainAssert.Raised<KitchenPreparationStarted>(ticket);
        Assert.Equal(2, ticket.DomainEvents.OfType<KitchenTicketItemStarted>().Count());
        Assert.Equal(startedAt, ticket.StartedAtUtc);
    }

    [Fact]
    public void MarkItemStarted_after_Start_does_not_raise_KitchenPreparationStarted_again()
    {
        var ticket = NewTicket();
        ticket.Start(_clock);
        ticket.ClearDomainEvents();

        ticket.MarkItemStarted(ItemId(ticket, 0), _clock);

        DomainAssert.NotRaised<KitchenPreparationStarted>(ticket);
        DomainAssert.Raised<KitchenTicketItemStarted>(ticket);
    }

    [Fact]
    public void MarkItemStarted_rejects_an_item_that_is_already_preparing_or_ready()
    {
        var ticket = NewTicket();
        ticket.MarkItemStarted(ItemId(ticket, 0), _clock);
        PrepareItem(ticket, 1);
        ticket.ClearDomainEvents();

        DomainAssert.Throws(KitchenTicketErrors.ItemNotQueued, () => ticket.MarkItemStarted(ItemId(ticket, 0), _clock));
        DomainAssert.Throws(KitchenTicketErrors.ItemNotQueued, () => ticket.MarkItemStarted(ItemId(ticket, 1), _clock));

        Assert.Equal(KitchenTicketItemStatus.Preparing, ticket.Items.First().Status);
        Assert.Equal(KitchenTicketItemStatus.Ready, ticket.Items.Last().Status);
        DomainAssert.NoEvents(ticket);
    }

    [Fact]
    public void MarkItemStarted_rejects_an_unknown_item_and_leaves_the_ticket_queued()
    {
        var ticket = NewTicket();

        DomainAssert.Throws(KitchenTicketErrors.ItemNotFound, () => ticket.MarkItemStarted(KitchenTicketItemId.New(), _clock));

        Assert.Equal(KitchenTicketStatus.Queued, ticket.Status);
        DomainAssert.NoEvents(ticket);
    }

    [Fact]
    public void MarkItemReady_moves_a_preparing_item_to_ready_and_raises_KitchenTicketItemReady()
    {
        var ticket = NewTicket();
        var first = ticket.Items.First();
        ticket.MarkItemStarted(first.Id, _clock);
        ticket.ClearDomainEvents();

        ticket.MarkItemReady(first.Id, _clock);

        Assert.Equal(KitchenTicketItemStatus.Ready, first.Status);
        Assert.True(first.IsReady);
        var itemReady = DomainAssert.Raised<KitchenTicketItemReady>(ticket);
        Assert.Equal(first.Id, itemReady.ItemId);
        Assert.Equal(first.OrderItemId, itemReady.OrderItemId);
        Assert.Equal(first.StationId, itemReady.StationId);
        Assert.Equal(ticket.Id, itemReady.TicketId);
        Assert.Equal(Location, itemReady.LocationId);
    }

    [Fact]
    public void MarkItemReady_rejects_skipping_the_preparing_step()
    {
        var ticket = NewTicket();
        ticket.Start(_clock);
        ticket.ClearDomainEvents();

        DomainAssert.Throws(KitchenTicketErrors.ItemNotPreparing, () => ticket.MarkItemReady(ItemId(ticket, 0), _clock));

        Assert.Equal(KitchenTicketItemStatus.Queued, ticket.Items.First().Status);
        DomainAssert.NoEvents(ticket);
    }

    [Fact]
    public void MarkItemReady_rejects_an_item_that_is_already_ready_and_an_unknown_item()
    {
        var ticket = NewTicket();
        PrepareItem(ticket, 0);
        ticket.ClearDomainEvents();

        DomainAssert.Throws(KitchenTicketErrors.ItemNotPreparing, () => ticket.MarkItemReady(ItemId(ticket, 0), _clock));
        DomainAssert.Throws(KitchenTicketErrors.ItemNotFound, () => ticket.MarkItemReady(KitchenTicketItemId.New(), _clock));

        DomainAssert.NoEvents(ticket);
    }

    [Fact]
    public void Last_item_becoming_ready_does_not_mark_the_ticket_ready_by_itself()
    {
        var ticket = NewTicket();

        PrepareItem(ticket, 0);
        PrepareItem(ticket, 1);

        Assert.Equal(KitchenTicketStatus.InProgress, ticket.Status);
        Assert.True(ticket.CanBeMarkedReady);
        Assert.Null(ticket.ReadyAtUtc);
        DomainAssert.NotRaised<KitchenTicketReady>(ticket);
    }

    // ---- MarkReady ----

    [Fact]
    public void MarkReady_rejects_a_ticket_whose_items_are_not_all_ready()
    {
        var ticket = NewTicket();
        PrepareItem(ticket, 0);
        ticket.MarkItemStarted(ItemId(ticket, 1), _clock);
        ticket.ClearDomainEvents();

        Assert.False(ticket.CanBeMarkedReady);
        DomainAssert.Throws(KitchenTicketErrors.ItemsNotReady, () => ticket.MarkReady(_clock));

        Assert.Equal(KitchenTicketStatus.InProgress, ticket.Status);
        Assert.Null(ticket.ReadyAtUtc);
        DomainAssert.NoEvents(ticket);
    }

    [Fact]
    public void MarkReady_rejects_a_started_ticket_without_any_ready_item()
    {
        var ticket = NewTicket();
        ticket.Start(_clock);

        DomainAssert.Throws(KitchenTicketErrors.ItemsNotReady, () => ticket.MarkReady(_clock));

        DomainAssert.NotRaised<KitchenTicketReady>(ticket);
    }

    [Fact]
    public void MarkReady_rejects_a_queued_ticket()
    {
        var ticket = NewTicket();

        DomainAssert.Throws(KitchenTicketErrors.NotInProgress, () => ticket.MarkReady(_clock));

        Assert.Equal(KitchenTicketStatus.Queued, ticket.Status);
        DomainAssert.NoEvents(ticket);
    }

    [Fact]
    public void MarkReady_moves_the_ticket_to_ready_once_every_item_is_ready_and_raises_KitchenTicketReady()
    {
        var ticket = NewTicket();
        PrepareItem(ticket, 0);
        PrepareItem(ticket, 1);
        ticket.ClearDomainEvents();
        _clock.Advance(TimeSpan.FromMinutes(10));

        ticket.MarkReady(_clock);

        Assert.Equal(KitchenTicketStatus.Ready, ticket.Status);
        Assert.Equal(_clock.UtcNow, ticket.ReadyAtUtc);
        Assert.False(ticket.CanBeMarkedReady);
        var ready = DomainAssert.Raised<KitchenTicketReady>(ticket);
        Assert.Equal(Tenant, ready.TenantId);
        Assert.Equal(ticket.Id, ready.TicketId);
        Assert.Equal(Order, ready.OrderId);
        Assert.Equal(Location, ready.LocationId);
        Assert.Equal(2, ready.ReadyItemCount);
        Assert.Equal(2, ready.TotalItemCount);
        Assert.Equal(_clock.UtcNow, ready.OccurredAtUtc);
    }

    [Fact]
    public void MarkReady_rejects_an_already_ready_ticket()
    {
        var ticket = ReadyTicket();

        DomainAssert.Throws(KitchenTicketErrors.NotInProgress, () => ticket.MarkReady(_clock));

        DomainAssert.NoEvents(ticket);
    }

    [Fact]
    public void MarkReady_follows_a_partial_readiness_policy()
    {
        var ticket = NewTicket(itemCount: 2, policy: TicketReadinessPolicy.AtLeast(50));
        ticket.Start(_clock);

        DomainAssert.Throws(KitchenTicketErrors.ItemsNotReady, () => ticket.MarkReady(_clock));

        PrepareItem(ticket, 0);
        ticket.MarkReady(_clock);

        Assert.Equal(KitchenTicketStatus.Ready, ticket.Status);
        var ready = DomainAssert.Raised<KitchenTicketReady>(ticket);
        Assert.Equal(1, ready.ReadyItemCount);
        Assert.Equal(2, ready.TotalItemCount);
    }

    [Fact]
    public void Remaining_items_can_still_progress_on_a_ticket_ready_under_a_partial_policy()
    {
        var ticket = NewTicket(itemCount: 2, policy: TicketReadinessPolicy.AtLeast(50));
        PrepareItem(ticket, 0);
        ticket.MarkReady(_clock);

        PrepareItem(ticket, 1);

        Assert.Equal(KitchenTicketStatus.Ready, ticket.Status);
        Assert.All(ticket.Items, i => Assert.True(i.IsReady));
    }

    // ---- Complete ----

    [Fact]
    public void Complete_moves_a_ready_ticket_to_completed_and_raises_KitchenTicketCompleted()
    {
        var ticket = ReadyTicket();
        _clock.Advance(TimeSpan.FromMinutes(1));

        ticket.Complete(_clock);

        Assert.Equal(KitchenTicketStatus.Completed, ticket.Status);
        Assert.Equal(_clock.UtcNow, ticket.CompletedAtUtc);
        Assert.True(ticket.IsClosed);
        var completed = DomainAssert.Raised<KitchenTicketCompleted>(ticket);
        Assert.Equal(ticket.Id, completed.TicketId);
        Assert.Equal(Order, completed.OrderId);
        Assert.Equal(Tenant, completed.TenantId);
        Assert.Equal(Location, completed.LocationId);
    }

    [Fact]
    public void Complete_rejects_a_queued_ticket()
    {
        var ticket = NewTicket();

        DomainAssert.Throws(KitchenTicketErrors.NotReady, () => ticket.Complete(_clock));

        Assert.Equal(KitchenTicketStatus.Queued, ticket.Status);
        DomainAssert.NoEvents(ticket);
    }

    [Fact]
    public void Complete_rejects_a_ticket_in_progress_even_when_all_items_are_ready()
    {
        var ticket = NewTicket();
        PrepareItem(ticket, 0);
        PrepareItem(ticket, 1);

        DomainAssert.Throws(KitchenTicketErrors.NotReady, () => ticket.Complete(_clock));

        Assert.Equal(KitchenTicketStatus.InProgress, ticket.Status);
        DomainAssert.NotRaised<KitchenTicketCompleted>(ticket);
    }

    [Fact]
    public void Complete_rejects_a_ticket_ready_under_a_partial_policy_until_every_item_is_ready()
    {
        var ticket = NewTicket(itemCount: 2, policy: TicketReadinessPolicy.AtLeast(50));
        PrepareItem(ticket, 0);
        ticket.MarkReady(_clock);
        ticket.ClearDomainEvents();

        Assert.False(ticket.CanBeCompleted);
        DomainAssert.Throws(KitchenTicketErrors.ItemsNotAllReady, () => ticket.Complete(_clock));

        Assert.Equal(KitchenTicketStatus.Ready, ticket.Status);
        Assert.Null(ticket.CompletedAtUtc);
        DomainAssert.NoEvents(ticket);

        ticket.MarkItemStarted(ItemId(ticket, 1), _clock);
        DomainAssert.Throws(KitchenTicketErrors.ItemsNotAllReady, () => ticket.Complete(_clock));

        ticket.MarkItemReady(ItemId(ticket, 1), _clock);
        Assert.True(ticket.CanBeCompleted);
        ticket.Complete(_clock);

        Assert.Equal(KitchenTicketStatus.Completed, ticket.Status);
        Assert.All(ticket.Items, i => Assert.True(i.IsReady));
        DomainAssert.Raised<KitchenTicketCompleted>(ticket);
    }

    [Fact]
    public void A_ticket_ready_under_a_partial_policy_can_still_be_cancelled()
    {
        var ticket = NewTicket(itemCount: 2, policy: TicketReadinessPolicy.AtLeast(50));
        PrepareItem(ticket, 0);
        ticket.MarkReady(_clock);

        ticket.Cancel(_clock, "Customer left");

        Assert.Equal(KitchenTicketStatus.Cancelled, ticket.Status);
    }

    // ---- Cancel ----

    [Theory]
    [InlineData(KitchenTicketStatus.Queued)]
    [InlineData(KitchenTicketStatus.InProgress)]
    [InlineData(KitchenTicketStatus.Ready)]
    public void Cancel_is_allowed_until_completion(KitchenTicketStatus from)
    {
        var ticket = from == KitchenTicketStatus.Ready ? ReadyTicket() : NewTicket();
        if (from == KitchenTicketStatus.InProgress)
        {
            ticket.Start(_clock);
            ticket.ClearDomainEvents();
        }

        Assert.Equal(from, ticket.Status);

        ticket.Cancel(_clock);

        Assert.Equal(KitchenTicketStatus.Cancelled, ticket.Status);
        Assert.Equal(_clock.UtcNow, ticket.CancelledAtUtc);
        Assert.True(ticket.IsClosed);
        var cancelled = DomainAssert.Raised<KitchenTicketCancelled>(ticket);
        Assert.Equal(ticket.Id, cancelled.TicketId);
        Assert.Equal(Order, cancelled.OrderId);
        Assert.Equal(Tenant, cancelled.TenantId);
        Assert.Equal(Location, cancelled.LocationId);
        Assert.Null(cancelled.Reason);
        Assert.Null(ticket.CancellationReason);
    }

    [Fact]
    public void Cancel_keeps_the_trimmed_reason_and_carries_it_in_the_event()
    {
        var ticket = NewTicket();

        ticket.Cancel(_clock, "  Customer left ");

        Assert.Equal("Customer left", ticket.CancellationReason);
        Assert.Equal("Customer left", DomainAssert.Raised<KitchenTicketCancelled>(ticket).Reason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Cancel_treats_a_blank_reason_as_no_reason(string reason)
    {
        var ticket = NewTicket();

        ticket.Cancel(_clock, reason);

        Assert.Null(ticket.CancellationReason);
        Assert.Null(DomainAssert.Raised<KitchenTicketCancelled>(ticket).Reason);
    }

    [Fact]
    public void Cancel_accepts_a_reason_at_the_limit_and_rejects_a_longer_one()
    {
        var ticket = NewTicket();

        DomainAssert.Throws(
            KitchenTicketErrors.CancellationReasonTooLong,
            () => ticket.Cancel(_clock, new string('x', KitchenTicket.MaxCancellationReasonLength + 1)));
        Assert.Equal(KitchenTicketStatus.Queued, ticket.Status);
        Assert.Null(ticket.CancelledAtUtc);
        DomainAssert.NoEvents(ticket);

        ticket.Cancel(_clock, new string('x', KitchenTicket.MaxCancellationReasonLength));
        Assert.Equal(KitchenTicket.MaxCancellationReasonLength, ticket.CancellationReason!.Length);
    }

    // ---- États terminaux ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Closed_ticket_rejects_every_mutation(bool completed)
    {
        var ticket = completed ? CompletedTicket() : CancelledTicket();
        var status = ticket.Status;
        var itemId = ItemId(ticket, 0);

        DomainAssert.Throws(KitchenTicketErrors.Closed, () => ticket.Start(_clock));
        DomainAssert.Throws(KitchenTicketErrors.Closed, () => ticket.MarkItemStarted(itemId, _clock));
        DomainAssert.Throws(KitchenTicketErrors.Closed, () => ticket.MarkItemReady(itemId, _clock));
        DomainAssert.Throws(KitchenTicketErrors.Closed, () => ticket.MarkReady(_clock));
        DomainAssert.Throws(KitchenTicketErrors.Closed, () => ticket.Complete(_clock));
        DomainAssert.Throws(KitchenTicketErrors.Closed, () => ticket.Cancel(_clock));
        DomainAssert.Throws(KitchenTicketErrors.Closed, () => ticket.Cancel(_clock, "Too late"));
        DomainAssert.Throws(KitchenTicketErrors.Closed, () => ticket.ChangePriority(KitchenPriority.Rush, _clock));

        Assert.Equal(status, ticket.Status);
        Assert.Equal(KitchenPriority.Normal, ticket.Priority);
        DomainAssert.NoEvents(ticket);
    }

    // ---- Priorité ----

    [Fact]
    public void ChangePriority_updates_the_priority_and_raises_KitchenTicketPriorityChanged()
    {
        var ticket = NewTicket();

        ticket.ChangePriority(KitchenPriority.Rush, _clock);

        Assert.Equal(KitchenPriority.Rush, ticket.Priority);
        var changed = DomainAssert.Raised<KitchenTicketPriorityChanged>(ticket);
        Assert.Equal(ticket.Id, changed.TicketId);
        Assert.Equal(Tenant, changed.TenantId);
        Assert.Equal(Location, changed.LocationId);
        Assert.Equal(KitchenPriority.Normal, changed.PreviousPriority);
        Assert.Equal(KitchenPriority.Rush, changed.NewPriority);
    }

    [Fact]
    public void ChangePriority_is_allowed_on_in_progress_and_ready_tickets()
    {
        var ready = ReadyTicket();
        var inProgress = NewTicket();
        inProgress.Start(_clock);

        ready.ChangePriority(KitchenPriority.High, _clock);
        inProgress.ChangePriority(KitchenPriority.High, _clock);

        Assert.Equal(KitchenPriority.High, ready.Priority);
        Assert.Equal(KitchenPriority.High, inProgress.Priority);
    }

    [Fact]
    public void ChangePriority_to_the_same_priority_raises_nothing()
    {
        var ticket = NewTicket();

        ticket.ChangePriority(KitchenPriority.Normal, _clock);

        DomainAssert.NoEvents(ticket);
    }

    [Fact]
    public void ChangePriority_rejects_an_unknown_priority()
    {
        var ticket = NewTicket();

        DomainAssert.Throws(KitchenTicketErrors.PriorityUnknown, () => ticket.ChangePriority((KitchenPriority)42, _clock));

        Assert.Equal(KitchenPriority.Normal, ticket.Priority);
        DomainAssert.NoEvents(ticket);
    }

    // ---- Événements ----

    [Fact]
    public void Every_ticket_event_carries_the_location()
    {
        var ticket = KitchenTicket.CreateFromOrder(Snapshot(Grill), KitchenPriority.Normal, _clock);
        ticket.ChangePriority(KitchenPriority.High, _clock);
        PrepareItem(ticket, 0);
        ticket.MarkReady(_clock);
        ticket.Complete(_clock);
        var cancelled = NewTicket();
        cancelled.Cancel(_clock);

        var events = ticket.DomainEvents.Concat(cancelled.DomainEvents).ToList();

        Assert.Equal(8, events.Select(e => e.GetType()).Distinct().Count());
        Assert.All(events, e => Assert.Equal(Location, (LocationId)e.GetType().GetProperty("LocationId")!.GetValue(e)!));
    }

    [Fact]
    public void State_timestamps_equal_the_event_timestamps()
    {
        var clock = new TickingClock();
        var ticket = KitchenTicket.CreateFromOrder(Snapshot(Grill), KitchenPriority.Normal, clock);
        ticket.MarkItemStarted(ItemId(ticket, 0), clock);
        ticket.MarkItemReady(ItemId(ticket, 0), clock);
        ticket.MarkReady(clock);
        ticket.Complete(clock);
        var cancelled = KitchenTicket.CreateFromOrder(Snapshot(Grill), KitchenPriority.Normal, clock);
        cancelled.Cancel(clock);

        Assert.Equal(ticket.CreatedAtUtc, DomainAssert.Raised<KitchenTicketCreated>(ticket).OccurredAtUtc);
        Assert.Equal(ticket.StartedAtUtc, DomainAssert.Raised<KitchenPreparationStarted>(ticket).OccurredAtUtc);
        Assert.Equal(ticket.ReadyAtUtc, DomainAssert.Raised<KitchenTicketReady>(ticket).OccurredAtUtc);
        Assert.Equal(ticket.CompletedAtUtc, DomainAssert.Raised<KitchenTicketCompleted>(ticket).OccurredAtUtc);
        Assert.Equal(cancelled.CancelledAtUtc, DomainAssert.Raised<KitchenTicketCancelled>(cancelled).OccurredAtUtc);
    }

    /// <summary>Avance à chaque lecture : prouve qu'une opération ne lit l'horloge qu'une fois.</summary>
    private sealed class TickingClock : IClock
    {
        private DateTimeOffset _now = FakeClock.DefaultNow;

        public DateTimeOffset UtcNow => _now = _now.AddSeconds(1);
    }

    [Fact]
    public void At_table_only_the_first_course_and_direct_dishes_start_and_the_others_are_fired_course_by_course()
    {
        var snapshot = KitchenOrderSnapshot.Create(Order, Tenant, Location, new[]
        {
            KitchenOrderLine.Create(OrderItemId.New(), "Jus", 1, Cold, null, [], null),
            KitchenOrderLine.Create(OrderItemId.New(), "Salade", 1, Cold, null, [], null, KitchenCourse.Starter),
            KitchenOrderLine.Create(OrderItemId.New(), "Poulet", 1, Grill, null, [], null, KitchenCourse.Main),
            KitchenOrderLine.Create(OrderItemId.New(), "Glace", 1, Cold, null, [], null, KitchenCourse.Dessert),
        });

        var ticket = KitchenTicket.CreateFromOrder(snapshot, KitchenPriority.Normal, _clock, holdLaterCourses: true);
        var counter = KitchenTicket.CreateFromOrder(snapshot, KitchenPriority.Normal, _clock);

        Assert.Equal([false, false, true, true], ticket.Items.Select(item => item.IsHeld).ToArray());
        Assert.Equal([KitchenCourse.Main, KitchenCourse.Dessert], ticket.HeldCourses);
        Assert.Empty(counter.HeldCourses);
        DomainAssert.Throws(KitchenTicketErrors.ItemHeld, () => ticket.MarkItemStarted(ItemId(ticket, 2), _clock));
        DomainAssert.Throws(KitchenTicketErrors.NothingToFire, () => ticket.FireCourse(KitchenCourse.Starter, _clock));

        ticket.FireCourse(KitchenCourse.Main, _clock);
        ticket.MarkItemStarted(ItemId(ticket, 2), _clock);

        var fired = DomainAssert.Raised<KitchenCourseFired>(ticket);
        Assert.Equal((KitchenCourse.Main, 1), (fired.Course, fired.ItemCount));
        Assert.Equal([KitchenCourse.Dessert], ticket.HeldCourses);
        Assert.False(ticket.CanBeMarkedReady);
    }
}
