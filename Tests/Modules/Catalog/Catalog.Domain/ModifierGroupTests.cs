namespace Catalog.Domain;

public class ModifierGroupTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private readonly FakeClock _clock = new();

    private static Money Eur(decimal amount) => Money.Of(amount, Currency.EUR);

    private static CatalogName Name(string value) => CatalogName.Create(value);

    private static ModifierGroup NewGroup(int min = 0, int max = 2) =>
        ModifierGroup.Create(Tenant, Name("Extras"), Currency.EUR, SelectionRule.Create(min, max));

    [Fact]
    public void Create_builds_an_empty_group_without_event()
    {
        var group = NewGroup(1, 3);

        Assert.Equal(Tenant, group.TenantId);
        Assert.Equal("Extras", group.Name.Value);
        Assert.Equal(Currency.EUR, group.Currency);
        Assert.Equal(1, group.MinSelections);
        Assert.Equal(3, group.MaxSelections);
        Assert.Empty(group.Modifiers);
        DomainAssert.NoEvents(group);
    }

    [Fact]
    public void Create_rejects_an_empty_tenant() =>
        DomainAssert.Throws(
            CommonErrors.EmptyId,
            () => ModifierGroup.Create(default, Name("Extras"), Currency.EUR, SelectionRule.OptionalSingle));

    [Fact]
    public void Rename_changes_the_name()
    {
        var group = NewGroup();

        group.Rename(Name("Supplements"));

        Assert.Equal("Supplements", group.Name.Value);
    }

    [Fact]
    public void ChangeSelectionRule_replaces_the_rule_whatever_the_number_of_modifiers()
    {
        var group = NewGroup();

        group.ChangeSelectionRule(SelectionRule.Create(2, 4));

        Assert.Equal(2, group.MinSelections);
        Assert.Equal(4, group.MaxSelections);
        Assert.False(group.IsSatisfiable);
    }

    [Fact]
    public void AddModifier_adds_an_available_modifier_and_raises_ModifierAdded()
    {
        var group = NewGroup();

        var modifierId = group.AddModifier(Name("Cheese"), Eur(1.5m), _clock);

        var modifier = Assert.Single(group.Modifiers);
        Assert.Equal(modifierId, modifier.Id);
        Assert.Equal("Cheese", modifier.Name.Value);
        Assert.Equal(Eur(1.5m), modifier.PriceDelta);
        Assert.True(modifier.IsAvailable);

        var added = DomainAssert.Raised<ModifierAdded>(group);
        Assert.Equal(Tenant, added.TenantId);
        Assert.Equal(group.Id, added.ModifierGroupId);
        Assert.Equal(modifierId, added.ModifierId);
        Assert.Equal("Cheese", added.Name);
        Assert.Equal(Eur(1.5m), added.PriceDelta);
        Assert.Equal(_clock.UtcNow, added.OccurredAtUtc);
    }

    [Fact]
    public void AddModifier_accepts_a_free_modifier()
    {
        var group = NewGroup();

        group.AddModifier(Name("No onion"), Eur(0m), _clock);

        Assert.True(group.Modifiers.Single().PriceDelta.IsZero);
    }

    [Fact]
    public void AddModifier_rejects_a_duplicate_name_ignoring_case()
    {
        var group = NewGroup();
        group.AddModifier(Name("Cheese"), Eur(1m), _clock);
        group.ClearDomainEvents();

        DomainAssert.Throws(ModifierGroupErrors.ModifierNameDuplicate, () => group.AddModifier(Name("cheese"), Eur(2m), _clock));

        Assert.Single(group.Modifiers);
        DomainAssert.NoEvents(group);
    }

    [Fact]
    public void AddModifier_rejects_a_negative_price()
    {
        var group = NewGroup();

        DomainAssert.Throws(ModifierGroupErrors.ModifierPriceNegative, () => group.AddModifier(Name("Cheese"), Eur(-0.01m), _clock));

        Assert.Empty(group.Modifiers);
        DomainAssert.NoEvents(group);
    }

    [Fact]
    public void AddModifier_rejects_a_price_in_another_currency()
    {
        var group = NewGroup();

        DomainAssert.Throws(
            ModifierGroupErrors.ModifierCurrencyMismatch,
            () => group.AddModifier(Name("Cheese"), Money.Of(1m, Currency.USD), _clock));

        Assert.Empty(group.Modifiers);
    }

    [Fact]
    public void RemoveModifier_removes_it_and_raises_ModifierRemoved()
    {
        var group = NewGroup();
        var modifierId = group.AddModifier(Name("Cheese"), Eur(1m), _clock);
        group.ClearDomainEvents();

        group.RemoveModifier(modifierId, _clock);

        Assert.Empty(group.Modifiers);
        var removed = DomainAssert.Raised<ModifierRemoved>(group);
        Assert.Equal(group.Id, removed.ModifierGroupId);
        Assert.Equal(modifierId, removed.ModifierId);
        Assert.Equal(Tenant, removed.TenantId);
    }

    [Fact]
    public void RemoveModifier_rejects_an_unknown_modifier()
    {
        var group = NewGroup();

        DomainAssert.Throws(ModifierGroupErrors.ModifierNotFound, () => group.RemoveModifier(ModifierId.New(), _clock));

        DomainAssert.NoEvents(group);
    }

    [Fact]
    public void RenameModifier_keeps_names_unique_but_allows_recasing_itself()
    {
        var group = NewGroup();
        var cheese = group.AddModifier(Name("Cheese"), Eur(1m), _clock);
        group.AddModifier(Name("Bacon"), Eur(2m), _clock);

        DomainAssert.Throws(ModifierGroupErrors.ModifierNameDuplicate, () => group.RenameModifier(cheese, Name("BACON")));
        DomainAssert.Throws(ModifierGroupErrors.ModifierNotFound, () => group.RenameModifier(ModifierId.New(), Name("Egg")));

        group.RenameModifier(cheese, Name("CHEESE"));
        Assert.Contains(group.Modifiers, m => m.Name.Value == "CHEESE");
    }

    [Fact]
    public void ChangeModifierPrice_updates_the_price()
    {
        var group = NewGroup();
        var modifierId = group.AddModifier(Name("Cheese"), Eur(1m), _clock);

        group.ChangeModifierPrice(modifierId, Eur(0m), _clock);

        Assert.Equal(Eur(0m), group.Modifiers.Single().PriceDelta);
    }

    [Fact]
    public void ChangeModifierPrice_rejects_negative_other_currency_and_unknown_modifier()
    {
        var group = NewGroup();
        var modifierId = group.AddModifier(Name("Cheese"), Eur(1m), _clock);

        DomainAssert.Throws(ModifierGroupErrors.ModifierPriceNegative, () => group.ChangeModifierPrice(modifierId, Eur(-1m), _clock));
        DomainAssert.Throws(
            ModifierGroupErrors.ModifierCurrencyMismatch,
            () => group.ChangeModifierPrice(modifierId, Money.Of(1m, Currency.XOF), _clock));
        DomainAssert.Throws(ModifierGroupErrors.ModifierNotFound, () => group.ChangeModifierPrice(ModifierId.New(), Eur(1m), _clock));

        Assert.Equal(Eur(1m), group.Modifiers.Single().PriceDelta);
    }

    [Fact]
    public void SetModifierAvailability_toggles_and_rejects_an_unknown_modifier()
    {
        var group = NewGroup();
        var modifierId = group.AddModifier(Name("Cheese"), Eur(1m), _clock);

        group.SetModifierAvailability(modifierId, false, _clock);
        Assert.False(group.Modifiers.Single().IsAvailable);

        group.SetModifierAvailability(modifierId, true, _clock);
        Assert.True(group.Modifiers.Single().IsAvailable);

        DomainAssert.Throws(ModifierGroupErrors.ModifierNotFound, () => group.SetModifierAvailability(ModifierId.New(), true, _clock));
    }

    [Fact]
    public void ChangeModifierPrice_raises_ModifierPriceChanged_only_on_a_real_change()
    {
        var group = NewGroup();
        var modifierId = group.AddModifier(Name("Cheese"), Eur(1m), _clock);
        group.ClearDomainEvents();
        _clock.Advance(TimeSpan.FromHours(1));

        group.ChangeModifierPrice(modifierId, Eur(1m), _clock);
        DomainAssert.NoEvents(group);

        group.ChangeModifierPrice(modifierId, Eur(1.5m), _clock);

        var changed = DomainAssert.Raised<ModifierPriceChanged>(group);
        Assert.Equal(Tenant, changed.TenantId);
        Assert.Equal(group.Id, changed.ModifierGroupId);
        Assert.Equal(modifierId, changed.ModifierId);
        Assert.Equal(Eur(1m), changed.PreviousPriceDelta);
        Assert.Equal(Eur(1.5m), changed.NewPriceDelta);
        Assert.Equal(_clock.UtcNow, changed.OccurredAtUtc);
    }

    [Fact]
    public void SetModifierAvailability_raises_ModifierAvailabilityChanged_only_on_a_real_change()
    {
        var group = NewGroup();
        var modifierId = group.AddModifier(Name("Cheese"), Eur(1m), _clock);
        group.ClearDomainEvents();

        group.SetModifierAvailability(modifierId, true, _clock);
        DomainAssert.NoEvents(group);

        group.SetModifierAvailability(modifierId, false, _clock);
        group.SetModifierAvailability(modifierId, false, _clock);

        var changed = DomainAssert.Raised<ModifierAvailabilityChanged>(group);
        Assert.Equal(Tenant, changed.TenantId);
        Assert.Equal(group.Id, changed.ModifierGroupId);
        Assert.Equal(modifierId, changed.ModifierId);
        Assert.False(changed.IsAvailable);
        Assert.Equal(_clock.UtcNow, changed.OccurredAtUtc);
    }

    [Fact]
    public void AddModifier_rejects_more_modifiers_than_the_limit()
    {
        var group = NewGroup();
        for (var i = 0; i < ModifierGroup.MaxModifiers; i++)
        {
            group.AddModifier(Name($"Option {i}"), Eur(0m), _clock);
        }

        DomainAssert.Throws(ModifierGroupErrors.TooManyModifiers, () => group.AddModifier(Name("One too many"), Eur(0m), _clock));
        Assert.Equal(ModifierGroup.MaxModifiers, group.Modifiers.Count);
    }

    [Fact]
    public void EnsureSatisfiable_compares_min_with_the_available_modifiers()
    {
        var group = NewGroup(min: 2, max: 2);
        var cheese = group.AddModifier(Name("Cheese"), Eur(1m), _clock);

        Assert.False(group.IsSatisfiable);
        DomainAssert.Throws(ModifierGroupErrors.RuleNotSatisfiable, group.EnsureSatisfiable);

        group.AddModifier(Name("Bacon"), Eur(2m), _clock);
        Assert.True(group.IsSatisfiable);
        group.EnsureSatisfiable();

        group.SetModifierAvailability(cheese, false, _clock);
        Assert.False(group.IsSatisfiable);
        DomainAssert.Throws(ModifierGroupErrors.RuleNotSatisfiable, group.EnsureSatisfiable);
    }

    [Fact]
    public void An_optional_group_is_satisfiable_even_when_empty()
    {
        var group = NewGroup(min: 0, max: 3);

        Assert.True(group.IsSatisfiable);
        group.EnsureSatisfiable();
    }

    [Fact]
    public void EnsureSelectionIsValid_accepts_a_selection_within_the_bounds()
    {
        var group = NewGroup(min: 1, max: 2);
        var cheese = group.AddModifier(Name("Cheese"), Eur(1m), _clock);
        var bacon = group.AddModifier(Name("Bacon"), Eur(2m), _clock);

        group.EnsureSelectionIsValid([cheese]);
        group.EnsureSelectionIsValid([cheese, bacon]);
    }

    [Fact]
    public void EnsureSelectionIsValid_rejects_too_few_and_too_many()
    {
        var group = NewGroup(min: 1, max: 2);
        var cheese = group.AddModifier(Name("Cheese"), Eur(1m), _clock);
        var bacon = group.AddModifier(Name("Bacon"), Eur(2m), _clock);
        var egg = group.AddModifier(Name("Egg"), Eur(1m), _clock);

        DomainAssert.Throws(ModifierGroupErrors.SelectionBelowMin, () => group.EnsureSelectionIsValid([]));
        DomainAssert.Throws(ModifierGroupErrors.SelectionAboveMax, () => group.EnsureSelectionIsValid([cheese, bacon, egg]));
    }

    [Fact]
    public void EnsureSelectionIsValid_rejects_duplicate_unknown_and_unavailable_modifiers()
    {
        var group = NewGroup(min: 0, max: 3);
        var cheese = group.AddModifier(Name("Cheese"), Eur(1m), _clock);
        var bacon = group.AddModifier(Name("Bacon"), Eur(2m), _clock);
        group.SetModifierAvailability(bacon, false, _clock);

        DomainAssert.Throws(ModifierGroupErrors.SelectionDuplicate, () => group.EnsureSelectionIsValid([cheese, cheese]));
        DomainAssert.Throws(ModifierGroupErrors.ModifierNotFound, () => group.EnsureSelectionIsValid([ModifierId.New()]));
        DomainAssert.Throws(ModifierGroupErrors.SelectionModifierUnavailable, () => group.EnsureSelectionIsValid([cheese, bacon]));
    }

    [Fact]
    public void ToReference_carries_id_tenant_and_currency()
    {
        var group = NewGroup();

        var reference = group.ToReference();

        Assert.Equal(group.Id, reference.Id);
        Assert.Equal(Tenant, reference.TenantId);
        Assert.Equal(Currency.EUR, reference.Currency);
    }
}
