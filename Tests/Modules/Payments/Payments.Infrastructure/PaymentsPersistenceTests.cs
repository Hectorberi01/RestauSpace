using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payments.Application.Abstractions;
using Payments.Domain;
using Payments.Infrastructure.Persistence;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Payments.Infrastructure;

public sealed class PaymentsPersistenceTests(PaymentsDatabase database) : IClassFixture<PaymentsDatabase>
{
    private FakeClock Clock => database.Clock;

    private static Money Xof(decimal amount) => Money.Of(amount, Currency.XOF);

    private static OrderReference Order(TenantId tenantId, OrderId? orderId = null, decimal due = 7000m) =>
        OrderReference.Create(tenantId, orderId ?? OrderId.New(), PaymentsDatabase.Location, Xof(due));

    private Payment Online(TenantId tenantId, string provider = "testpay", OrderId? orderId = null, decimal amount = 7000m, string? reference = null)
    {
        var payment = Payment.Create(database.Accepting(tenantId, provider), PaymentMethodType.Card, Order(tenantId, orderId), Xof(amount), Clock);
        payment.RegisterAttempt(ProviderReference.Create(reference ?? "tp_" + Guid.NewGuid().ToString("N")), Clock);
        return payment;
    }

    private RestaurantPaymentConfiguration Cash(TenantId tenantId) =>
        database.Accepting(tenantId, "cash", PaymentProviderType.OnSite, PaymentMethodType.Cash);

    private async Task SaveAsync(Payment payment)
    {
        await using var scope = database.NewScope(payment.TenantId);
        scope.ServiceProvider.GetRequiredService<IPaymentRepository>().Add(payment);
        await scope.ServiceProvider.GetRequiredService<IPaymentsUnitOfWork>().SaveChangesAsync();
    }

    private async Task EditAsync(TenantId tenantId, PaymentId id, Action<Payment> change)
    {
        await using var scope = database.NewScope(tenantId);
        change((await scope.ServiceProvider.GetRequiredService<IPaymentRepository>().FindAsync(tenantId, id))!);
        await scope.ServiceProvider.GetRequiredService<IPaymentsUnitOfWork>().SaveChangesAsync();
    }

    private async Task<T> InScopeAsync<T>(TenantId? tenantId, Func<IServiceProvider, Task<T>> read)
    {
        await using var scope = database.NewScope(tenantId);
        return await read(scope.ServiceProvider);
    }

    [Fact]
    public async Task A_payment_comes_back_with_its_attempts_and_refunds_in_order_and_its_events_in_the_outbox()
    {
        var tenant = TenantId.New();
        var payment = Online(tenant, reference: "tp_first");
        payment.Fail(ProviderReference.Create("tp_first"), PaymentFailureCode.Create("declined"), Clock, "Declined");
        payment.RegisterAttempt(ProviderReference.Create("tp_second"), Clock);
        payment.Capture(ProviderReference.Create("tp_second"), Xof(7000m), Clock);
        await SaveAsync(payment);

        // Plusieurs remboursements dans la même milliseconde : seul l'ordre de la liste fait foi, pas celui des ids.
        await EditAsync(tenant, payment.Id, loaded =>
        {
            loaded.RequestRefund(Xof(1000m), "Cold dish", Clock);
            loaded.RequestRefund(Xof(500m), "Missing drink", Clock);
            loaded.RequestRefund(Xof(250m), "Late", Clock);
        });
        var refunds = (await InScopeAsync(tenant, services => services.GetRequiredService<IPaymentRepository>().FindAsync(tenant, payment.Id)))!.Refunds;
        await EditAsync(tenant, payment.Id, loaded => loaded.RegisterRefund(refunds.First().Id, RefundOutcome.Succeeded, Clock));

        var reloaded = await InScopeAsync(tenant, services => services.GetRequiredService<IPaymentRepository>().FindAsync(tenant, payment.Id));
        Assert.NotNull(reloaded);
        Assert.Equal(new[] { "tp_first", "tp_second" }, reloaded.Attempts.Select(attempt => attempt.ProviderReference.Value).ToArray());
        Assert.Equal("Declined", reloaded.Attempts.First().FailureReason);
        Assert.Equal(PaymentFailureCode.Create("declined"), reloaded.Attempts.First().FailureCode);
        Assert.Equal(new[] { 1000m, 500m, 250m }, reloaded.Refunds.Select(refund => refund.Amount.Amount).ToArray());
        Assert.Equal(PaymentStatus.PartiallyRefunded, reloaded.Status);
        Assert.Equal(Xof(7000m), reloaded.CapturedAmount);
        Assert.Equal(Xof(1000m), reloaded.RefundedAmount);
        Assert.Equal(Xof(5250m), reloaded.SettledAmount);
        Assert.Equal(PaymentProviderCode.Create("testpay"), reloaded.Provider);

        var types = await InScopeAsync(tenant, services => services.GetRequiredService<PaymentsDbContext>().OutboxMessages
            .Select(message => message.Type)
            .ToListAsync());
        Assert.Contains(nameof(PaymentCaptured), types);
        Assert.Contains(nameof(RefundRequested), types);
        Assert.Contains(nameof(RefundSucceeded), types);
        Assert.Contains(nameof(PaymentSettlementChanged), types);
    }

    [Fact]
    public async Task Payments_are_found_by_order_and_by_provider_reference_in_their_tenant_only()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var order = OrderId.New();
        var first = Online(tenantA, orderId: order, amount: 3000m, reference: "tp_shared_" + Guid.NewGuid().ToString("N"));
        Clock.Advance(TimeSpan.FromSeconds(1));
        var second = Payment.RecordOffline(Cash(tenantA), PaymentMethodType.Cash, Order(tenantA, order, 4000m), Xof(4000m), ProviderReference.Create("T-1"), Clock);
        await SaveAsync(first);
        await SaveAsync(second);
        var reference = first.Attempts.Single().ProviderReference;

        var byOrder = await InScopeAsync(tenantA, services => services.GetRequiredService<IPaymentRepository>().ListByOrderAsync(tenantA, order));
        var byReference = await InScopeAsync(tenantA, services => services.GetRequiredService<IPaymentRepository>()
            .FindByProviderReferenceAsync(tenantA, PaymentProviderCode.Create("testpay"), reference));
        var otherProvider = await InScopeAsync(tenantA, services => services.GetRequiredService<IPaymentRepository>()
            .FindByProviderReferenceAsync(tenantA, PaymentProviderCode.Create("sandbox"), reference));

        Assert.Equal(new[] { first.Id, second.Id }, byOrder.Select(payment => payment.Id).ToArray());
        Assert.Equal(first.Id, byReference!.Id);
        Assert.Null(otherProvider);
        // Le filtre du repository ET le query filter refusent.
        Assert.Null(await InScopeAsync(tenantB, services => services.GetRequiredService<IPaymentRepository>().FindAsync(tenantA, first.Id)));
        Assert.Empty(await InScopeAsync(tenantB, services => services.GetRequiredService<IPaymentRepository>().ListByOrderAsync(tenantB, order)));
        Assert.Empty(await InScopeAsync(null, services => services.GetRequiredService<PaymentsDbContext>().Payments.ToListAsync()));
    }

    [Fact]
    public async Task Two_webhooks_deciding_on_the_same_payment_at_once_cannot_both_win()
    {
        var tenant = TenantId.New();
        var payment = Online(tenant);
        payment.Capture(payment.Attempts.Single().ProviderReference, Xof(7000m), Clock);
        payment.RequestRefund(Xof(7000m), "Order cancelled", Clock);
        await SaveAsync(payment);

        await using var first = database.NewScope(tenant);
        await using var second = database.NewScope(tenant);
        var mine = await first.ServiceProvider.GetRequiredService<IPaymentRepository>().FindAsync(tenant, payment.Id);
        var theirs = await second.ServiceProvider.GetRequiredService<IPaymentRepository>().FindAsync(tenant, payment.Id);
        var refund = mine!.Refunds.Single().Id;
        mine.RegisterRefund(refund, RefundOutcome.Failed, Clock);
        theirs!.RegisterRefund(refund, RefundOutcome.Failed, Clock);

        await first.ServiceProvider.GetRequiredService<IPaymentsUnitOfWork>().SaveChangesAsync();
        // Seul le remboursement change : c'est la version du paiement, réécrite, qui arrête le second.
        await Assert.ThrowsAsync<ConcurrencyException>(() => second.ServiceProvider.GetRequiredService<IPaymentsUnitOfWork>().SaveChangesAsync());
    }

    [Fact]
    public async Task Payments_of_one_order_are_created_one_at_a_time()
    {
        var tenant = TenantId.New();
        var order = OrderId.New();

        await using var first = database.NewScope(tenant);
        await using var second = database.NewScope(tenant);
        await first.ServiceProvider.GetRequiredService<IPaymentOrderLock>().LockAsync(tenant, order);
        var waiting = second.ServiceProvider.GetRequiredService<IPaymentOrderLock>().LockAsync(tenant, order);
        var otherOrder = database.NewScope(tenant);
        await otherOrder.ServiceProvider.GetRequiredService<IPaymentOrderLock>().LockAsync(tenant, OrderId.New());
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        var blocked = !waiting.IsCompleted;

        first.ServiceProvider.GetRequiredService<IPaymentRepository>().Add(
            Payment.RecordOffline(Cash(tenant), PaymentMethodType.Cash, Order(tenant, order), Xof(7000m), ProviderReference.Create("T-9"), Clock));
        await first.ServiceProvider.GetRequiredService<IPaymentsUnitOfWork>().SaveChangesAsync();
        await waiting.WaitAsync(TimeSpan.FromSeconds(10));
        var seen = await second.ServiceProvider.GetRequiredService<IPaymentRepository>().ListByOrderAsync(tenant, order);
        await otherOrder.DisposeAsync();

        Assert.True(blocked);
        // Le second encaissement voit le premier : il ne peut plus dépasser le reste à payer.
        Assert.Single(seen);
    }

    [Fact]
    public async Task Outbox_messages_reach_the_online_provider_of_the_restaurant_after_the_save()
    {
        var tenant = TenantId.New();
        await database.GivenOnlineConfigurationAsync(tenant, "testpay", new Dictionary<string, string> { ["api_key"] = "tp_key_" + tenant.Value });
        var refunded = Online(tenant);
        var reference = refunded.Attempts.Single().ProviderReference;
        refunded.Capture(reference, Xof(7000m), Clock);
        var refundId = refunded.RequestRefund(Xof(2000m), "Cold dish", Clock);
        var cancelled = Online(tenant);
        cancelled.Cancel("Client left", Clock);
        var cash = Payment.RecordOffline(Cash(tenant), PaymentMethodType.Cash, Order(tenant), Xof(1000m), ProviderReference.Create("T-3"), Clock);
        cash.RequestRefund(Xof(1000m), "Mistake", Clock);
        await SaveAsync(refunded);
        await SaveAsync(cancelled);
        await SaveAsync(cash);

        await database.Services.GetRequiredService<OutboxDispatcher<PaymentsDbContext>>().DispatchPendingAsync();

        var request = Assert.Single(database.TestPay.Refunds, candidate => candidate.PaymentId == refunded.Id);
        Assert.Equal((refundId, reference, Xof(2000m)), (request.RefundId, request.CapturedReference, request.Amount));
        Assert.Contains(cancelled.Attempts.Single().ProviderReference.Value, database.TestPay.Cancelled);
        Assert.DoesNotContain(database.TestPay.Refunds, candidate => candidate.PaymentId == cash.Id);
        // Le prestataire a été appelé avec la clé du compte marchand de CE restaurant.
        Assert.Contains("tp_key_" + tenant.Value, database.TestPay.ApiKeys);
    }

    [Fact]
    public async Task A_refund_the_provider_refuses_for_good_is_recorded_as_failed()
    {
        var tenant = TenantId.New();
        await database.GivenOnlineConfigurationAsync(tenant, "testpay", new Dictionary<string, string> { ["api_key"] = "tp_key" });
        var payment = Online(tenant);
        payment.Capture(payment.Attempts.Single().ProviderReference, Xof(7000m), Clock);
        payment.RequestRefund(Xof(7000m), "Order cancelled", Clock);
        await SaveAsync(payment);

        database.TestPay.RefuseRefunds = true;
        try
        {
            await database.Services.GetRequiredService<OutboxDispatcher<PaymentsDbContext>>().DispatchPendingAsync();
        }
        finally
        {
            database.TestPay.RefuseRefunds = false;
        }

        var reloaded = await InScopeAsync(tenant, services => services.GetRequiredService<IPaymentRepository>().FindAsync(tenant, payment.Id));
        Assert.Equal(RefundStatus.Failed, reloaded!.Refunds.Single().Status);
        Assert.Equal(Xof(7000m), reloaded.SettledAmount);
    }

    [Fact]
    public async Task The_catalog_and_the_configurations_come_back_with_only_a_reference_and_the_secret_encrypted_apart()
    {
        var tenant = TenantId.New();
        var configuration = await database.GivenOnlineConfigurationAsync(tenant, "testpay", new Dictionary<string, string> { ["api_key"] = "sk_live_secret" });
        var reference = PaymentCredentialReference.ForTenant(tenant, PaymentProviderCode.Create("testpay"));

        var catalog = await InScopeAsync(null, services => services.GetRequiredService<IPaymentProviderRepository>().ListAsync());
        var loaded = await InScopeAsync(tenant, services => services.GetRequiredService<IRestaurantPaymentConfigurationRepository>()
            .FindByProviderAsync(tenant, PaymentProviderCode.Create("testpay")));
        var secret = await InScopeAsync(null, services => services.GetRequiredService<IPaymentSecretStore>().ReadAsync(reference));
        var row = await ScalarAsync(
            "SELECT row_to_json(c)::text FROM payments.restaurant_payment_configurations c WHERE id = @id", configuration.Id.Value);
        var stored = await ScalarAsync("SELECT cipher_text FROM payments.payment_secrets WHERE reference = @id", reference.Value);

        // Les moyens du comptoir intégrés sont posés à la migration, actifs, pour toutes les devises.
        Assert.Equal(
            new[] { "bank_transfer", "card_terminal", "cash", "courier_cash", "delivery_platform", "gift_card", "mobile_money" },
            catalog.Where(provider => provider.Type == PaymentProviderType.OnSite).Select(provider => provider.Code.Value).Order(StringComparer.Ordinal).ToArray());
        Assert.All(
            catalog.Where(provider => provider.Type == PaymentProviderType.OnSite),
            provider => Assert.True(provider.IsEnabled && provider.SupportedCurrencies.Count == 0));
        Assert.NotNull(loaded);
        Assert.True(loaded.IsEnabled);
        Assert.Equal((PaymentMode.TenantManaged, PaymentConfigurationStatus.Configured), (loaded.Mode, loaded.Status));
        Assert.Equal(reference, loaded.SecretReference);
        Assert.Equal(new[] { PaymentMethodType.Card }, loaded.EnabledMethods.ToArray());
        Assert.Equal("sk_live_secret", secret!["api_key"]);
        // Ni la configuration, ni le coffre ne contiennent la clé en clair.
        Assert.DoesNotContain("sk_live_secret", row, StringComparison.Ordinal);
        Assert.StartsWith("v1:", stored, StringComparison.Ordinal);
        Assert.DoesNotContain("sk_live_secret", stored, StringComparison.Ordinal);
        // Un autre restaurant ne voit pas cette configuration, même avec son id.
        var other = TenantId.New();
        Assert.Null(await InScopeAsync(other, services => services.GetRequiredService<IRestaurantPaymentConfigurationRepository>().FindAsync(other, configuration.Id)));
    }

    [Fact]
    public async Task A_secret_copied_to_another_reference_cannot_be_read()
    {
        var tenant = TenantId.New();
        var thief = TenantId.New();
        await database.GivenOnlineConfigurationAsync(tenant, "copypay", new Dictionary<string, string> { ["api_key"] = "k_victim" });
        var victim = PaymentCredentialReference.ForTenant(tenant, PaymentProviderCode.Create("copypay"));
        var stolen = PaymentCredentialReference.ForTenant(thief, PaymentProviderCode.Create("copypay"));
        await CopySecretAsync(victim.Value, stolen.Value, thief.Value);

        var copied = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            InScopeAsync(null, services => services.GetRequiredService<IPaymentSecretStore>().ReadAsync(stolen)));

        Assert.DoesNotContain("k_victim", copied.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Assignments_to_locations_are_stored_in_order_with_their_bounds()
    {
        var tenant = TenantId.New();
        var configuration = await database.GivenOnlineConfigurationAsync(tenant, "assignpay", new Dictionary<string, string> { ["api_key"] = "k" });
        var first = new LocationReference(tenant, LocationId.New(), CountryCode.Create("BJ"));
        var second = new LocationReference(tenant, LocationId.New(), CountryCode.Create("BJ"));

        await InScopeAsync(tenant, async services =>
        {
            var loaded = (await services.GetRequiredService<IRestaurantPaymentConfigurationRepository>().FindAsync(tenant, configuration.Id))!;
            loaded.AssignToLocation(first, PaymentMethodType.Card, priority: 3, Clock, minAmount: Xof(500m), maxAmount: Xof(50_000m));
            loaded.AssignToLocation(second, PaymentMethodType.Card, priority: 1, Clock);
            await services.GetRequiredService<IPaymentsUnitOfWork>().SaveChangesAsync();
            return 0;
        });
        var reloaded = (await InScopeAsync(tenant, services =>
            services.GetRequiredService<IRestaurantPaymentConfigurationRepository>().FindAsync(tenant, configuration.Id)))!;

        Assert.False(reloaded.IsGlobal);
        Assert.Equal(new[] { first.LocationId, second.LocationId }, reloaded.Assignments.Select(assignment => assignment.LocationId).ToArray());
        var bounded = reloaded.Assignments.First();
        Assert.Equal((3, Xof(500m), Xof(50_000m)), (bounded.Priority, bounded.MinAmount, bounded.MaxAmount));
        Assert.Null(reloaded.Assignments.Last().MinAmount);
        Assert.True(reloaded.CanTake(first.LocationId, PaymentMethodType.Card, Xof(1_000m)));
        Assert.False(reloaded.CanTake(first.LocationId, PaymentMethodType.Card, Xof(100m)));
        Assert.False(reloaded.CanTake(LocationId.New(), PaymentMethodType.Card, Xof(1_000m)));
    }

    [Fact]
    public async Task A_restaurant_accepts_a_provider_once_even_under_a_race()
    {
        var tenant = TenantId.New();
        await database.GivenOnlineConfigurationAsync(tenant, "racepay", new Dictionary<string, string> { ["api_key"] = "k" });

        var refused = await Assert.ThrowsAsync<DomainException>(() =>
            database.GivenOnlineConfigurationAsync(tenant, "racepay", new Dictionary<string, string> { ["api_key"] = "k" }));

        Assert.Equal(PaymentConfigurationErrors.AlreadyExists, refused.Error);
    }

    [Fact]
    public async Task A_payment_comes_back_with_its_location_method_and_dates()
    {
        var tenant = TenantId.New();
        var payment = Online(tenant, provider: "datepay");
        var reference = payment.Attempts.Single().ProviderReference;
        payment.Authorize(reference, Clock);
        Clock.Advance(TimeSpan.FromSeconds(5));
        payment.Fail(reference, PaymentFailureCode.Create("authorization_expired"), Clock);
        await SaveAsync(payment);

        var reloaded = (await InScopeAsync(tenant, services => services.GetRequiredService<IPaymentRepository>().FindAsync(tenant, payment.Id)))!;

        Assert.Equal(PaymentsDatabase.Location, reloaded.LocationId);
        Assert.Equal(PaymentMethodType.Card, reloaded.Method);
        Assert.Equal(payment.AuthorizedAtUtc, reloaded.AuthorizedAtUtc);
        Assert.Equal(payment.FailedAtUtc, reloaded.FailedAtUtc);
        Assert.NotNull(reloaded.FailedAtUtc);
    }

    [Fact]
    public async Task The_schema_has_the_tables_of_the_specification()
    {
        var tables = await InScopeAsync(null, async services =>
        {
            var connection = services.GetRequiredService<PaymentsDbContext>().Database.GetDbConnection();
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = 'payments'";
            var names = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                names.Add(reader.GetString(0));
            }

            return names;
        });

        Assert.Superset(
            new HashSet<string>
            {
                "payment_providers", "restaurant_payment_configurations", "payment_method_assignments", "payments",
                "payment_attempts", "refunds", "provider_webhook_events", "payment_secrets", "outbox_deliveries", "outbox_messages",
            },
            tables.ToHashSet());
    }

    [Fact]
    public async Task Every_child_row_carries_the_tenant_of_its_aggregate()
    {
        var tenant = TenantId.New();
        var payment = Online(tenant);
        payment.Capture(payment.Attempts.Single().ProviderReference, Xof(7000m), Clock);
        payment.RequestRefund(Xof(500m), "Cold dish", Clock);
        await SaveAsync(payment);
        var configuration = await database.GivenOnlineConfigurationAsync(tenant, "tenantpay", new Dictionary<string, string> { ["api_key"] = "k" });
        await InScopeAsync(tenant, async services =>
        {
            var loaded = (await services.GetRequiredService<IRestaurantPaymentConfigurationRepository>().FindAsync(tenant, configuration.Id))!;
            loaded.AssignToLocation(new LocationReference(tenant, PaymentsDatabase.Location, CountryCode.Create("BJ")), PaymentMethodType.Card, priority: 0, Clock);
            await services.GetRequiredService<IPaymentsUnitOfWork>().SaveChangesAsync();
            return 0;
        });

        var attempts = await ScalarAsync("SELECT count(*)::text FROM payments.payment_attempts WHERE tenant_id = @id", tenant.Value);
        var refunds = await ScalarAsync("SELECT count(*)::text FROM payments.refunds WHERE tenant_id = @id", tenant.Value);
        var assignments = await ScalarAsync("SELECT count(*)::text FROM payments.payment_method_assignments WHERE tenant_id = @id", tenant.Value);
        var secrets = await ScalarAsync("SELECT count(*)::text FROM payments.payment_secrets WHERE tenant_id = @id", tenant.Value);

        Assert.Equal(("1", "1", "1", "1"), (attempts, refunds, assignments, secrets));
    }

    [Fact]
    public async Task The_database_refuses_an_amount_the_domain_would_never_write()
    {
        var tenant = TenantId.New();
        var payment = Online(tenant);
        await SaveAsync(payment);

        var refused = await Assert.ThrowsAnyAsync<Npgsql.PostgresException>(() =>
            ExecuteAsync("UPDATE payments.payments SET amount = -1 WHERE id = @id", payment.Id.Value));

        Assert.Equal(Npgsql.PostgresErrorCodes.CheckViolation, refused.SqlState);
        Assert.Equal("ck_payments_amount_positive", refused.ConstraintName);
    }

    [Theory]
    [InlineData("UPDATE payments.payments SET status = 99 WHERE id = @id", "ck_payments_status")]
    [InlineData("UPDATE payments.payments SET method = 99 WHERE id = @id", "ck_payments_method")]
    [InlineData("UPDATE payments.payments SET captured_amount_currency = 'EUR' WHERE id = @id", "ck_payments_captured_amount_currency")]
    [InlineData("UPDATE payments.payments SET amount_currency = 'xo1', captured_amount_currency = 'xo1' WHERE id = @id", "ck_payments_amount_currency")]
    [InlineData("UPDATE payments.payment_attempts SET status = 0 WHERE payment_id = @id", "ck_payment_attempts_status")]
    public async Task The_database_refuses_a_payment_row_the_domain_would_never_write(string sql, string constraint)
    {
        var tenant = TenantId.New();
        var payment = Online(tenant);
        await SaveAsync(payment);

        var refused = await Assert.ThrowsAnyAsync<Npgsql.PostgresException>(() => ExecuteAsync(sql, payment.Id.Value));

        Assert.Equal((Npgsql.PostgresErrorCodes.CheckViolation, constraint), (refused.SqlState, refused.ConstraintName));
    }

    [Fact]
    public async Task A_configuration_cannot_point_to_the_vault_of_another_restaurant_or_of_the_platform()
    {
        var tenant = TenantId.New();
        var configuration = await database.GivenOnlineConfigurationAsync(tenant, "vaultpay", new Dictionary<string, string> { ["api_key"] = "k" });
        var otherRestaurant = PaymentCredentialReference.ForTenant(TenantId.New(), PaymentProviderCode.Create("vaultpay")).Value;
        var platform = PaymentCredentialReference.ForPlatform(PaymentProviderCode.Create("vaultpay")).Value;

        foreach (var reference in new[] { otherRestaurant, platform })
        {
            var refused = await Assert.ThrowsAnyAsync<Npgsql.PostgresException>(() => ExecuteAsync(
                "UPDATE payments.restaurant_payment_configurations SET secret_reference = '" + reference + "' WHERE id = @id",
                configuration.Id.Value));

            Assert.Equal(
                (Npgsql.PostgresErrorCodes.CheckViolation, "ck_restaurant_payment_configurations_secret_reference_tenant"),
                (refused.SqlState, refused.ConstraintName));
        }
    }

    [Fact]
    public async Task A_vault_row_always_belongs_to_the_tenant_of_its_reference()
    {
        var tenant = TenantId.New();
        var thief = TenantId.New();
        await database.GivenOnlineConfigurationAsync(tenant, "ownerpay", new Dictionary<string, string> { ["api_key"] = "k_victim" });
        var victim = PaymentCredentialReference.ForTenant(tenant, PaymentProviderCode.Create("ownerpay")).Value;
        var stolen = PaymentCredentialReference.ForTenant(thief, PaymentProviderCode.Create("ownerpay")).Value;

        // Sous la référence d'un autre restaurant en gardant le propriétaire d'origine, ou sans propriétaire.
        foreach (var owner in new Guid?[] { tenant.Value, null })
        {
            var refused = await Assert.ThrowsAnyAsync<Npgsql.PostgresException>(() => CopySecretAsync(victim, stolen, owner));

            Assert.Equal((Npgsql.PostgresErrorCodes.CheckViolation, "ck_payment_secrets_owner"), (refused.SqlState, refused.ConstraintName));
        }
    }

    [Fact]
    public async Task A_provider_keeps_every_capability_across_a_reload()
    {
        // Toutes les capacités à une valeur autre que celle par défaut : un champ oublié par le mapping jsonb se voit.
        var capability = PaymentProviderCapability.Create(
            new[] { PaymentMethodType.Card, PaymentMethodType.MobileMoney },
            new[] { CountryCode.Create("BJ"), CountryCode.Create("TG") },
            new[] { Currency.XOF, Currency.EUR },
            supportsAuthorization: true,
            supportsRefund: true,
            supportsPartialRefund: true,
            supportsWebhooks: true,
            supportsRecurring: true,
            paymentExpiration: TimeSpan.FromMinutes(30),
            amountLimits: new[] { PaymentAmountLimit.Create(Currency.XOF, 100m, 1_000_000m) },
            supportsPlatformManagedMode: true);
        var provider = PaymentProvider.Register(
            PaymentProviderCode.Create("fullpay" + Guid.NewGuid().ToString("N")[..8]),
            PaymentProviderType.Online,
            "Full Pay",
            capability,
            isCodeUnique: true,
            Clock);
        await InScopeAsync(null, async services =>
        {
            services.GetRequiredService<IPaymentProviderRepository>().Add(provider);
            await services.GetRequiredService<IPaymentsUnitOfWork>().SaveChangesAsync();
            return 0;
        });

        var reloaded = await InScopeAsync(null, services => services.GetRequiredService<IPaymentProviderRepository>().FindAsync(provider.Id));

        Assert.NotNull(reloaded);
        Assert.Equal(capability, reloaded.Capability);
        Assert.True(reloaded.Capability.SupportsPlatformManagedMode);
    }

    /// <summary>Une valeur lue en SQL brut (paramètre <c>@id</c>), pour vérifier ce qui est réellement stocké.</summary>
    private Task<string> ScalarAsync(string sql, object id) =>
        InScopeAsync(null, async services =>
        {
            var connection = services.GetRequiredService<PaymentsDbContext>().Database.GetDbConnection();
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "id";
            parameter.Value = id;
            command.Parameters.Add(parameter);
            return (string)(await command.ExecuteScalarAsync())!;
        });

    private Task<int> ExecuteAsync(string sql, object id) =>
        InScopeAsync(null, async services =>
        {
            var connection = services.GetRequiredService<PaymentsDbContext>().Database.GetDbConnection();
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var parameter = command.CreateParameter();
            parameter.ParameterName = "id";
            parameter.Value = id;
            command.Parameters.Add(parameter);
            return await command.ExecuteNonQueryAsync();
        });

    /// <summary>
    /// Un attaquant ayant accès à la base recopie le chiffré d'un restaurant sous la référence d'un autre, avec le tenant
    /// <paramref name="ownerTenant"/> (celui de la nouvelle référence pour passer la contrainte d'appartenance).
    /// </summary>
    private Task<int> CopySecretAsync(string from, string to, Guid? ownerTenant) =>
        InScopeAsync(null, async services =>
        {
            var connection = services.GetRequiredService<PaymentsDbContext>().Database.GetDbConnection();
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO payments.payment_secrets (reference, tenant_id, cipher_text, updated_at_utc)
                SELECT @to, @tenant, cipher_text, updated_at_utc FROM payments.payment_secrets WHERE reference = @from
                """;
            foreach (var (name, value) in new (string, object)[] { ("from", from), ("to", to), ("tenant", (object?)ownerTenant ?? DBNull.Value) })
            {
                var parameter = command.CreateParameter();
                parameter.ParameterName = name;
                parameter.Value = value;
                command.Parameters.Add(parameter);
            }

            return await command.ExecuteNonQueryAsync();
        });
}
