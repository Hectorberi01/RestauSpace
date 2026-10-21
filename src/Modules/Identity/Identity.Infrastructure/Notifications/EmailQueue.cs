using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Identity.Infrastructure.Notifications;

/// <summary>
/// Les emails attendent ici et partent en arrière-plan : une requête n'attend jamais le serveur mail, donc sa durée
/// ne révèle pas si un email a été envoyé. En mémoire volontairement : un lien envoyé par email est un secret, il
/// n'est écrit nulle part. Un email perdu au redémarrage se récupère en demandant un nouveau lien.
/// </summary>
internal sealed class EmailQueue
{
    private readonly Channel<EmailMessage> _channel = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(1000) { FullMode = BoundedChannelFullMode.DropOldest });

    public ChannelReader<EmailMessage> Reader => _channel.Reader;

    public void Enqueue(EmailMessage message) => _channel.Writer.TryWrite(message);
}

internal sealed class EmailDispatchService(EmailQueue queue, IEmailSender sender, ILogger<EmailDispatchService> logger)
    : BackgroundService
{
    private const int MaxAttempts = 3;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
            {
                await SendWithRetriesAsync(message, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task SendWithRetriesAsync(EmailMessage message, CancellationToken stoppingToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await sender.SendAsync(message, stoppingToken);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (attempt == MaxAttempts)
                {
                    // Jamais le corps : il contient le lien.
                    logger.LogError(exception, "Email \"{Subject}\" could not be sent after {Attempts} attempts.", message.Subject, attempt);
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), stoppingToken);
            }
        }
    }
}
