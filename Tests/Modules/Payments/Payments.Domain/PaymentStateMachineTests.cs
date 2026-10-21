namespace Payments.Domain;

/// <summary>
/// Étape 5 : la machine à états de <see cref="Payment"/>, cas par cas. Pour CHAQUE statut et CHAQUE opération métier,
/// le résultat attendu est écrit : nouveau statut, rejeu sans effet, ou refus avec son erreur. Un refus ou un rejeu ne
/// modifie rien et ne lève aucun événement. Le statut ne change jamais autrement que par ces méthodes (aucun setter
/// public : test d'architecture <c>Entities_have_no_public_setter</c>).
/// </summary>
public class PaymentStateMachineTests
{
    // Opérations, toutes sur la tentative « pi_1 » des paiements de Given.PaymentIn (100.00 EUR).
    public const string NewAttemptOp = "RegisterAttempt";
    public const string AuthorizeOp = "Authorize";
    public const string CaptureOp = "Capture";
    public const string FailOp = "Fail";
    public const string CancelOp = "Cancel";
    public const string RefundOp = "RequestRefund";

    [Theory]
    // Pending : une tentative ouverte.
    [InlineData(PaymentStatus.Pending, NewAttemptOp, null, nameof(PaymentErrors.AttemptInProgress))]
    [InlineData(PaymentStatus.Pending, AuthorizeOp, PaymentStatus.Authorized, null)]
    [InlineData(PaymentStatus.Pending, CaptureOp, PaymentStatus.Captured, null)]
    [InlineData(PaymentStatus.Pending, FailOp, PaymentStatus.Failed, null)]
    [InlineData(PaymentStatus.Pending, CancelOp, PaymentStatus.Cancelled, null)]
    [InlineData(PaymentStatus.Pending, RefundOp, null, nameof(PaymentErrors.RefundNotAllowed))]
    // Authorized : la tentative autorisée est encore ouverte.
    [InlineData(PaymentStatus.Authorized, NewAttemptOp, null, nameof(PaymentErrors.AttemptInProgress))]
    [InlineData(PaymentStatus.Authorized, AuthorizeOp, PaymentStatus.Authorized, null)]
    [InlineData(PaymentStatus.Authorized, CaptureOp, PaymentStatus.Captured, null)]
    [InlineData(PaymentStatus.Authorized, FailOp, PaymentStatus.Failed, null)]
    [InlineData(PaymentStatus.Authorized, CancelOp, PaymentStatus.Cancelled, null)]
    [InlineData(PaymentStatus.Authorized, RefundOp, null, nameof(PaymentErrors.RefundNotAllowed))]
    // Failed : seul un nouvel essai (ou l'annulation) le fait avancer ; la tentative échouée est définitive.
    [InlineData(PaymentStatus.Failed, NewAttemptOp, PaymentStatus.Pending, null)]
    [InlineData(PaymentStatus.Failed, AuthorizeOp, null, nameof(PaymentErrors.AttemptAlreadyFailed))]
    [InlineData(PaymentStatus.Failed, CaptureOp, null, nameof(PaymentErrors.AttemptAlreadyFailed))]
    [InlineData(PaymentStatus.Failed, FailOp, PaymentStatus.Failed, null)]
    [InlineData(PaymentStatus.Failed, CancelOp, PaymentStatus.Cancelled, null)]
    [InlineData(PaymentStatus.Failed, RefundOp, null, nameof(PaymentErrors.RefundNotAllowed))]
    // Cancelled : terminal (une capture tardive passe par RegisterLateCapture, testé à part).
    [InlineData(PaymentStatus.Cancelled, NewAttemptOp, null, nameof(PaymentErrors.AlreadyCancelled))]
    [InlineData(PaymentStatus.Cancelled, AuthorizeOp, null, nameof(PaymentErrors.AttemptAlreadyFailed))]
    [InlineData(PaymentStatus.Cancelled, CaptureOp, null, nameof(PaymentErrors.AttemptAlreadyFailed))]
    [InlineData(PaymentStatus.Cancelled, FailOp, PaymentStatus.Cancelled, null)]
    [InlineData(PaymentStatus.Cancelled, CancelOp, null, nameof(PaymentErrors.AlreadyCancelled))]
    [InlineData(PaymentStatus.Cancelled, RefundOp, null, nameof(PaymentErrors.RefundNotAllowed))]
    // Captured : seuls les remboursements le font avancer ; les rejeux du prestataire sont absorbés.
    [InlineData(PaymentStatus.Captured, NewAttemptOp, null, nameof(PaymentErrors.AttemptNotAllowed))]
    [InlineData(PaymentStatus.Captured, AuthorizeOp, PaymentStatus.Captured, null)]
    [InlineData(PaymentStatus.Captured, CaptureOp, PaymentStatus.Captured, null)]
    [InlineData(PaymentStatus.Captured, FailOp, null, nameof(PaymentErrors.AttemptAlreadySucceeded))]
    [InlineData(PaymentStatus.Captured, CancelOp, null, nameof(PaymentErrors.CancelNotAllowed))]
    [InlineData(PaymentStatus.Captured, RefundOp, PaymentStatus.Captured, null)]
    // PartiallyRefunded : encore remboursable.
    [InlineData(PaymentStatus.PartiallyRefunded, NewAttemptOp, null, nameof(PaymentErrors.AttemptNotAllowed))]
    [InlineData(PaymentStatus.PartiallyRefunded, AuthorizeOp, PaymentStatus.PartiallyRefunded, null)]
    [InlineData(PaymentStatus.PartiallyRefunded, CaptureOp, PaymentStatus.PartiallyRefunded, null)]
    [InlineData(PaymentStatus.PartiallyRefunded, FailOp, null, nameof(PaymentErrors.AttemptAlreadySucceeded))]
    [InlineData(PaymentStatus.PartiallyRefunded, CancelOp, null, nameof(PaymentErrors.CancelNotAllowed))]
    [InlineData(PaymentStatus.PartiallyRefunded, RefundOp, PaymentStatus.PartiallyRefunded, null)]
    // Refunded : terminal, plus rien n'est remboursable.
    [InlineData(PaymentStatus.Refunded, NewAttemptOp, null, nameof(PaymentErrors.AttemptNotAllowed))]
    [InlineData(PaymentStatus.Refunded, AuthorizeOp, PaymentStatus.Refunded, null)]
    [InlineData(PaymentStatus.Refunded, CaptureOp, PaymentStatus.Refunded, null)]
    [InlineData(PaymentStatus.Refunded, FailOp, null, nameof(PaymentErrors.AttemptAlreadySucceeded))]
    [InlineData(PaymentStatus.Refunded, CancelOp, null, nameof(PaymentErrors.CancelNotAllowed))]
    [InlineData(PaymentStatus.Refunded, RefundOp, null, nameof(PaymentErrors.RefundNotAllowed))]
    public void Every_operation_in_every_status_has_one_written_outcome(
        PaymentStatus from, string operation, PaymentStatus? expectedStatus, string? expectedError)
    {
        var clock = new FakeClock();
        var payment = Given.PaymentIn(from, clock);
        payment.ClearDomainEvents();
        var attemptsBefore = payment.Attempts.Count;
        var refundsBefore = payment.Refunds.Count;

        void Act()
        {
            switch (operation)
            {
                case NewAttemptOp:
                    payment.RegisterAttempt(Given.Ref("pi_new"), clock);
                    break;
                case AuthorizeOp:
                    payment.Authorize(Given.Ref("pi_1"), clock);
                    break;
                case CaptureOp:
                    payment.Capture(Given.Ref("pi_1"), Given.Eur(100m), clock);
                    break;
                case FailOp:
                    payment.Fail(Given.Ref("pi_1"), Given.Code("card_declined"), clock);
                    break;
                case CancelOp:
                    payment.Cancel("Client left", clock);
                    break;
                case RefundOp:
                    payment.RequestRefund(Given.Eur(10m), "Gesture", clock);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(operation));
            }
        }

        if (expectedError is not null)
        {
            var error = (DomainError)typeof(PaymentErrors).GetField(expectedError)!.GetValue(null)!;
            DomainAssert.Throws(error, Act);
            // Un refus ne change rien.
            Assert.Equal(from, payment.Status);
            Assert.Equal((attemptsBefore, refundsBefore), (payment.Attempts.Count, payment.Refunds.Count));
            DomainAssert.NoEvents(payment);
            return;
        }

        Act();

        Assert.Equal(expectedStatus, payment.Status);
        if (expectedStatus == from && operation != RefundOp)
        {
            // Rejeu absorbé : aucun effet, aucun événement.
            DomainAssert.NoEvents(payment);
        }
        else
        {
            Assert.NotEmpty(payment.DomainEvents);
        }
    }

    [Fact]
    public void Terminal_payments_keep_their_money_untouched_whatever_is_tried()
    {
        var clock = new FakeClock();
        foreach (var status in new[] { PaymentStatus.Cancelled, PaymentStatus.Refunded })
        {
            var payment = Given.PaymentIn(status, clock);
            var (captured, refunded) = (payment.CapturedAmount, payment.RefundedAmount);

            Assert.ThrowsAny<DomainException>(() => payment.RequestRefund(Given.Eur(1m), "Again", clock));
            Assert.ThrowsAny<DomainException>(() => payment.RegisterAttempt(Given.Ref("pi_x"), clock));
            Assert.ThrowsAny<DomainException>(() => payment.Cancel("Again", clock));

            Assert.Equal((status, captured, refunded), (payment.Status, payment.CapturedAmount, payment.RefundedAmount));
        }
    }
}
