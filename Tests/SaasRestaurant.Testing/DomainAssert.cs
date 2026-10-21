using SaasRestaurant.BuildingBlocks.Domain;
using Xunit;

namespace SaasRestaurant.Testing;

public static class DomainAssert
{
    /// <summary>Vérifie que <paramref name="action"/> enfreint la règle métier identifiée par <paramref name="expected"/>.</summary>
    public static DomainException Throws(DomainError expected, Action action)
    {
        var exception = Assert.Throws<DomainException>(action);
        Assert.Equal(expected.Code, exception.Code);
        return exception;
    }

    public static DomainException Throws(DomainError expected, Func<object?> action) =>
        Throws(expected, () => { _ = action(); });

    /// <summary>Vérifie qu'exactement un événement de type <typeparamref name="TEvent"/> a été levé et le renvoie.</summary>
    public static TEvent Raised<TEvent>(IAggregateRoot aggregate)
        where TEvent : IDomainEvent =>
        Assert.Single(aggregate.DomainEvents.OfType<TEvent>());

    public static void NotRaised<TEvent>(IAggregateRoot aggregate)
        where TEvent : IDomainEvent =>
        Assert.Empty(aggregate.DomainEvents.OfType<TEvent>());

    public static void NoEvents(IAggregateRoot aggregate) => Assert.Empty(aggregate.DomainEvents);
}
