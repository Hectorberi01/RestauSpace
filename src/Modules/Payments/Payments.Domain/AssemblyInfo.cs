using System.Runtime.CompilerServices;

// Permet aux tests d'exercer les gardes propres aux entités enfants (tentative/remboursement terminal), inatteignables
// via la racine car celle-ci absorbe les doublons exacts avant de déléguer.
[assembly: InternalsVisibleTo("Payments.Domain.Tests")]
