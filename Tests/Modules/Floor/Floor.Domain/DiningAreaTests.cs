namespace Floor.Domain;

public sealed class DiningAreaTests
{
    private readonly FakeClock _clock = new();
    private readonly TenantId _tenantId = TenantId.New();
    private readonly LocationId _locationId = LocationId.New();

    private LocationReference Location(bool isOperational = true) => new(_tenantId, _locationId, isOperational);

    private DiningArea Area(string name = "Terrasse")
    {
        var area = DiningArea.Create(_tenantId, Location(), name, _clock);
        area.ClearDomainEvents();
        return area;
    }

    private (DiningArea Area, TableId TableId) AreaWithTable(string label = "T1", int capacity = 4)
    {
        var area = Area();
        var tableId = area.AddTable(label, capacity, _clock);
        area.ClearDomainEvents();
        return (area, tableId);
    }

    [Fact]
    public void Create_builds_an_empty_area_and_raises_DiningAreaCreated()
    {
        var area = DiningArea.Create(_tenantId, Location(), "  Terrasse ", _clock);

        Assert.Equal(_tenantId, area.TenantId);
        Assert.Equal(_locationId, area.LocationId);
        Assert.Equal("Terrasse", area.Name);
        Assert.Empty(area.Tables);
        var raised = DomainAssert.Raised<DiningAreaCreated>(area);
        Assert.Equal(new DiningAreaCreated(_tenantId, area.Id, _locationId, "Terrasse", _clock.UtcNow), raised);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Create_rejects_a_missing_name(string? name)
    {
        DomainAssert.Throws(DiningAreaErrors.NameRequired, () => DiningArea.Create(_tenantId, Location(), name, _clock));
    }

    [Fact]
    public void Create_enforces_the_name_length_limit()
    {
        var atLimit = DiningArea.Create(_tenantId, Location(), new string('a', DiningArea.NameMaxLength), _clock);

        Assert.Equal(DiningArea.NameMaxLength, atLimit.Name.Length);
        DomainAssert.Throws(
            DiningAreaErrors.NameTooLong,
            () => DiningArea.Create(_tenantId, Location(), new string('a', DiningArea.NameMaxLength + 1), _clock));
    }

    [Fact]
    public void Rename_changes_the_name_and_raises_DiningAreaRenamed()
    {
        var area = Area("Terrasse");

        area.Rename(" Patio ", _clock);

        Assert.Equal("Patio", area.Name);
        var raised = DomainAssert.Raised<DiningAreaRenamed>(area);
        Assert.Equal(new DiningAreaRenamed(_tenantId, area.Id, "Terrasse", "Patio", _clock.UtcNow), raised);
    }

    [Fact]
    public void Rename_rejects_an_empty_or_too_long_name()
    {
        var area = Area();

        DomainAssert.Throws(DiningAreaErrors.NameRequired, () => area.Rename(" ", _clock));
        DomainAssert.Throws(DiningAreaErrors.NameTooLong, () => area.Rename(new string('a', DiningArea.NameMaxLength + 1), _clock));
        Assert.Equal("Terrasse", area.Name);
        DomainAssert.NoEvents(area);
    }

    [Fact]
    public void Rename_to_the_same_name_changes_nothing()
    {
        var area = Area("Terrasse");

        area.Rename("Terrasse", _clock);

        DomainAssert.NoEvents(area);
    }

    [Fact]
    public void AddTable_adds_an_available_table_and_raises_TableAdded()
    {
        var area = Area();

        var tableId = area.AddTable("  T1 ", 4, _clock);

        var table = Assert.Single(area.Tables);
        Assert.Equal(tableId, table.Id);
        Assert.Equal("T1", table.Label.Value);
        Assert.Equal(4, table.Capacity);
        Assert.Equal(TableStatus.Available, table.Status);
        Assert.True(table.IsInService);
        var raised = DomainAssert.Raised<TableAdded>(area);
        Assert.Equal(new TableAdded(_tenantId, area.Id, _locationId, tableId, "T1", 4, _clock.UtcNow), raised);
    }

    [Theory]
    [InlineData("T1")]
    [InlineData("t1")]
    [InlineData("  T1  ")]
    [InlineData(" t1")]
    public void AddTable_rejects_a_duplicate_label_ignoring_case_and_spaces(string duplicate)
    {
        var (area, _) = AreaWithTable("T1");

        DomainAssert.Throws(DiningAreaErrors.TableLabelAlreadyUsed, () => area.AddTable(duplicate, 2, _clock));

        Assert.Single(area.Tables);
        DomainAssert.NoEvents(area);
    }

    [Fact]
    public void AddTable_accepts_the_same_label_in_another_area()
    {
        var (first, _) = AreaWithTable("T1");
        var second = Area("Salle");

        second.AddTable("T1", 2, _clock);

        Assert.Single(first.Tables);
        Assert.Single(second.Tables);
    }

    [Fact]
    public void AddTable_accepts_a_label_freed_by_a_removed_table()
    {
        var (area, tableId) = AreaWithTable("T1");
        area.RemoveTable(tableId, hasOpenSession: false, _clock);

        var newId = area.AddTable("T1", 6, _clock);

        Assert.Equal(newId, Assert.Single(area.Tables).Id);
        Assert.NotEqual(tableId, newId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddTable_rejects_a_missing_label(string? label)
    {
        var area = Area();

        DomainAssert.Throws(DiningAreaErrors.TableLabelRequired, () => area.AddTable(label, 4, _clock));
        Assert.Empty(area.Tables);
    }

    [Fact]
    public void AddTable_enforces_the_label_length_limit()
    {
        var area = Area();

        area.AddTable(new string('x', TableLabel.MaxLength), 4, _clock);

        DomainAssert.Throws(
            DiningAreaErrors.TableLabelTooLong,
            () => area.AddTable(new string('y', TableLabel.MaxLength + 1), 4, _clock));
        Assert.Single(area.Tables);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void AddTable_rejects_a_capacity_that_is_not_positive(int capacity)
    {
        var area = Area();

        DomainAssert.Throws(DiningAreaErrors.TableCapacityNotPositive, () => area.AddTable("T1", capacity, _clock));

        Assert.Empty(area.Tables);
        DomainAssert.NoEvents(area);
    }

    [Fact]
    public void AddTable_accepts_a_capacity_of_one()
    {
        var area = Area();

        area.AddTable("Bar 1", 1, _clock);

        Assert.Equal(1, Assert.Single(area.Tables).Capacity);
    }

    [Fact]
    public void RemoveTable_removes_the_table_and_raises_TableRemoved()
    {
        var (area, tableId) = AreaWithTable();

        area.RemoveTable(tableId, hasOpenSession: false, _clock);

        Assert.Empty(area.Tables);
        var raised = DomainAssert.Raised<TableRemoved>(area);
        Assert.Equal(new TableRemoved(_tenantId, area.Id, _locationId, tableId, _clock.UtcNow), raised);
    }

    [Fact]
    public void RemoveTable_rejects_a_table_with_an_open_session()
    {
        var (area, tableId) = AreaWithTable();

        DomainAssert.Throws(DiningAreaErrors.TableHasOpenSession, () => area.RemoveTable(tableId, hasOpenSession: true, _clock));

        Assert.Single(area.Tables);
        DomainAssert.NoEvents(area);
    }

    [Fact]
    public void Table_behaviours_reject_a_table_that_is_not_in_the_area()
    {
        var (area, _) = AreaWithTable();
        var unknown = TableId.New();

        DomainAssert.Throws(DiningAreaErrors.TableNotFound, () => area.RemoveTable(unknown, hasOpenSession: false, _clock));
        DomainAssert.Throws(DiningAreaErrors.TableNotFound, () => area.ChangeTableCapacity(unknown, 2, _clock));
        DomainAssert.Throws(DiningAreaErrors.TableNotFound, () => area.SetTableOutOfService(unknown, _clock));
        DomainAssert.Throws(DiningAreaErrors.TableNotFound, () => area.RestoreTable(unknown, _clock));
        DomainAssert.Throws(DiningAreaErrors.TableNotFound, () => area.GetTableReference(unknown));
        DomainAssert.NoEvents(area);
    }

    [Fact]
    public void ChangeTableCapacity_updates_the_seats_and_raises_TableCapacityChanged()
    {
        var (area, tableId) = AreaWithTable(capacity: 4);

        area.ChangeTableCapacity(tableId, 6, _clock);

        Assert.Equal(6, area.Tables.Single().Capacity);
        var raised = DomainAssert.Raised<TableCapacityChanged>(area);
        Assert.Equal(new TableCapacityChanged(_tenantId, area.Id, _locationId, tableId, 4, 6, _clock.UtcNow), raised);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void ChangeTableCapacity_rejects_a_capacity_that_is_not_positive(int capacity)
    {
        var (area, tableId) = AreaWithTable(capacity: 4);

        DomainAssert.Throws(DiningAreaErrors.TableCapacityNotPositive, () => area.ChangeTableCapacity(tableId, capacity, _clock));

        Assert.Equal(4, area.Tables.Single().Capacity);
        DomainAssert.NoEvents(area);
    }

    [Fact]
    public void ChangeTableCapacity_to_the_same_value_raises_nothing()
    {
        var (area, tableId) = AreaWithTable(capacity: 4);

        area.ChangeTableCapacity(tableId, 4, _clock);

        DomainAssert.NoEvents(area);
    }

    [Fact]
    public void SetTableOutOfService_marks_the_table_and_raises_TableSetOutOfService()
    {
        var (area, tableId) = AreaWithTable();

        area.SetTableOutOfService(tableId, _clock);

        var table = area.Tables.Single();
        Assert.Equal(TableStatus.OutOfService, table.Status);
        Assert.False(table.IsInService);
        var raised = DomainAssert.Raised<TableSetOutOfService>(area);
        Assert.Equal(new TableSetOutOfService(_tenantId, area.Id, _locationId, tableId, _clock.UtcNow), raised);
    }

    [Fact]
    public void SetTableOutOfService_rejects_a_table_already_out_of_service()
    {
        var (area, tableId) = AreaWithTable();
        area.SetTableOutOfService(tableId, _clock);
        area.ClearDomainEvents();

        DomainAssert.Throws(DiningAreaErrors.TableAlreadyOutOfService, () => area.SetTableOutOfService(tableId, _clock));
        DomainAssert.NoEvents(area);
    }

    [Fact]
    public void RestoreTable_makes_the_table_available_again_and_raises_TableRestored()
    {
        var (area, tableId) = AreaWithTable();
        area.SetTableOutOfService(tableId, _clock);
        area.ClearDomainEvents();

        area.RestoreTable(tableId, _clock);

        Assert.Equal(TableStatus.Available, area.Tables.Single().Status);
        var raised = DomainAssert.Raised<TableRestored>(area);
        Assert.Equal(new TableRestored(_tenantId, area.Id, _locationId, tableId, _clock.UtcNow), raised);
    }

    [Fact]
    public void RestoreTable_rejects_a_table_that_is_not_out_of_service()
    {
        var (area, tableId) = AreaWithTable();

        DomainAssert.Throws(DiningAreaErrors.TableNotOutOfService, () => area.RestoreTable(tableId, _clock));
        DomainAssert.NoEvents(area);
    }

    [Fact]
    public void An_out_of_service_table_can_still_have_its_capacity_changed()
    {
        var (area, tableId) = AreaWithTable(capacity: 4);
        area.SetTableOutOfService(tableId, _clock);

        area.ChangeTableCapacity(tableId, 2, _clock);

        Assert.Equal(2, area.Tables.Single().Capacity);
    }

    [Fact]
    public void SeatsInService_counts_only_available_tables()
    {
        var area = Area();
        area.AddTable("T1", 4, _clock);
        var broken = area.AddTable("T2", 6, _clock);
        area.AddTable("T3", 2, _clock);

        area.SetTableOutOfService(broken, _clock);

        Assert.Equal(6, area.SeatsInService);
    }

    [Fact]
    public void GetTableReference_reflects_tenant_location_id_and_service_status()
    {
        var (area, tableId) = AreaWithTable();

        var reference = area.GetTableReference(tableId);
        Assert.Equal(_tenantId, reference.TenantId);
        Assert.Equal(_locationId, reference.LocationId);
        Assert.Equal(tableId, reference.TableId);
        Assert.True(reference.IsInService);
        Assert.Equal(reference, area.GetTableReference(tableId));

        area.SetTableOutOfService(tableId, _clock);

        Assert.False(area.GetTableReference(tableId).IsInService);
        Assert.NotEqual(reference, area.GetTableReference(tableId));
    }

    [Fact]
    public void TableLabel_ignores_case_when_compared()
    {
        Assert.Equal(TableLabel.Create("Terrasse 3"), TableLabel.Create(" terrasse 3 "));
        Assert.NotEqual(TableLabel.Create("T1"), TableLabel.Create("T2"));
        Assert.Equal("T1", TableLabel.Create(" T1 ").ToString());
    }

    [Fact]
    public void Create_rejects_an_empty_tenant_and_an_empty_location_id()
    {
        DomainAssert.Throws(CommonErrors.EmptyId, () => DiningArea.Create(default, Location(), "Terrasse", _clock));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new LocationReference(_tenantId, default, IsOperational: true));
        DomainAssert.Throws(CommonErrors.EmptyId, () => new LocationReference(default, _locationId, IsOperational: true));
    }

    [Fact]
    public void Create_rejects_a_location_of_another_tenant()
    {
        var foreign = new LocationReference(TenantId.New(), _locationId, IsOperational: true);

        DomainAssert.Throws(CommonErrors.TenantMismatch, () => DiningArea.Create(_tenantId, foreign, "Terrasse", _clock));
    }

    [Fact]
    public void Create_accepts_a_location_that_is_not_operational_yet()
    {
        var area = DiningArea.Create(_tenantId, Location(isOperational: false), "Terrasse", _clock);

        Assert.Equal(_locationId, area.LocationId);
    }

    [Theory]
    [InlineData("Terrasse  3")]
    [InlineData("Terrasse\t3")]
    [InlineData("Terrasse\u00A03")]
    [InlineData("Terras\u200Bse 3")]
    [InlineData("\uFEFFTerrasse 3\u200D")]
    [InlineData("Terrasse\u0000 3")]
    [InlineData("ｔｅｒｒａｓｓｅ 3")]
    public void AddTable_rejects_a_label_that_reads_the_same_as_an_existing_one(string lookalike)
    {
        var (area, _) = AreaWithTable("Terrasse 3");

        DomainAssert.Throws(DiningAreaErrors.TableLabelAlreadyUsed, () => area.AddTable(lookalike, 2, _clock));

        Assert.Single(area.Tables);
    }

    [Fact]
    public void AddTable_rejects_the_decomposed_form_of_an_existing_label()
    {
        const string composed = "Caf\u00E9";
        const string decomposed = "Cafe\u0301";
        var (area, _) = AreaWithTable(composed);

        DomainAssert.Throws(DiningAreaErrors.TableLabelAlreadyUsed, () => area.AddTable(decomposed, 2, _clock));
        Assert.Equal(composed, TableLabel.Create(decomposed).Value);
    }

    [Theory]
    [InlineData("\u200B")]
    [InlineData("\u200B\u200D\uFEFF")]
    [InlineData(" \u0000\u00AD ")]
    public void AddTable_rejects_a_label_made_of_invisible_characters_only(string invisible)
    {
        var area = Area();

        DomainAssert.Throws(DiningAreaErrors.TableLabelRequired, () => area.AddTable(invisible, 2, _clock));
    }

    [Fact]
    public void TableLabel_is_stored_in_its_canonical_form_and_measured_after_normalisation()
    {
        Assert.Equal("Terrasse 3", TableLabel.Create("  Terrasse \t\u200B  3 ").Value);

        var padded = new string('a', TableLabel.MaxLength) + "\u200B\u200B";
        Assert.Equal(TableLabel.MaxLength, TableLabel.Create(padded).Value.Length);
        Assert.Equal("?", TableLabel.Create("\uD800").Value.Replace('\uFFFD', '?'));
    }

    [Theory]
    [InlineData("Salle   du \t fond", "Salle du fond")]
    [InlineData("Salle\u200B du fond", "Salle du fond")]
    [InlineData("Cafe\u0301", "Caf\u00E9")]
    public void Area_name_is_stored_in_its_canonical_form(string typed, string expected)
    {
        var area = Area(typed);

        Assert.Equal(expected, area.Name);
        area.Rename(expected, _clock);
        DomainAssert.NoEvents(area);
    }

    [Theory]
    [InlineData("\u200B")]
    [InlineData("\u0000 \uFEFF")]
    public void Area_name_made_of_invisible_characters_only_is_rejected(string invisible)
    {
        DomainAssert.Throws(DiningAreaErrors.NameRequired, () => DiningArea.Create(_tenantId, Location(), invisible, _clock));
        DomainAssert.Throws(DiningAreaErrors.NameRequired, () => Area().Rename(invisible, _clock));
    }

    [Fact]
    public void DiningAreaId_rejects_an_empty_guid()
    {
        DomainAssert.Throws(CommonErrors.EmptyId, () => DiningAreaId.From(Guid.Empty));
    }

    [Fact]
    public void A_table_is_renamed_to_a_label_free_in_its_area()
    {
        var (area, tableId) = AreaWithTable("T1");
        var other = area.AddTable("T2", 2, _clock);
        area.ClearDomainEvents();

        area.RenameTable(tableId, " Terrasse 1 ", _clock);
        area.RenameTable(tableId, "Terrasse 1", _clock);

        Assert.Equal("Terrasse 1", area.Tables.Single(table => table.Id == tableId).Label.Value);
        var renamed = DomainAssert.Raised<TableRenamed>(area);
        Assert.Equal(("T1", "Terrasse 1"), (renamed.PreviousLabel, renamed.Label));
        DomainAssert.Throws(DiningAreaErrors.TableLabelAlreadyUsed, () => area.RenameTable(other, "terrasse 1", _clock));
    }

    [Fact]
    public void A_table_is_placed_on_the_drawn_plan_inside_the_grid()
    {
        var (area, tableId) = AreaWithTable();

        area.PlaceTable(tableId, TableLayout.Create(10, 20, 12, 8, TableShape.Rectangle), _clock);
        area.PlaceTable(tableId, TableLayout.Create(10, 20, 12, 8, TableShape.Rectangle), _clock);

        var placed = DomainAssert.Raised<TablePlaced>(area);
        Assert.Equal((10, 20, 12, 8, TableShape.Rectangle), (placed.X, placed.Y, placed.Width, placed.Height, placed.Shape));
        Assert.Equal(TableShape.Rectangle, area.Tables.Single().Layout!.Shape);
        DomainAssert.Throws(DiningAreaErrors.LayoutInvalid, () => TableLayout.Create(95, 0, 10, 10, TableShape.Square));
        DomainAssert.Throws(DiningAreaErrors.LayoutInvalid, () => TableLayout.Create(0, 0, 10, 8, TableShape.Round));
        DomainAssert.Throws(DiningAreaErrors.LayoutInvalid, () => TableLayout.Create(0, 0, 2, 2, TableShape.Square));
        DomainAssert.Throws(DiningAreaErrors.LayoutInvalid, () => TableLayout.Create(0, 0, 8, 8, (TableShape)9));
    }

    [Fact]
    public void A_table_is_rotated_by_steps_of_fifteen_degrees()
    {
        var (area, tableId) = AreaWithTable();

        area.PlaceTable(tableId, TableLayout.Create(10, 20, 16, 8, TableShape.Rectangle), _clock);
        area.PlaceTable(tableId, TableLayout.Create(10, 20, 16, 8, TableShape.Rectangle, 45), _clock);

        Assert.Equal(45, area.Tables.Single().Layout!.Rotation);
        Assert.Equal(45, area.DomainEvents.OfType<TablePlaced>().Last().Rotation);
        Assert.Equal(0, TableLayout.Create(0, 0, 8, 8, TableShape.Round, 90).Rotation);
        DomainAssert.Throws(DiningAreaErrors.LayoutInvalid, () => TableLayout.Create(0, 0, 8, 8, TableShape.Square, 10));
        DomainAssert.Throws(DiningAreaErrors.LayoutInvalid, () => TableLayout.Create(0, 0, 8, 8, TableShape.Square, 360));
        DomainAssert.Throws(DiningAreaErrors.LayoutInvalid, () => TableLayout.Create(0, 0, 8, 8, TableShape.Square, -15));
    }

    [Fact]
    public void An_area_is_deleted_with_its_tables_unless_one_is_occupied()
    {
        var (area, tableId) = AreaWithTable();

        DomainAssert.Throws(DiningAreaErrors.AreaHasOpenSession, () => area.Delete([tableId], _clock));
        area.Delete([], _clock);

        Assert.Equal([tableId], DomainAssert.Raised<DiningAreaDeleted>(area).TableIds);
    }
}
