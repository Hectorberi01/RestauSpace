using SaasRestaurant.BuildingBlocks.Application;
using SaasRestaurant.Contracts;

namespace SaasRestaurant.Host;

internal static class IntegrationEvents
{
    public static readonly DomainError ContractMismatch =
        new("outbox.contract.mismatch", "The message does not match the integration contract of its consumer.");

    public static T Read<T>(this OutboxEnvelope message)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(message);
        try
        {
            return IntegrationEventReader.Read<T>(message.Module, message.Type, message.Version, message.Content);
        }
        catch (IntegrationContractException exception)
        {
            throw new DomainException(ContractMismatch, exception);
        }
    }
}
